using System.Text;
using KlaraHome.Modules.Payments.Domain;
using KlaraHome.Modules.Payments.Infrastructure;
using KlaraHome.Modules.Payments.Infrastructure.Gateway;
using KlaraHome.Modules.Payments.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Time;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Payments.Endpoints;

/// <summary>
/// The inbound webhook receiver (docs/04-api-specification.md §5, docs/08-integrations.md §1).
/// </summary>
/// <remarks>
/// <para>
/// This endpoint does five things and deliberately not a sixth. It reads the <b>raw body</b> before
/// any model binding, verifies the HMAC over exactly those bytes, refuses anything outside the skew
/// window, stores the event, and answers <c>200</c>. It does not confirm an order, capture stock or
/// raise an invoice — <see cref="Infrastructure.Jobs.GatewayEventWorker"/> does that afterwards,
/// which is what keeps a gateway from timing out and redelivering while a transaction runs.
/// </para>
/// <para>
/// The status codes are chosen for a machine that retries. A duplicate, a stale event and a type
/// nobody handles all answer <c>200</c>, because none of them is something the gateway can fix by
/// sending it again — and a <c>4xx</c> to a gateway is an invitation to a retry storm. The single
/// exception is a signature that does not verify, which answers <c>401</c>: that one is worth the
/// gateway knowing about, and it is stored all the same because somebody sending us forged webhooks
/// is worth a record.
/// </para>
/// <para>
/// It is anonymous, as every webhook must be, and the signature is the whole of its authentication.
/// The body has a hard size ceiling, because an unauthenticated endpoint that reads a body into
/// memory in order to hash it would otherwise be the denial of service.
/// </para>
/// </remarks>
internal static partial class WebhookEndpoints
{
    /// <summary>Maps the receiver beneath <c>/webhooks</c>.</summary>
    /// <param name="endpoints">The versioned API group.</param>
    public static IEndpointRouteBuilder MapPaymentWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints
            .MapGroup("/webhooks")
            .WithTags("Webhooks")
            .AllowAnonymous();

        group.MapPost("/razorpay", async (HttpContext context) =>
            {
                var services = context.RequestServices;

                return await ReceiveAsync(
                        context,
                        PaymentProviders.Razorpay,
                        services.GetRequiredService<PaymentProviderRegistry>(),
                        services.GetRequiredService<PaymentsDbContext>(),
                        services.GetRequiredService<IOptions<PaymentsOptions>>().Value,
                        services.GetRequiredService<IClock>(),
                        services.GetRequiredService<ILoggerFactory>().CreateLogger("KlaraHome.Payments.Webhook"))
                    .ConfigureAwait(false);
            })
            .WithName("razorpayWebhook")
            .WithSummary("Receives a signed Razorpay webhook, stores it, and answers immediately.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> ReceiveAsync(
        HttpContext context,
        string providerName,
        PaymentProviderRegistry providers,
        PaymentsDbContext data,
        PaymentsOptions options,
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

        var signature = context.Request.Headers["X-Razorpay-Signature"].ToString();
        var verified = provider.VerifyWebhookSignature(body, signature);
        var envelope = provider.ReadWebhook(body);

        if (envelope.IsFailure)
        {
            // Unreadable, and therefore unstorable — there is no event id to deduplicate on. It is
            // logged rather than kept, and answered 200 so the gateway stops sending it.
            UnreadableBody(logger, providerName, envelope.Error.Message);
            return Results.Ok();
        }

        var read = envelope.Value;
        var now = clock.UtcNow;

        // Replay protection, checked before anything is written. The unique index is the real
        // guarantee; this turns the second delivery into a 200 rather than a constraint violation.
        var known = await data.GatewayEvents
            .AnyAsync(entry => entry.Provider == providerName
                               && entry.ProviderEventId == read.ProviderEventId)
            .ConfigureAwait(false);

        if (known)
        {
            return Results.Ok();
        }

        var stored = GatewayEvent.Receive(
            providerName,
            read.ProviderEventId,
            read.EventType,
            verified,
            body,
            read.OccurredAt,
            now);

        // Outside the skew window: a signed body somebody recorded and is replaying hours later.
        // Stored as evidence, and marked so nothing will ever act on it.
        if (read.OccurredAt is { } occurred
            && occurred < now.AddMinutes(-options.WebhookSkewMinutes))
        {
            stored.MarkIgnored(now);
            StaleEvent(logger, providerName, read.EventType, occurred);
        }
        else if (!verified)
        {
            stored.MarkIgnored(now);
            BadSignature(logger, providerName, read.EventType);
        }

        data.GatewayEvents.Add(stored);
        await data.SaveChangesAsync().ConfigureAwait(false);

        return verified ? Results.Ok() : Results.Unauthorized();
    }

    /// <summary>
    /// Reads the body as it arrived, or null when it is over the ceiling.
    /// </summary>
    /// <remarks>
    /// The bytes are read once, exactly as sent. Re-serialising a parsed document would produce a
    /// different byte sequence and the HMAC would never verify — which is why every webhook contract
    /// in this platform says "read the raw body before any model binding".
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

    [LoggerMessage(EventId = 1620, Level = LogLevel.Warning,
        Message = "A {Provider} webhook could not be read and was not stored: {Detail}")]
    private static partial void UnreadableBody(ILogger logger, string provider, string detail);

    [LoggerMessage(EventId = 1621, Level = LogLevel.Warning,
        Message = "A {Provider} {EventType} webhook arrived from {OccurredAt}, outside the skew window. "
                  + "It was stored and ignored.")]
    private static partial void StaleEvent(
        ILogger logger,
        string provider,
        string eventType,
        DateTimeOffset occurredAt);

    [LoggerMessage(EventId = 1622, Level = LogLevel.Error,
        Message = "A {Provider} {EventType} webhook failed signature verification. It was stored as "
                  + "evidence and will never be processed.")]
    private static partial void BadSignature(ILogger logger, string provider, string eventType);
}
