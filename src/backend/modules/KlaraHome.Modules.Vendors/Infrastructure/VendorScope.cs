using System.Globalization;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Vendors.Infrastructure;

/// <summary>The refusals this module can produce, with the codes the frontend switches on.</summary>
internal static class VendorErrors
{
    /// <summary>The seller does not exist, or is outside the caller's scope.</summary>
    public static Error NotFound { get; } =
        Error.NotFound("VENDOR_NOT_FOUND", "That seller does not exist.");

    /// <summary>The commission plan does not exist.</summary>
    public static Error PlanNotFound { get; } =
        Error.NotFound("COMMISSION_PLAN_NOT_FOUND", "That commission plan does not exist.");

    /// <summary>A KYC document, bank account or pickup location does not exist for this seller.</summary>
    /// <param name="what">What was being looked for, in words a caller can read.</param>
    public static Error ChildNotFound(string what)
        => Error.NotFound("VENDOR_RECORD_NOT_FOUND", $"That {what} does not exist.");

    /// <summary>The seller's code or slug is already taken.</summary>
    /// <param name="field">Which one.</param>
    public static Error Duplicate(string field)
        => Error.Conflict("VENDOR_DUPLICATE", $"Another seller already uses that {field}.");

    /// <summary>The life cycle does not allow the requested move.</summary>
    /// <param name="from">Where the seller is.</param>
    /// <param name="to">Where the caller tried to take them.</param>
    public static Error InvalidTransition(VendorStatus from, VendorStatus to)
        => Error.Conflict(
            "VENDOR_INVALID_TRANSITION",
            $"A seller cannot go from {from} to {to}.");

    /// <summary>An onboarding requirement has not been met.</summary>
    /// <param name="because">Which requirement, in words shown to the operator.</param>
    public static Error NotReady(string because)
        => Error.Validation("VENDOR_NOT_READY", because);

    /// <summary>A vendor caller tried to act on a seller other than their own.</summary>
    public static Error OutOfScope { get; } =
        Error.Validation("VENDOR_SCOPE", "You can only do that within your own organisation.");

    /// <summary>A seller must keep one owner and one primary bank account.</summary>
    /// <param name="what">What cannot be removed.</param>
    public static Error LastOne(string what)
        => Error.Validation("VENDOR_LAST_RECORD", $"A seller must keep at least one {what}.");
}

/// <summary>
/// Finds a seller the caller is allowed to act on, and settles who that is.
/// </summary>
/// <remarks>
/// <para>
/// The child tables are <see cref="KlaraHome.SharedKernel.Domain.IVendorScoped"/> and are filtered
/// in the data layer, so a vendor user querying pickup locations simply does not see anybody
/// else's. <see cref="Vendor"/> itself cannot be: its scope column <em>is</em> its primary key, and
/// a filter on that would need a property the model does not have. So the check lives here, once,
/// rather than in each of the fourteen handlers that take a vendor id from a route.
/// </para>
/// <para>
/// A seller outside the caller's scope answers 404 rather than 403. An object-level check that says
/// "forbidden" confirms the id exists, which is the enumeration leak docs/07-security-compliance.md
/// §2 rules out — and the same reasoning the Identity module's user scope follows.
/// </para>
/// </remarks>
/// <param name="context">The Vendors data context.</param>
/// <param name="caller">The signed-in caller, for their vendor scope.</param>
/// <param name="options">Supplies the code prefix.</param>
internal sealed class VendorScope(
    VendorsDbContext context,
    ICallerContext caller,
    IOptions<VendorOptions> options)
{
    /// <summary>Whether the caller is confined to one seller.</summary>
    public bool IsVendorCaller => caller.VendorId is not null;

    /// <summary>The seller the caller is confined to, or null for platform staff.</summary>
    public Guid? CallerVendorId => caller.VendorId;

    /// <summary>
    /// Resolves the seller a request is about: the id in the route for platform staff, and the
    /// caller's own seller for a vendor user, whichever id they typed.
    /// </summary>
    /// <param name="requested">The id from the route, or null for "mine".</param>
    public Result<Guid> Resolve(Guid? requested)
    {
        if (caller.VendorId is { } scoped)
        {
            return requested is null || requested == scoped
                ? Result.Success(scoped)
                : Result.Failure<Guid>(VendorErrors.OutOfScope);
        }

        // Platform staff must name a seller. "Mine" is not a thing they have.
        return requested is { } id && id != Guid.Empty
            ? Result.Success(id)
            : Result.Failure<Guid>(VendorErrors.NotFound);
    }

    /// <summary>Finds a seller the caller may act on, tracked for modification, or null.</summary>
    /// <param name="vendorId">The seller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Vendor?> FindAsync(Guid vendorId, CancellationToken cancellationToken)
    {
        if (caller.VendorId is { } scoped && scoped != vendorId)
        {
            return null;
        }

        return await context.Vendors
            .FirstOrDefaultAsync(vendor => vendor.Id == vendorId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Takes the next vendor code from the sequence — <c>VND-000017</c>.
    /// </summary>
    /// <remarks>
    /// Read outside the entity because it is a database call, and formatted here because the prefix
    /// is configuration. Six digits is deliberate: it sorts correctly as text up to a million
    /// sellers, which is well past the point at which this is somebody else's problem.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<string> NextCodeAsync(CancellationToken cancellationToken)
    {
        var sequence = $"{VendorsModule.SchemaName}.{VendorsDbContext.CodeSequenceName}";

        // EF1002 warns that SqlQueryRaw concatenates rather than parameterises. It is right in
        // general and does not apply here: a sequence name cannot be a parameter in any dialect,
        // and this one is built from two compile-time constants in this assembly with nothing
        // caller-supplied anywhere near it. Suppressed narrowly, at the one call site.
#pragma warning disable EF1002
        var next = await context.Database
            .SqlQueryRaw<long>($"SELECT nextval('{sequence}') AS \"Value\"")
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);
#pragma warning restore EF1002

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{options.Value.CodePrefix}-{next:D6}");
    }
}
