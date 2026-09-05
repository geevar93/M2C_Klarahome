using KlaraHome.Contracts.Notifications;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Notifications.Application;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Notifications.Endpoints;

/// <summary>
/// What a signed-in customer may change about what they receive
/// (docs/04-api-specification.md §3.1).
/// </summary>
internal static class StoreNotificationEndpoints
{
    /// <summary>Maps the storefront surface beneath the versioned API group.</summary>
    /// <param name="endpoints">The versioned API group the module is handed.</param>
    public static IEndpointRouteBuilder MapStoreNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/store/me/notification-preferences")
            .WithTags("Notifications")
            .RequireAuthorization();

        group.MapGet("/", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetPreferencesQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("storeNotificationPreferencesGet")
            .WithSummary("Returns what this account has chosen to receive, per category and channel.")
            .Produces<PreferencesResponse>();

        group.MapPut("/", async (
                UpdatePreferenceRequest request,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdatePreferenceCommand(
                    request.Category,
                    request.Email,
                    request.Sms,
                    request.WhatsApp,
                    request.InApp);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("storeNotificationPreferencesPut")
            .WithSummary("Changes one category's choices and returns the whole set.")
            .RequireRateLimiting(RateLimitPolicies.CartWrite)
            .Produces<PreferencesResponse>();

        return endpoints;
    }
}

/// <summary>The body of a preference change.</summary>
/// <param name="Category">Which bucket. Security cannot be changed.</param>
/// <param name="Email">Whether email is wanted.</param>
/// <param name="Sms">Whether SMS is wanted.</param>
/// <param name="WhatsApp">Whether WhatsApp is wanted.</param>
/// <param name="InApp">Whether in-application messages are wanted.</param>
internal sealed record UpdatePreferenceRequest(
    string Category,
    bool Email,
    bool Sms,
    bool WhatsApp,
    bool InApp);

/// <summary>
/// Notification administration: the wording, the delivery log, and a way to prove a channel works
/// (docs/04-api-specification.md §4).
/// </summary>
internal static class AdminNotificationEndpoints
{
    /// <summary>Permission required to read or rewrite a template.</summary>
    public const string TemplateManagePermission = "notifications.template.manage";

    /// <summary>Permission required to read the delivery log and re-queue a message.</summary>
    public const string LogReadPermission = "notifications.log.read";

    /// <summary>Maps the admin surface beneath the versioned API group.</summary>
    /// <param name="endpoints">The versioned API group the module is handed.</param>
    public static IEndpointRouteBuilder MapAdminNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/admin").WithTags("Notifications");

        MapTemplates(admin);
        MapDeliveryLog(admin);

        return endpoints;
    }

    private static void MapTemplates(IEndpointRouteBuilder admin)
    {
        admin.MapGet("/notification-templates", async (
                string? channel,
                string? eventKey,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new ListTemplatesQuery(channel, eventKey), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminNotificationTemplatesList")
            .WithSummary("Lists every notification template, with the placeholders each expects.")
            .RequirePermission(TemplateManagePermission)
            .Produces<IReadOnlyList<TemplateResponse>>();

        admin.MapGet("/notification-templates/{id:guid}", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetTemplateQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminNotificationTemplateGet")
            .WithSummary("Returns one template.")
            .RequirePermission(TemplateManagePermission)
            .Produces<TemplateResponse>();

        admin.MapPut("/notification-templates/{id:guid}", async (
                Guid id,
                UpdateTemplateRequest request,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateTemplateCommand(
                    id,
                    request.Subject,
                    request.Body,
                    request.ProviderTemplateId,
                    request.IsActive);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminNotificationTemplatePut")
            .WithSummary("Rewrites one template. An active SMS template must carry its DLT id.")
            .RequirePermission(TemplateManagePermission)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<TemplateResponse>();
    }

    private static void MapDeliveryLog(IEndpointRouteBuilder admin)
    {
        admin.MapGet("/notifications", async (
                [AsParameters] NotificationLogFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new SearchNotificationsQuery(
                    filter.Status,
                    filter.Channel,
                    filter.EventKey,
                    filter.From,
                    filter.To,
                    filter.Cursor,
                    filter.Size);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminNotificationsList")
            .WithSummary("Searches the delivery log, newest first. Recipients are masked.")
            .RequirePermission(LogReadPermission)
            .Produces<PagedResult<NotificationLogResponse>>();

        admin.MapGet("/notifications/{id:guid}", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetNotificationQuery(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminNotificationGet")
            .WithSummary("Returns one delivery-log entry.")
            .RequirePermission(LogReadPermission)
            .Produces<NotificationLogResponse>();

        admin.MapPost("/notifications/{id:guid}/retry", async (
                Guid id,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new RetryNotificationCommand(id), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminNotificationRetry")
            .WithSummary("Puts a failed or suppressed message back in the queue.")
            .RequirePermission(LogReadPermission)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<NotificationLogResponse>();

        admin.MapPost("/notifications/test", async (
                SendTestRequest request,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new SendTestNotificationCommand(request.Channel, request.To), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminNotificationTest")
            .WithSummary("Sends the built-in test message, to prove a channel works end to end.")
            .RequirePermission(TemplateManagePermission)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<IReadOnlyList<QueuedNotification>>();
    }
}

/// <summary>The body of a template rewrite.</summary>
/// <param name="Subject">The new subject.</param>
/// <param name="Body">The new body.</param>
/// <param name="ProviderTemplateId">The registered DLT id, for SMS.</param>
/// <param name="IsActive">Whether it stays in use.</param>
internal sealed record UpdateTemplateRequest(
    string Subject,
    string Body,
    string? ProviderTemplateId,
    bool IsActive);

/// <summary>The body of a test send.</summary>
/// <param name="Channel">Which channel to test.</param>
/// <param name="To">Where to send it.</param>
internal sealed record SendTestRequest(string Channel, string To);

/// <summary>Query-string filter for the delivery log.</summary>
/// <param name="Status">Restrict to one status.</param>
/// <param name="Channel">Restrict to one channel.</param>
/// <param name="EventKey">Restrict to one event.</param>
/// <param name="From">Earliest queued time.</param>
/// <param name="To">Latest queued time.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record NotificationLogFilter(
    string? Status,
    string? Channel,
    string? EventKey,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size);
