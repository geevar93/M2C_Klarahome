using System.Buffers.Binary;
using KlaraHome.IntegrationTests.Database;

namespace KlaraHome.IntegrationTests.Step8;

/// <summary>The host, the recorders, and the sign-in every Step 8 test needs.</summary>
/// <param name="fixture">The migrated database.</param>
public abstract class Step8TestBase(KlaraHomeSchemaFixture fixture) : IDisposable
{
    private Step8ApiFactory? _factory;
    private bool _disposed;

    /// <summary>The ambient cancellation token, so a hung test fails rather than hanging.</summary>
    protected static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The host under test, with its recorders.</summary>
    internal Step8ApiFactory Factory => _factory ??= new Step8ApiFactory(fixture.ConnectionString);

    /// <summary>Skips the test when Docker is unavailable, rather than failing on the environment.</summary>
    protected void SkipWithoutDocker()
        => Assert.SkipWhen(fixture.SkipReason is not null, fixture.SkipReason ?? string.Empty);

    /// <summary>A client against the host under test.</summary>
    protected HttpClient CreateClient() => Factory.CreateClient();

    /// <summary>A client signed in as the deployment's first administrator.</summary>
    protected async Task<HttpClient> SignedInAdministratorAsync()
    {
        var client = CreateClient();

        await TestSignIn.SignInAsync(
            client,
            "admin",
            KlaraHomeSchemaFixture.BootstrapEmail,
            KlaraHomeSchemaFixture.BootstrapPassword,
            Cancellation);

        return client;
    }

    /// <summary>An email address no other test is using.</summary>
    /// <param name="prefix">A readable hint about which test made it.</param>
    protected static string NewEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@klarahome.test";

    /// <summary>A PNG header carrying the given dimensions. Enough for the inspector to read.</summary>
    /// <param name="width">Pixel width.</param>
    /// <param name="height">Pixel height.</param>
    protected static byte[] TestPng(int width, int height)
    {
        var bytes = new byte[33];

        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);

        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);

        bytes[24] = 8;
        bytes[25] = 6;

        return bytes;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _factory?.Dispose();
        GC.SuppressFinalize(this);
    }
}
