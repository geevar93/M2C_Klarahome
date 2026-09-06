using FluentValidation;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Catalog.Application.Validation;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Application.Taxonomy;

/// <summary>One permitted value of a list attribute, as the API states it.</summary>
/// <param name="Id">The option.</param>
/// <param name="Value">The machine value a facet filters on.</param>
/// <param name="Label">The label a shopper sees.</param>
/// <param name="SwatchHex">A hex colour, for a swatch.</param>
/// <param name="Position">Sort order.</param>
internal sealed record AttributeOptionResponse(Guid Id, string Value, string Label, string? SwatchHex, int Position);

/// <summary>An attribute, with its options.</summary>
/// <param name="Id">The attribute.</param>
/// <param name="Code">Its stable code — the key a facet query uses.</param>
/// <param name="Name">The shopper-facing label.</param>
/// <param name="DataType">What kind of value it holds.</param>
/// <param name="Unit">The unit its numbers are in.</param>
/// <param name="IsVariantDefining">Whether it is a variant axis.</param>
/// <param name="IsFilterable">Whether it is offered as a filter.</param>
/// <param name="IsSearchable">Whether it feeds the search text.</param>
/// <param name="IsRequired">Whether a product must supply it before publishing.</param>
/// <param name="Position">Sort order.</param>
/// <param name="Options">Its permitted values, for a list attribute.</param>
internal sealed record AttributeResponse(
    Guid Id,
    string Code,
    string Name,
    AttributeDataType DataType,
    string? Unit,
    bool IsVariantDefining,
    bool IsFilterable,
    bool IsSearchable,
    bool IsRequired,
    int Position,
    IReadOnlyList<AttributeOptionResponse> Options);

/// <summary>An attribute set and what is in it.</summary>
/// <param name="Id">The set.</param>
/// <param name="Code">Its stable code.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Attributes">Its members, in form order.</param>
internal sealed record AttributeSetResponse(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<AttributeSetMemberResponse> Attributes);

/// <summary>One attribute's membership of a set.</summary>
/// <param name="AttributeId">The attribute.</param>
/// <param name="Code">Its stable code.</param>
/// <param name="Name">Its label.</param>
/// <param name="DataType">What kind of value it holds.</param>
/// <param name="IsRequired">Whether this set makes it mandatory.</param>
/// <param name="Position">Sort order in the form.</param>
internal sealed record AttributeSetMemberResponse(
    Guid AttributeId,
    string Code,
    string Name,
    AttributeDataType DataType,
    bool IsRequired,
    int Position);

/// <summary>One option, as a caller supplies it.</summary>
/// <param name="Id">The existing option to keep, or null to add one.</param>
/// <param name="Value">The machine value.</param>
/// <param name="Label">The shopper-facing label.</param>
/// <param name="SwatchHex">A hex colour.</param>
/// <param name="Position">Sort order.</param>
internal sealed record AttributeOptionPayload(
    Guid? Id,
    string Value,
    string Label,
    string? SwatchHex,
    int Position);

/// <summary>One membership, as a caller supplies it.</summary>
/// <param name="AttributeId">The attribute.</param>
/// <param name="IsRequired">Whether this set makes it mandatory.</param>
/// <param name="Position">Sort order in the form.</param>
internal sealed record AttributeSetMemberPayload(Guid AttributeId, bool IsRequired, int Position);

/// <summary>Lists attributes.</summary>
/// <param name="FilterableOnly">Whether to return only the ones offered as filters.</param>
/// <param name="VariantDefiningOnly">Whether to return only the variant axes.</param>
internal sealed record ListAttributesQuery(bool FilterableOnly, bool VariantDefiningOnly)
    : IQuery<IReadOnlyList<AttributeResponse>>;

/// <summary>Reads one attribute.</summary>
/// <param name="AttributeId">The attribute.</param>
internal sealed record GetAttributeQuery(Guid AttributeId) : IQuery<AttributeResponse>;

/// <summary>Declares an attribute.</summary>
/// <param name="Code">A code to use, or null to derive one from the name.</param>
/// <param name="Name">The shopper-facing label.</param>
/// <param name="DataType">What kind of value it holds.</param>
/// <param name="Unit">The unit its numbers are in.</param>
/// <param name="IsVariantDefining">Whether it is a variant axis.</param>
/// <param name="IsFilterable">Whether it is offered as a filter.</param>
/// <param name="IsSearchable">Whether it feeds the search text.</param>
/// <param name="IsRequired">Whether a product must supply it before publishing.</param>
/// <param name="Position">Sort order.</param>
/// <param name="Options">Its permitted values, for a list attribute.</param>
internal sealed record CreateAttributeCommand(
    string? Code,
    string Name,
    AttributeDataType DataType,
    string? Unit,
    bool IsVariantDefining,
    bool IsFilterable,
    bool IsSearchable,
    bool IsRequired,
    int Position,
    IReadOnlyList<AttributeOptionPayload>? Options) : ICommand<AttributeResponse>;

