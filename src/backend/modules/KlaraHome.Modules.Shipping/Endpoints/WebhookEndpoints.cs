using System.Text;
using KlaraHome.Modules.Shipping.Domain;
using KlaraHome.Modules.Shipping.Infrastructure;
using KlaraHome.Modules.Shipping.Infrastructure.Courier;
using KlaraHome.Modules.Shipping.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Shipping.Endpoints;

/// <summary>
/// The inbound courier webhook receiver (docs/04-api-specification.md §5,
/// docs/08-integrations.md §2).
/// </summary>
/// <remarks>
/// <para>
/// The same five things the payments receiver does and deliberately not a sixth. It reads the
/// <b>raw body</b> before any model binding, verifies the signature over exactly those bytes,
/// refuses anything outside the skew window, stores the event, and answers <c>200</c>. It does not
/// move a parcel, an order or any money — <see cref="Infrastructure.Jobs.CourierEventWorker"/> does
/// that afterwards, which is what keeps an aggregator from timing out and redelivering while a
/// transaction runs.
/// </para>
/// <para>
/// The status codes are chosen for a machine that retries. A duplicate, a stale event and an
/// unreadable body all answer <c>200</c>, because none of them is something the courier can fix by
/// sending it again, and a <c>4xx</c> to an aggregator is an invitation to a retry storm. The
/// exception is a signature that does not verify, which answers <c>401</c> and is stored all the
/// same: somebody sending forged tracking updates is worth a record.
/// </para>
/// <para>
/// The skew window is deliberately wider here than for payments — an hour rather than five minutes.
/// Courier webhooks routinely arrive late from a backed-up queue, and a scan from forty minutes ago
/// is ordinary rather than suspicious. It still bounds a replay of a captured signed body, which is
/// what the window is for.
/// </para>
/// <para>
/// It is anonymous, as every webhook must be, and the signature is the whole of its authentication.
/// The body has a hard size ceiling, because an unauthenticated endpoint that reads a body into
/// memory in order to hash it would otherwise be the denial of service.
/// </para>
/// </remarks>
internal static partial class WebhookEndpoints
{
    /// <summary>Maps the receiver beneath <c>/webhooks/shipping</c>.</summary>
    /// <param name="endpoints">The versioned API group.</param>
    public static IEndpointRouteBuilder MapShippingWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup("/webhooks/shipping")
            .WithTags("Webhooks")
            .AllowAnonymous();

        // Named by provider, exactly as docs/04-api-specification.md §5 specifies. A deployment that
        // changes aggregator points the new one at its own path, and events already stored under the
        // old name stay attributable to the adapter that can read them.
        group.MapPost("/{provider}", async (string provider, HttpContext context) =>
            {
                var services = context.RequestServices;

                return await ReceiveAsync(
                        context,
                        provider,
                        services.GetRequiredService<ShippingProviderRegistry>(),
                        services.GetRequiredService<ShippingDbContext>(),
                        services.GetRequiredService<IOptions<ShippingOptions>>().Value,
                        services.GetRequiredService<IClock>(),
                        services.GetRequiredService<ILoggerFactory>().CreateLogger("KlaraHome.Shipping.Webhook"))
                    .ConfigureAwait(false);
            })
            .WithName("shippingWebhook")
            .WithSummary("Receives a signed courier webhook, stores it, and answers immediately.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> ReceiveAsync(
        HttpContext context,
        string providerName,
        ShippingProviderRegistry providers,
        ShippingDbContext data,
        ShippingOptions options,
        IClock clock,
        ILogger logger)
    {
        var provider = providers.Find(providerName);

        if (provider is null)
        {
            // No adapter for this provider in this build. Answering 503 rather than 404 tells an
            // operator the endpoint exists and the deployment is incomplete.
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var body = await ReadRawBodyAsync(context, options.MaxWebhookBytes).ConfigureAwait(false);

        if (body is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // Whichever header the aggregator signs with. Both spellings are read because they are the
        // two this class of API uses, and an operator should not have to guess which.
        var signature = context.Request.Headers["X-Shipping-Signature"].ToString();

        if (string.IsNullOrWhiteSpace(signature))
        {
            signature = context.Request.Headers["X-Webhook-Signature"].ToString();
        }

        var verified = provider.VerifyWebhookSignature(body, signature);
        var envelope = provider.ReadWebhook(body);

        if (envelope.IsFailure)
        {
            // Unreadable, and therefore unstorable — there is no event id to deduplicate on. Logged
            // rather than kept, and answered 200 so the courier stops sending it.
            UnreadableBody(logger, providerName, envelope.Error.Message);
            return Results.Ok();
        }

        var read = envelope.Value;
        var now = clock.UtcNow;

        // Replay protection, checked before anything is written. The unique index is the real
        // guarantee; this turns the second delivery into a 200 rather than a constraint violation.
        var known = await data.CourierEvents
            .AnyAsync(entry => entry.Provider == providerName
                               && entry.ProviderEventId == read.ProviderEventId)
            .ConfigureAwait(false);

        if (known)
        {
            return Results.Ok();
        }

        var stored = CourierEvent.Receive(
            providerName,
            read.ProviderEventId,
            read.EventType,
            verified,
            body,
            read.Awb,
            read.OccurredAt,
            now);

        // Outside the skew window: a signed body somebody recorded and is replaying later. Stored as
        // evidence, and marked so nothing will ever act on it.
        if (read.OccurredAt is { } occurred
            && occurred < now.AddMinutes(-options.WebhookSkewMinutes))
        {
            stored.MarkIgnored(now);
            StaleEvent(logger, providerName, read.EventType, occurred);
        }
        else if (!verified)
        {
            stored.MarkIgnored(now);
            BadSignature(logger, providerName, read.Awb ?? "<none>");
        }

        data.CourierEvents.Add(stored);
        await data.SaveChangesAsync().ConfigureAwait(false);

        return verified ? Results.Ok() : Results.Unauthorized();
    }

    /// <summary>
    /// Reads the body as it arrived, or null when it is over the ceiling.
    /// </summary>
    /// <remarks>
    /// The bytes are read once, exactly as sent. Re-serialising a parsed document would produce a
    /// different byte sequence and the signature would never verify — which is why every webhook
    /// contract in this platform says "read the raw body before any model binding".
    /// </remarks>
    private static async Task<string?> ReadRawBodyAsync(HttpContext context, int maxBytes)
    {
        if (context.Request.ContentLength is { } declared && declared > maxBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;

        while ((read = await context.Request.Body
                   .ReadAsync(chunk.AsMemory(0, chunk.Length), context.RequestAborted)
                   .ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), context.RequestAborted).ConfigureAwait(false);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    [LoggerMessage(EventId = 1810, Level = LogLevel.Warning,
        Message = "A {Provider} courier webhook could not be read and was not stored: {Detail}")]
    private static partial void UnreadableBody(ILogger logger, string provider, string detail);

    [LoggerMessage(EventId = 1811, Level = LogLevel.Warning,
        Message = "A {Provider} {EventType} webhook arrived from {OccurredAt}, outside the skew window. "
                  + "It was stored and ignored.")]
    private static partial void StaleEvent(
        ILogger logger,
        string provider,
        string eventType,
        DateTimeOffset occurredAt);

    [LoggerMessage(EventId = 1812, Level = LogLevel.Error,
        Message = "A {Provider} webhook for parcel {Awb} failed signature verification. It was stored as "
                  + "evidence and will never be processed.")]
    private static partial void BadSignature(ILogger logger, string provider, string awb);
}
