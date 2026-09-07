using System.Text.Json;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.RateLimiting;
using KlaraHome.Modules.Platform.Application.Auditing;
using KlaraHome.Modules.Platform.Application.FeatureFlags;
using KlaraHome.Modules.Platform.Application.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace KlaraHome.Modules.Platform.Endpoints;

/// <summary>
/// The platform's administrative surface: settings, feature flags and the audit trail
/// (docs/04-api-specification.md §4).
/// </summary>
/// <remarks>
/// Every endpoint here declares the permission it will require. Nothing enforces those declarations
/// yet — the Identity module registers the authentication scheme and the policies at Step 7 — so
/// <c>UnsecuredEndpointGuard</c> refuses to start this host outside Development while that is true.
/// </remarks>
internal static class AdminPlatformEndpoints
{
    /// <summary>Permission required to read or change store settings and feature flags.</summary>
    public const string SettingsManagePermission = "platform.settings.manage";

    /// <summary>Permission required to read the audit trail.</summary>
    public const string AuditReadPermission = "platform.audit.read";

    /// <summary>Maps the admin surface beneath the versioned API group.</summary>
    /// <param name="endpoints">The versioned API group the module is handed.</param>
    public static IEndpointRouteBuilder MapAdminPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/admin")
            .WithTags("Platform");

        MapSettings(group);
        MapFeatureFlags(group);
        MapAuditLogs(group);

        return endpoints;
    }

    private static void MapSettings(IEndpointRouteBuilder admin)
    {
        admin.MapGet("/settings", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetStoreSettingsQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSettingsGet")
            .WithSummary("Returns every store settings section with its current value.")
            .RequirePermission(SettingsManagePermission)
            .Produces<StoreSettingsResponse>();

        admin.MapGet("/settings/schema", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetSettingsSchemaQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSettingsSchemaGet")
            .WithSummary("The shape of every settings section and the rules its values must satisfy, so the "
                         + "settings form can draw the right control and enforce the same bounds the server "
                         + "does. Advisory: the server validates every write regardless.")
            .RequirePermission(SettingsManagePermission)
            .Produces<SettingsSchemaResponse>();

        admin.MapPut("/settings/{key}", async (
                string key,
                JsonElement value,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var result = await dispatcher
                    .SendAsync(new UpdateStoreSettingCommand(key, value), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminSettingsPut")
            .WithSummary("Replaces one settings section. The section is edited as a whole.")
            .RequirePermission(SettingsManagePermission)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<SettingsSectionResponse>();
    }

    private static void MapFeatureFlags(IEndpointRouteBuilder admin)
    {
        admin.MapGet("/feature-flags", async (IDispatcher dispatcher, HttpContext context) =>
            {
                var result = await dispatcher
                    .QueryAsync(new GetFeatureFlagsQuery(), context.RequestAborted)
                    .ConfigureAwait(false);

                return result.ToOk(context);
            })
            .WithName("adminFeatureFlagsGet")
            .WithSummary("Lists every declared feature flag and its current configuration.")
            .RequirePermission(SettingsManagePermission)
            .Produces<IReadOnlyList<FeatureFlagResponse>>();

        admin.MapPut("/feature-flags/{key}", async (
                string key,
                UpdateFeatureFlagRequest request,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var command = new UpdateFeatureFlagCommand(
                    key,
                    request.Enabled,
                    request.Rollout,
                    request.Description);

                var result = await dispatcher.SendAsync(command, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminFeatureFlagsPut")
            .WithSummary("Turns one feature flag on or off and sets who it reaches.")
            .RequirePermission(SettingsManagePermission)
            .RequireRateLimiting(RateLimitPolicies.AdminWrite)
            .Produces<FeatureFlagResponse>();
    }

    private static void MapAuditLogs(IEndpointRouteBuilder admin)
    {
        admin.MapGet("/audit-logs", async (
                [AsParameters] AuditLogFilter filter,
                IDispatcher dispatcher,
                HttpContext context) =>
            {
                var query = new SearchAuditLogsQuery(
                    filter.EntityType,
                    filter.EntityId,
                    filter.ActorId,
                    filter.Action,
                    filter.From,
                    filter.To,
                    filter.Cursor,
                    filter.Size);

                var result = await dispatcher.QueryAsync(query, context.RequestAborted).ConfigureAwait(false);
                return result.ToOk(context);
            })
            .WithName("adminAuditLogsGet")
            .WithSummary("Searches the immutable audit trail, newest first.")
            .RequirePermission(AuditReadPermission)
            .Produces<PagedResult<AuditLogResponse>>();
    }
}

/// <summary>The body of a feature-flag change. The key comes from the path.</summary>
/// <param name="Enabled">The new master switch value.</param>
/// <param name="Rollout">The new rollout. Omit for "everyone".</param>
/// <param name="Description">A new description, or omit to keep the declared one.</param>
internal sealed record UpdateFeatureFlagRequest(bool Enabled, RolloutModel? Rollout, string? Description);

/// <summary>Query-string filters for an audit search.</summary>
/// <param name="EntityType">Restrict to one kind of thing.</param>
/// <param name="EntityId">Restrict to one thing. Only meaningful with an entity type.</param>
/// <param name="ActorId">Restrict to one actor.</param>
/// <param name="Action">Restrict to one action.</param>
/// <param name="From">Earliest instant to include.</param>
/// <param name="To">Latest instant to include.</param>
/// <param name="Cursor">Opaque token from the previous page.</param>
/// <param name="Size">How many entries to return.</param>
internal sealed record AuditLogFilter(
    string? EntityType,
    string? EntityId,
    Guid? ActorId,
    string? Action,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int? Size);