/// <summary>Updates an attribute and reconciles its option list.</summary>
/// <param name="AttributeId">The attribute.</param>
/// <param name="Name">The shopper-facing label.</param>
/// <param name="Unit">The unit its numbers are in.</param>
/// <param name="IsVariantDefining">Whether it is a variant axis.</param>
/// <param name="IsFilterable">Whether it is offered as a filter.</param>
/// <param name="IsSearchable">Whether it feeds the search text.</param>
/// <param name="IsRequired">Whether a product must supply it before publishing.</param>
/// <param name="Position">Sort order.</param>
/// <param name="Options">The complete option list it should end up with.</param>
internal sealed record UpdateAttributeCommand(
    Guid AttributeId,
    string Name,
    string? Unit,
    bool IsVariantDefining,
    bool IsFilterable,
    bool IsSearchable,
    bool IsRequired,
    int Position,
    IReadOnlyList<AttributeOptionPayload>? Options) : ICommand<AttributeResponse>;

/// <summary>Removes an attribute nothing uses.</summary>
/// <param name="AttributeId">The attribute.</param>
internal sealed record DeleteAttributeCommand(Guid AttributeId) : ICommand;

/// <summary>Lists attribute sets.</summary>
internal sealed record ListAttributeSetsQuery : IQuery<IReadOnlyList<AttributeSetResponse>>;

/// <summary>Reads one attribute set.</summary>
/// <param name="AttributeSetId">The set.</param>
internal sealed record GetAttributeSetQuery(Guid AttributeSetId) : IQuery<AttributeSetResponse>;

/// <summary>Declares an attribute set.</summary>
/// <param name="Code">A code to use, or null to derive one from the name.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Attributes">What is in it.</param>
internal sealed record CreateAttributeSetCommand(
    string? Code,
    string Name,
    string? Description,
    IReadOnlyList<AttributeSetMemberPayload>? Attributes) : ICommand<AttributeSetResponse>;

/// <summary>Updates an attribute set and reconciles its membership.</summary>
/// <param name="AttributeSetId">The set.</param>
/// <param name="Name">What it is called.</param>
/// <param name="Description">What it is for.</param>
/// <param name="Attributes">The complete membership it should end up with.</param>
internal sealed record UpdateAttributeSetCommand(
    Guid AttributeSetId,
    string Name,
    string? Description,
    IReadOnlyList<AttributeSetMemberPayload>? Attributes) : ICommand<AttributeSetResponse>;

/// <summary>Removes an attribute set no category uses.</summary>
/// <param name="AttributeSetId">The set.</param>
internal sealed record DeleteAttributeSetCommand(Guid AttributeSetId) : ICommand;

/// <summary>Rejects an attribute that could never be stored.</summary>
internal sealed class CreateAttributeValidator : AbstractValidator<CreateAttributeCommand>
{
    public CreateAttributeValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.DataType).IsInEnum();
        RuleFor(command => command.Unit).MaximumLength(16);
        RuleFor(command => command.Code)
            .Matches(CatalogFormats.Code())
            .MaximumLength(64)
            .When(command => !string.IsNullOrWhiteSpace(command.Code))
            .WithMessage("A code is lowercase letters, digits and single underscores.");

        RuleForEach(command => command.Options).SetValidator(new AttributeOptionPayloadValidator());

        // A closed list with no values is a filter with no options and a variant axis nothing can
        // be pinned to. Caught here, where the message can say so.
        RuleFor(command => command.Options)
            .NotEmpty()
            .When(command => command.DataType is AttributeDataType.Select or AttributeDataType.MultiSelect)
            .WithMessage("A select attribute needs at least one option.");
    }
}

/// <summary>The same rules, for a change.</summary>
internal sealed class UpdateAttributeValidator : AbstractValidator<UpdateAttributeCommand>
{
    public UpdateAttributeValidator()
    {
        RuleFor(command => command.AttributeId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Unit).MaximumLength(16);
        RuleForEach(command => command.Options).SetValidator(new AttributeOptionPayloadValidator());
    }
}

