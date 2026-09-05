using System.Text.Json;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Platform.Application.Settings;
using KlaraHome.Modules.Platform.Domain;
using KlaraHome.Modules.Platform.Infrastructure.FeatureFlags;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using KlaraHome.Modules.Platform.Infrastructure.Settings;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Platform.Application.Storefront;

/// <summary>
/// Everything the storefront needs before it can render its first page: who the store is, what it
/// is called, what it charges for, and which features are on.
/// </summary>
/// <param name="TenantCode">The deployment's tenant code.</param>
/// <param name="Settings">The public settings sections, keyed by section.</param>
/// <param name="Features">Every feature flag and whether it is on for this caller.</param>
internal sealed record StoreConfigResponse(
    string TenantCode,
    IReadOnlyDictionary<string, JsonElement> Settings,
    IReadOnlyDictionary<string, bool> Features);

/// <summary>Reads the anonymous store configuration document.</summary>
internal sealed record GetStoreConfigQuery : IQuery<StoreConfigResponse>;

/// <summary>
/// Assembles the public configuration document.
/// </summary>
/// <remarks>
/// Only sections that declare themselves public are included, and the check is on the section type
/// rather than on the row's <c>is_public</c> column. Both say the same thing, but the column is
/// data and the declaration is code — and this endpoint is anonymous, so the answer to "may a
/// stranger read this" should not be something a bad row could change.
/// </remarks>
/// <param name="settings">The settings service.</param>
/// <param name="flags">The flag service.</param>
/// <param name="tenant">The ambient tenant.</param>
internal sealed class GetStoreConfigQueryHandler(
    StoreSettingsService settings,
    IFeatureFlags flags,
    ITenantContext tenant) : IQueryHandler<GetStoreConfigQuery, StoreConfigResponse>
{
    public async Task<Result<StoreConfigResponse>> HandleAsync(
        GetStoreConfigQuery query,
        CancellationToken cancellationToken)
    {
        var sections = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var descriptor in SettingsCatalog.Sections.Where(section => section.IsPublic))
        {
            var document = await settings.GetDocumentAsync(descriptor, cancellationToken).ConfigureAwait(false);
            sections[descriptor.Key] = JsonElements.Parse(document);
        }

        var features = await flags.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return new StoreConfigResponse(tenant.Code, sections, features);
    }
}

/// <summary>A state or union territory, as the storefront address form needs it.</summary>
/// <param name="Code">The two-digit GST state code.</param>
/// <param name="Name">Official name.</param>
/// <param name="Kind">Whether it is a state or a union territory.</param>
internal sealed record StateResponse(string Code, string Name, StateKind Kind);

/// <summary>Lists the Indian states and union territories.</summary>
internal sealed record GetStatesQuery : IQuery<IReadOnlyList<StateResponse>>;

/// <param name="context">The Platform module's context.</param>
internal sealed class GetStatesQueryHandler(PlatformDbContext context)
    : IQueryHandler<GetStatesQuery, IReadOnlyList<StateResponse>>
{
    public async Task<Result<IReadOnlyList<StateResponse>>> HandleAsync(
        GetStatesQuery query,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<StateResponse> states = await context.States
            .AsNoTracking()
            .OrderBy(state => state.Name)
            .Select(state => new StateResponse(state.Code, state.Name, state.Kind))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(states);
    }
}

/// <summary>A PIN code and the place it identifies.</summary>
/// <param name="Pincode">The six-digit code.</param>
/// <param name="City">City or town.</param>
/// <param name="District">Revenue district.</param>
/// <param name="StateCode">GST state code of the owning state.</param>
/// <param name="StateName">Name of the owning state.</param>
/// <param name="Zone">Logistics zone.</param>
internal sealed record PincodeResponse(
    string Pincode,
    string City,
    string District,
    string StateCode,
    string StateName,
    string Zone);

/// <summary>Looks up one PIN code, for address autofill.</summary>
/// <param name="Pincode">The six-digit code.</param>
internal sealed record GetPincodeQuery(string Pincode) : IQuery<PincodeResponse>;

/// <param name="context">The Platform module's context.</param>
internal sealed class GetPincodeQueryHandler(PlatformDbContext context)
    : IQueryHandler<GetPincodeQuery, PincodeResponse>
{
    public async Task<Result<PincodeResponse>> HandleAsync(
        GetPincodeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The join is written as a query over two DbSets in the same schema rather than a
        // navigation, because the entity deliberately holds a state id and no reference: reference
        // rows are looked up, not walked.
        var match = await (
                from pincode in context.Pincodes.AsNoTracking()
                join state in context.States.AsNoTracking() on pincode.StateId equals state.Id
                where pincode.Code == query.Pincode
                select new PincodeResponse(
                    pincode.Code,
                    pincode.City,
                    pincode.District,
                    state.Code,
                    state.Name,
                    pincode.Zone))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return match is null
            ? Error.NotFound("PINCODE_NOT_FOUND", $"No PIN code {query.Pincode} is on file.")
            : match;
    }
}
