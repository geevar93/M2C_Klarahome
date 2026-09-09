using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Inventory.Application.Warehouses;
using KlaraHome.Modules.Inventory.Domain;
using KlaraHome.Modules.Inventory.Infrastructure;
using KlaraHome.Modules.Inventory.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Inventory.Application.Purchasing;

/// <summary>A supplier, as the API states it.</summary>
/// <param name="Id">The supplier.</param>
/// <param name="VendorId">Whose list they are on, or null for the platform's.</param>
/// <param name="Code">The short code a buyer quotes.</param>
/// <param name="Name">Their trading name.</param>
/// <param name="ContactName">Who to speak to.</param>
/// <param name="Email">Where the purchase order is emailed.</param>
/// <param name="Phone">The number to ring.</param>
/// <param name="Gstin">Their GST registration.</param>
/// <param name="Address">Where they are.</param>
/// <param name="PaymentTermsDays">How many days their invoice falls due in.</param>
/// <param name="IsActive">Whether orders may still be raised on them.</param>
/// <param name="CreatedAt">When they were added.</param>
internal sealed record SupplierResponse(
    Guid Id,
    Guid? VendorId,
    string Code,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Gstin,
    AddressPayload? Address,
    int PaymentTermsDays,
    bool IsActive,
    DateTimeOffset CreatedAt);

/// <summary>Lists suppliers.</summary>
/// <param name="ActiveOnly">Hide the ones no longer traded with.</param>
/// <param name="Search">A fragment of the code or the name.</param>
/// <param name="Cursor">Opaque page token.</param>
/// <param name="Size">Page size.</param>
internal sealed record ListSuppliersQuery(bool? ActiveOnly, string? Search, string? Cursor, int? Size)
    : IQuery<PagedResult<SupplierResponse>>;

/// <summary>Reads one supplier.</summary>
/// <param name="SupplierId">The supplier.</param>
internal sealed record GetSupplierQuery(Guid SupplierId) : IQuery<SupplierResponse>;

/// <summary>Adds a supplier.</summary>
/// <param name="VendorId">Whose list. Ignored for a vendor caller, who adds to their own.</param>
/// <param name="Code">The short code a buyer quotes.</param>
/// <param name="Name">Their trading name.</param>
/// <param name="ContactName">Who to speak to.</param>
/// <param name="Email">Where the purchase order is emailed.</param>
/// <param name="Phone">The number to ring.</param>
/// <param name="Gstin">Their GST registration.</param>
/// <param name="Address">Where they are.</param>
/// <param name="PaymentTermsDays">How many days their invoice falls due in.</param>
internal sealed record CreateSupplierCommand(
    Guid? VendorId,
    string Code,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Gstin,
    AddressPayload? Address,
    int PaymentTermsDays) : ICommand<SupplierResponse>;

/// <summary>Restates a supplier.</summary>
/// <param name="SupplierId">The supplier.</param>
/// <param name="Name">Their trading name.</param>
/// <param name="ContactName">Who to speak to.</param>
/// <param name="Email">Where the purchase order is emailed.</param>
/// <param name="Phone">The number to ring.</param>
/// <param name="Gstin">Their GST registration.</param>
/// <param name="Address">Where they are.</param>
/// <param name="PaymentTermsDays">How many days their invoice falls due in.</param>
/// <param name="IsActive">Whether orders may still be raised on them.</param>
internal sealed record UpdateSupplierCommand(
    Guid SupplierId,
    string Name,
    string? ContactName,
    string? Email,
    string? Phone,
    string? Gstin,
    AddressPayload? Address,
    int PaymentTermsDays,
    bool IsActive) : ICommand<SupplierResponse>;

/// <summary>Rejects a supplier that could never be stored.</summary>
internal sealed class CreateSupplierValidator : AbstractValidator<CreateSupplierCommand>
{
    public CreateSupplierValidator()
    {
        RuleFor(command => command.Code).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9][A-Za-z0-9-]*$");
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.ContactName).MaximumLength(160);
        RuleFor(command => command.Email).EmailAddress().MaximumLength(320)
            .When(command => !string.IsNullOrWhiteSpace(command.Email));
        RuleFor(command => command.Phone).MaximumLength(20);
        RuleFor(command => command.Gstin).Matches(SupplierFormats.Gstin)
            .When(command => !string.IsNullOrWhiteSpace(command.Gstin));
        RuleFor(command => command.PaymentTermsDays).InclusiveBetween(0, Supplier.MaxPaymentTermsDays);
    }
}

/// <summary>The same rules, for a change.</summary>
internal sealed class UpdateSupplierValidator : AbstractValidator<UpdateSupplierCommand>
{
    public UpdateSupplierValidator()
    {
        RuleFor(command => command.SupplierId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.ContactName).MaximumLength(160);
        RuleFor(command => command.Email).EmailAddress().MaximumLength(320)
            .When(command => !string.IsNullOrWhiteSpace(command.Email));
        RuleFor(command => command.Phone).MaximumLength(20);
        RuleFor(command => command.Gstin).Matches(SupplierFormats.Gstin)
            .When(command => !string.IsNullOrWhiteSpace(command.Gstin));
        RuleFor(command => command.PaymentTermsDays).InclusiveBetween(0, Supplier.MaxPaymentTermsDays);
    }
}

/// <summary>The Indian identifier formats this module validates.</summary>
internal static class SupplierFormats
{
    /// <summary>
    /// A GSTIN: two state digits, a ten-character PAN, an entity digit, a fixed <c>Z</c>, and a
    /// checksum character.
    /// </summary>
    public const string Gstin = "^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$";
}