/// <summary>Rejects an option that could never be stored.</summary>
internal sealed class AttributeOptionPayloadValidator : AbstractValidator<AttributeOptionPayload>
{
    public AttributeOptionPayloadValidator()
    {
        RuleFor(option => option.Value).NotEmpty().MaximumLength(120);
        RuleFor(option => option.Label).NotEmpty().MaximumLength(160);
        RuleFor(option => option.SwatchHex)
            .Matches(CatalogFormats.HexColour())
            .When(option => !string.IsNullOrWhiteSpace(option.SwatchHex))
            .WithMessage("A swatch colour is a CSS hex triplet, for example #C08552.");
    }
}

/// <summary>Rejects an attribute set that could never be stored.</summary>
internal sealed class CreateAttributeSetValidator : AbstractValidator<CreateAttributeSetCommand>
{
    public CreateAttributeSetValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Description).MaximumLength(1000);
        RuleFor(command => command.Code)
            .Matches(CatalogFormats.Code())
            .MaximumLength(64)
            .When(command => !string.IsNullOrWhiteSpace(command.Code))
            .WithMessage("A code is lowercase letters, digits and single underscores.");
    }
}

/// <summary>The same rules, for a change.</summary>
internal sealed class UpdateAttributeSetValidator : AbstractValidator<UpdateAttributeSetCommand>
{
    public UpdateAttributeSetValidator()
    {
        RuleFor(command => command.AttributeSetId).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(160);
        RuleFor(command => command.Description).MaximumLength(1000);
    }
}

/// <summary>Turns attributes into responses.</summary>
internal static class AttributeProjection
{
    /// <summary>States an attribute and its options.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="options">Its options, in any order; sorted here.</param>
    public static AttributeResponse ToResponse(
        ProductAttribute attribute,
        IEnumerable<AttributeOption> options)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        ArgumentNullException.ThrowIfNull(options);

        return new AttributeResponse(
            attribute.Id,
            attribute.Code,
            attribute.Name,
            attribute.DataType,
            attribute.Unit,
            attribute.IsVariantDefining,
            attribute.IsFilterable,
            attribute.IsSearchable,
            attribute.IsRequired,
            attribute.Position,
            [
                .. options
                    .OrderBy(option => option.Position)
                    .ThenBy(option => option.Label, StringComparer.Ordinal)
                    .Select(option => new AttributeOptionResponse(
                        option.Id,
                        option.Value,
                        option.Label,
                        option.SwatchHex,
                        option.Position)),
            ]);
    }
}

