using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace KlaraHome.IntegrationTests.Commerce;

/// <summary>
/// Reading a response the way an assertion needs to read it.
/// </summary>
/// <remarks>
/// <para>
/// <c>EnsureSuccessStatusCode</c> throws the problem document away, and the problem document is the
/// only part of a failure that says <em>why</em>. Every one of these keeps the body and puts it in
/// the assertion message, so a broken test names the error code the server actually answered rather
/// than leaving somebody to re-run it with a debugger attached.
/// </para>
/// <para>
/// Static and shared rather than protected members of the test base, because the scenario builder
/// needs exactly the same reading and a second copy of it would drift.
/// </para>
/// </remarks>
internal static class Rest
{
    /// <summary>Reads a successful response as JSON, failing with the body when it was not successful.</summary>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<JsonElement> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.True(
            response.IsSuccessStatusCode,
            $"{(int)response.StatusCode} {response.StatusCode} from "
            + $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {body}");

        return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>Asserts a response failed with a given status and error code, and returns the problem.</summary>
    /// <param name="response">The response.</param>
    /// <param name="status">The status it must carry.</param>
    /// <param name="code">The stable error code it must name, or null to accept any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<JsonElement> RefusedAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string? code,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Assert.True(
            status == response.StatusCode,
            $"Expected {(int)status} but got {(int)response.StatusCode} from "
            + $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {body}");

        if (body.Length == 0)
        {
            Assert.Null(code);
            return default;
        }

        var problem = JsonDocument.Parse(body).RootElement.Clone();

        if (code is not null)
        {
            Assert.True(
                problem.TryGetProperty("code", out var actual) && actual.GetString() == code,
                $"Expected error code '{code}' but the problem was: {body}");
        }

        return problem;
    }

    /// <summary>
    /// Sends a body exactly as written, which <c>PostAsJsonAsync</c> cannot.
    /// </summary>
    /// <remarks>
    /// The webhook tests need the bytes they signed to be the bytes that arrive: re-serialising a
    /// document produces a different byte sequence and a signature that correctly fails to verify,
    /// which would make every one of those tests pass for the wrong reason.
    /// </remarks>
    /// <param name="client">The client to send on.</param>
    /// <param name="path">The path.</param>
    /// <param name="rawBody">The body, exactly as it should arrive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="headers">Headers to set on the request.</param>
    public static async Task<HttpResponseMessage> PostRawAsync(
        HttpClient client,
        string path,
        string rawBody,
        CancellationToken cancellationToken,
        params (string Name, string Value)[] headers)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(headers);

        // Awaited rather than returned bare: the `using` below must not dispose the request — and
        // the `StringContent` it owns — until the send has actually finished reading it. Returning
        // the unawaited task let the `using` run its Dispose the moment this method returned, which
        // raced the in-memory test host's own read of the body and failed it with
        // ObjectDisposedException roughly as often as the host won that race.
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(path, UriKind.Relative))
        {
            Content = new StringContent(rawBody, Encoding.UTF8, "application/json"),
        };

        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>Uploads a file through the media endpoints, as a person would.</summary>
    /// <param name="client">A client with permission to upload.</param>
    /// <param name="content">The bytes.</param>
    /// <param name="fileName">The name they arrive under.</param>
    /// <param name="contentType">Their media type.</param>
    /// <param name="visibility">Which bucket: <c>private</c> or <c>public</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        byte[] content,
        string fileName,
        string contentType,
        string? visibility,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(content);

        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);

        var url = visibility is null ? "/api/v1/admin/media" : $"/api/v1/admin/media?visibility={visibility}";

        return await client.PostAsync(new Uri(url, UriKind.Relative), form, cancellationToken);
    }

    /// <summary>A PNG header carrying the given dimensions. Enough for the inspector to read.</summary>
    /// <param name="width">Pixel width.</param>
    /// <param name="height">Pixel height.</param>
    public static byte[] Png(int width, int height)
    {
        var bytes = new byte[33];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);

        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);

        bytes[24] = 8;
        bytes[25] = 6;

        return bytes;
    }
}