/// <summary>Lists suppliers.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class ListSuppliersQueryHandler(InventoryDbContext context)
    : IQueryHandler<ListSuppliersQuery, PagedResult<SupplierResponse>>
{
    public async Task<Result<PagedResult<SupplierResponse>>> HandleAsync(
        ListSuppliersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Cursor.NormalizeSize(query.Size);
        var rows = context.Suppliers.AsNoTracking();

        if (query.ActiveOnly == true)
        {
            rows = rows.Where(supplier => supplier.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{InventoryQueries.EscapeLike(query.Search)}%";

            rows = rows.Where(supplier =>
                EF.Functions.ILike(supplier.Code, pattern, "\\")
                || EF.Functions.ILike(supplier.Name, pattern, "\\"));
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(supplier => supplier.Id.CompareTo(after) < 0);
        }

        var page = await rows
            .OrderByDescending(supplier => supplier.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;

        if (hasMore)
        {
            page.RemoveAt(page.Count - 1);
        }

        return Result.Success(new PagedResult<SupplierResponse>(
            [.. page.Select(SupplierProjection.ToResponse)],
            new PageInfo(size, hasMore ? Cursor.Encode(page[^1].Id.ToString()) : null)));
    }
}

/// <summary>Reads one supplier.</summary>
/// <param name="context">The Inventory data context.</param>
internal sealed class GetSupplierQueryHandler(InventoryDbContext context)
    : IQueryHandler<GetSupplierQuery, SupplierResponse>
{
    public async Task<Result<SupplierResponse>> HandleAsync(
        GetSupplierQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var supplier = await context.Suppliers
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.SupplierId, cancellationToken)
            .ConfigureAwait(false);

        return supplier is null
            ? InventoryErrors.NotFound("supplier")
            : Result.Success(SupplierProjection.ToResponse(supplier));
    }
}

/// <summary>Adds a supplier.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Decides whose list it goes on.</param>
/// <param name="audit">Records the addition.</param>
internal sealed class CreateSupplierCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    IAuditLogger audit) : ICommandHandler<CreateSupplierCommand, SupplierResponse>
{
    /// <summary>The audited action for a new supplier.</summary>
    public const string AuditAction = "inventory.supplier.created";

    /// <summary>The entity type recorded against every supplier action.</summary>
    public const string AuditEntityType = "Supplier";

    public async Task<Result<SupplierResponse>> HandleAsync(
        CreateSupplierCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = command.Code.Trim().ToUpperInvariant();

        if (await context.Suppliers
                .AnyAsync(candidate => candidate.Code == code, cancellationToken)
                .ConfigureAwait(false))
        {
            return InventoryErrors.Duplicate("supplier code");
        }

        var supplier = Supplier.Add(scope.OwnerFor(command.VendorId), code, command.Name);

        supplier.Update(
            command.Name,
            command.ContactName,
            command.Email,
            command.Phone,
            command.Gstin,
            command.Address is null ? null : WarehouseProjection.ToAddress(command.Address),
            command.PaymentTermsDays);

        context.Suppliers.Add(supplier);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = supplier.Id.ToString(),
                After = new { supplier.Code, supplier.Name, supplier.Gstin },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(SupplierProjection.ToResponse(supplier));
    }
}

/// <summary>Restates a supplier.</summary>
/// <param name="context">The Inventory data context.</param>
/// <param name="scope">Refuses a seller editing somebody else's supplier.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdateSupplierCommandHandler(
    InventoryDbContext context,
    InventoryScope scope,
    IAuditLogger audit) : ICommandHandler<UpdateSupplierCommand, SupplierResponse>
{
    /// <summary>The audited action for a change to a supplier.</summary>
    public const string AuditAction = "inventory.supplier.updated";

    public async Task<Result<SupplierResponse>> HandleAsync(
        UpdateSupplierCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var supplier = await context.Suppliers
            .FirstOrDefaultAsync(candidate => candidate.Id == command.SupplierId, cancellationToken)
            .ConfigureAwait(false);

        if (supplier is null)
        {
            return InventoryErrors.NotFound("supplier");
        }

        if (!scope.CanWrite(supplier.VendorId))
        {
            return InventoryErrors.PlatformOnly;
        }

        var before = new { supplier.Name, supplier.Gstin, supplier.PaymentTermsDays, supplier.IsActive };

        supplier.Update(
            command.Name,
            command.ContactName,
            command.Email,
            command.Phone,
            command.Gstin,
            command.Address is null ? null : WarehouseProjection.ToAddress(command.Address),
            command.PaymentTermsDays);

        supplier.SetActive(command.IsActive);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateSupplierCommandHandler.AuditEntityType,
                EntityId = supplier.Id.ToString(),
                Before = before,
                After = new { supplier.Name, supplier.Gstin, supplier.PaymentTermsDays, supplier.IsActive },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(SupplierProjection.ToResponse(supplier));
    }
}

/// <summary>Turns suppliers into responses.</summary>
internal static class SupplierProjection
{
    /// <summary>States a supplier.</summary>
    /// <param name="supplier">The supplier.</param>
    public static SupplierResponse ToResponse(Supplier supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);

        return new SupplierResponse(
            supplier.Id,
            supplier.VendorId,
            supplier.Code,
            supplier.Name,
            supplier.ContactName,
            supplier.Email,
            supplier.Phone,
            supplier.Gstin,
            WarehouseProjection.ToPayload(supplier.Address),
            supplier.PaymentTermsDays,
            supplier.IsActive,
            supplier.CreatedAt);
    }
}