/// <summary>Lists attributes with their options.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class ListAttributesQueryHandler(CatalogDbContext context)
    : IQueryHandler<ListAttributesQuery, IReadOnlyList<AttributeResponse>>
{
    public async Task<Result<IReadOnlyList<AttributeResponse>>> HandleAsync(
        ListAttributesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var attributes = context.Attributes.AsNoTracking().AsQueryable();

        if (query.FilterableOnly)
        {
            attributes = attributes.Where(attribute => attribute.IsFilterable);
        }

        if (query.VariantDefiningOnly)
        {
            attributes = attributes.Where(attribute => attribute.IsVariantDefining);
        }

        var rows = await attributes
            .OrderBy(attribute => attribute.Position)
            .ThenBy(attribute => attribute.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Every option for the attributes in hand, in one read. Not unbounded: this is a taxonomy,
        // and a marketplace with more than a few thousand option rows has a different problem.
        var ids = rows.ConvertAll(attribute => attribute.Id);

        var options = await context.AttributeOptions
            .AsNoTracking()
            .Where(option => ids.Contains(option.AttributeId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byAttribute = options.GroupBy(option => option.AttributeId)
            .ToDictionary(group => group.Key, group => group.ToList());

        IReadOnlyList<AttributeResponse> response =
        [
            .. rows.Select(attribute => AttributeProjection.ToResponse(
                attribute,
                byAttribute.GetValueOrDefault(attribute.Id) ?? [])),
        ];

        return Result.Success(response);
    }
}

/// <summary>Reads one attribute.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class GetAttributeQueryHandler(CatalogDbContext context)
    : IQueryHandler<GetAttributeQuery, AttributeResponse>
{
    public async Task<Result<AttributeResponse>> HandleAsync(
        GetAttributeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var attribute = await context.Attributes
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.AttributeId, cancellationToken)
            .ConfigureAwait(false);

        if (attribute is null)
        {
            return CatalogErrors.NotFound("attribute");
        }

        var options = await context.AttributeOptions
            .AsNoTracking()
            .Where(option => option.AttributeId == attribute.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(AttributeProjection.ToResponse(attribute, options));
    }
}

/// <summary>Declares an attribute.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class CreateAttributeCommandHandler(CatalogDbContext context, IAuditLogger audit)
    : ICommandHandler<CreateAttributeCommand, AttributeResponse>
{
    /// <summary>The audited action for a new attribute.</summary>
    public const string AuditAction = "catalog.attribute.created";

    /// <summary>The entity type recorded against every attribute action.</summary>
    public const string AuditEntityType = "Attribute";

    public async Task<Result<AttributeResponse>> HandleAsync(
        CreateAttributeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var code = string.IsNullOrWhiteSpace(command.Code)
            ? CatalogFormats.ToCode(command.Name)
            : command.Code.Trim().ToLowerInvariant();

        if (code.Length == 0)
        {
            return Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["code"] = ["That name produces an empty code. Supply one explicitly."],
                });
        }

        if (await context.Attributes.AnyAsync(a => a.Code == code, cancellationToken).ConfigureAwait(false))
        {
            return CatalogErrors.Duplicate("attribute code");
        }

        var attribute = ProductAttribute.Create(code, command.Name.Trim(), command.DataType);

        if (command.IsVariantDefining && !attribute.CanDefineVariants)
        {
            return CatalogErrors.NotAVariantAxis(code);
        }

        attribute.Describe(
            command.Name.Trim(),
            command.Unit,
            command.IsVariantDefining,
            command.IsFilterable,
            command.IsSearchable,
            command.IsRequired,
            command.Position);

        context.Attributes.Add(attribute);

        var options = new List<AttributeOption>();

        if (attribute.UsesOptions && command.Options is { Count: > 0 })
        {
            var duplicate = AttributeOptions.FirstDuplicateValue(command.Options);

            if (duplicate is not null)
            {
                return CatalogErrors.Duplicate($"option value '{duplicate}'");
            }

            options.AddRange(command.Options.Select(option => AttributeOption.Create(
                attribute.Id,
                option.Value.Trim().ToLowerInvariant(),
                option.Label.Trim(),
                option.SwatchHex,
                option.Position)));

            context.AttributeOptions.AddRange(options);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = attribute.Id.ToString(),
                After = new
                {
                    attribute.Code,
                    attribute.Name,
                    DataType = attribute.DataType.ToString(),
                    attribute.IsVariantDefining,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(AttributeProjection.ToResponse(attribute, options));
    }
}

/// <summary>Updates an attribute and reconciles its options.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdateAttributeCommandHandler(CatalogDbContext context, IAuditLogger audit)
    : ICommandHandler<UpdateAttributeCommand, AttributeResponse>
{
    /// <summary>The audited action for a change to an attribute.</summary>
    public const string AuditAction = "catalog.attribute.updated";

    public async Task<Result<AttributeResponse>> HandleAsync(
        UpdateAttributeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var attribute = await context.Attributes
            .FirstOrDefaultAsync(candidate => candidate.Id == command.AttributeId, cancellationToken)
            .ConfigureAwait(false);

        if (attribute is null)
        {
            return CatalogErrors.NotFound("attribute");
        }

        if (command.IsVariantDefining && !attribute.CanDefineVariants)
        {
            return CatalogErrors.NotAVariantAxis(attribute.Code);
        }

        var before = new { attribute.Name, attribute.IsVariantDefining, attribute.IsFilterable };

        attribute.Describe(
            command.Name.Trim(),
            command.Unit,
            command.IsVariantDefining,
            command.IsFilterable,
            command.IsSearchable,
            command.IsRequired,
            command.Position);

        var existing = await context.AttributeOptions
            .Where(option => option.AttributeId == attribute.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (attribute.UsesOptions && command.Options is not null)
        {
            var reconciled = await ReconcileAsync(attribute, existing, command.Options, cancellationToken)
                .ConfigureAwait(false);

            if (reconciled.IsFailure)
            {
                return reconciled.Error;
            }

            existing = reconciled.Value;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateAttributeCommandHandler.AuditEntityType,
                EntityId = attribute.Id.ToString(),
                Before = before,
                After = new { attribute.Name, attribute.IsVariantDefining, attribute.IsFilterable },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(AttributeProjection.ToResponse(attribute, existing));
    }

    /// <summary>
    /// Brings the stored options in line with the submitted list.
    /// </summary>
    /// <remarks>
    /// Options are matched by id and updated in place rather than deleted and re-added. A variant's
    /// defining combination points at an option id, so re-creating "Beige" with a new id would
    /// silently orphan every variant that is beige.
    /// </remarks>
    private async Task<Result<List<AttributeOption>>> ReconcileAsync(
        ProductAttribute attribute,
        List<AttributeOption> existing,
        IReadOnlyList<AttributeOptionPayload> submitted,
        CancellationToken cancellationToken)
    {
        var duplicate = AttributeOptions.FirstDuplicateValue(submitted);

        if (duplicate is not null)
        {
            return CatalogErrors.Duplicate($"option value '{duplicate}'");
        }

        var byId = existing.ToDictionary(option => option.Id);
        var kept = new List<AttributeOption>(submitted.Count);

        foreach (var payload in submitted)
        {
            if (payload.Id is { } id && byId.TryGetValue(id, out var option))
            {
                option.Describe(payload.Label.Trim(), payload.SwatchHex, payload.Position);
                kept.Add(option);
                continue;
            }

            var created = AttributeOption.Create(
                attribute.Id,
                payload.Value.Trim().ToLowerInvariant(),
                payload.Label.Trim(),
                payload.SwatchHex,
                payload.Position);

            context.AttributeOptions.Add(created);
            kept.Add(created);
        }

        var removed = existing.Where(option => !kept.Contains(option)).ToList();

        if (removed.Count > 0)
        {
            var removedIds = removed.ConvertAll(option => option.Id);

            // An option a variant is pinned to cannot go: removing it would leave that variant with
            // an axis pointing at nothing, and its combination hash would no longer describe it.
            var inUse = await context.VariantAttributeValues
                .AnyAsync(value => removedIds.Contains(value.OptionId), cancellationToken)
                .ConfigureAwait(false)
                || await context.ProductAttributeValues
                    .AnyAsync(
                        value => value.ValueOptionId != null && removedIds.Contains(value.ValueOptionId.Value),
                        cancellationToken)
                    .ConfigureAwait(false);

            if (inUse)
            {
                return CatalogErrors.StillInUse("products or variants");
            }

            context.AttributeOptions.RemoveRange(removed);
        }

        return kept;
    }
}

/// <summary>Removes an attribute.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class DeleteAttributeCommandHandler(CatalogDbContext context, IAuditLogger audit)
    : ICommandHandler<DeleteAttributeCommand>
{
    /// <summary>The audited action for a removed attribute.</summary>
    public const string AuditAction = "catalog.attribute.deleted";

    public async Task<Result> HandleAsync(DeleteAttributeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var attribute = await context.Attributes
            .FirstOrDefaultAsync(candidate => candidate.Id == command.AttributeId, cancellationToken)
            .ConfigureAwait(false);

        if (attribute is null)
        {
            return Result.Failure(CatalogErrors.NotFound("attribute"));
        }

        var inUse = await context.ProductAttributeValues
            .AnyAsync(value => value.AttributeId == attribute.Id, cancellationToken)
            .ConfigureAwait(false)
            || await context.VariantAttributeValues
                .AnyAsync(value => value.AttributeId == attribute.Id, cancellationToken)
                .ConfigureAwait(false)
            || await context.AttributeSetMembers
                .AnyAsync(member => member.AttributeId == attribute.Id, cancellationToken)
                .ConfigureAwait(false);

        if (inUse)
        {
            return Result.Failure(CatalogErrors.StillInUse("products, variants or attribute sets"));
        }

        var options = await context.AttributeOptions
            .Where(option => option.AttributeId == attribute.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        context.AttributeOptions.RemoveRange(options);
        context.Attributes.Remove(attribute);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateAttributeCommandHandler.AuditEntityType,
                EntityId = attribute.Id.ToString(),
                Before = new { attribute.Code, attribute.Name },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Helpers shared by the attribute handlers.</summary>
internal static class AttributeOptions
{
    /// <summary>
    /// The first machine value that appears twice in a submitted option list, or null.
    /// </summary>
    /// <remarks>
    /// Caught before the write because the unique index would otherwise turn it into a 500 with a
    /// constraint name in it. The comparison is case-insensitive because the values are lowercased
    /// on the way in, so "Beige" and "beige" would collide only after being stored.
    /// </remarks>
    /// <param name="options">The submitted options.</param>
    public static string? FirstDuplicateValue(IReadOnlyList<AttributeOptionPayload> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var option in options)
        {
            if (!seen.Add(option.Value.Trim()))
            {
                return option.Value.Trim();
            }
        }

        return null;
    }
}
