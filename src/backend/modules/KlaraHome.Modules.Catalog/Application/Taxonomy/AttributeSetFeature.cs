using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Catalog.Application.Validation;
using KlaraHome.Modules.Catalog.Domain;
using KlaraHome.Modules.Catalog.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Catalog.Application.Taxonomy;

/// <summary>Lists attribute sets with their membership.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class ListAttributeSetsQueryHandler(CatalogDbContext context)
    : IQueryHandler<ListAttributeSetsQuery, IReadOnlyList<AttributeSetResponse>>
{
    public async Task<Result<IReadOnlyList<AttributeSetResponse>>> HandleAsync(
        ListAttributeSetsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sets = await context.AttributeSets
            .AsNoTracking()
            .OrderBy(set => set.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var members = await AttributeSetProjection
            .LoadMembersAsync(context, sets.ConvertAll(set => set.Id), cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<AttributeSetResponse> response =
        [
            .. sets.Select(set => AttributeSetProjection.ToResponse(
                set,
                members.GetValueOrDefault(set.Id) ?? [])),
        ];

        return Result.Success(response);
    }
}

/// <summary>Reads one attribute set.</summary>
/// <param name="context">The Catalog data context.</param>
internal sealed class GetAttributeSetQueryHandler(CatalogDbContext context)
    : IQueryHandler<GetAttributeSetQuery, AttributeSetResponse>
{
    public async Task<Result<AttributeSetResponse>> HandleAsync(
        GetAttributeSetQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var set = await context.AttributeSets
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.AttributeSetId, cancellationToken)
            .ConfigureAwait(false);

        if (set is null)
        {
            return CatalogErrors.NotFound("attribute set");
        }

        var members = await AttributeSetProjection
            .LoadMembersAsync(context, [set.Id], cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(AttributeSetProjection.ToResponse(set, members.GetValueOrDefault(set.Id) ?? []));
    }
}

/// <summary>Declares an attribute set.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class CreateAttributeSetCommandHandler(CatalogDbContext context, IAuditLogger audit)
    : ICommandHandler<CreateAttributeSetCommand, AttributeSetResponse>
{
    /// <summary>The audited action for a new attribute set.</summary>
    public const string AuditAction = "catalog.attribute-set.created";

    /// <summary>The entity type recorded against every attribute-set action.</summary>
    public const string AuditEntityType = "AttributeSet";

    public async Task<Result<AttributeSetResponse>> HandleAsync(
        CreateAttributeSetCommand command,
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

        if (await context.AttributeSets.AnyAsync(s => s.Code == code, cancellationToken).ConfigureAwait(false))
        {
            return CatalogErrors.Duplicate("attribute set code");
        }

        var set = AttributeSet.Create(code, command.Name.Trim());
        set.Describe(command.Name.Trim(), command.Description);
        context.AttributeSets.Add(set);

        var applied = await ApplyMembershipAsync(context, set.Id, [], command.Attributes, cancellationToken)
            .ConfigureAwait(false);

        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = set.Id.ToString(),
                After = new { set.Code, set.Name, Attributes = applied.Value.Count },
            },
            cancellationToken).ConfigureAwait(false);

        var members = await AttributeSetProjection
            .LoadMembersAsync(context, [set.Id], cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(AttributeSetProjection.ToResponse(set, members.GetValueOrDefault(set.Id) ?? []));
    }

    /// <summary>
    /// Brings a set's membership in line with the submitted list.
    /// </summary>
    /// <remarks>
    /// Shared by create and update, because "create with three attributes" and "update to have
    /// three attributes" are the same operation against an empty starting set — and two
    /// implementations of it would drift on the validation.
    /// </remarks>
    /// <param name="context">The Catalog data context.</param>
    /// <param name="setId">The set.</param>
    /// <param name="existing">What it currently holds.</param>
    /// <param name="submitted">What it should hold, or null to leave it alone.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal static async Task<Result<List<AttributeSetMember>>> ApplyMembershipAsync(
        CatalogDbContext context,
        Guid setId,
        List<AttributeSetMember> existing,
        IReadOnlyList<AttributeSetMemberPayload>? submitted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(existing);

        if (submitted is null)
        {
            return existing;
        }

        var wanted = submitted
            .GroupBy(member => member.AttributeId)
            .Select(group => group.First())
            .ToList();

        var attributeIds = wanted.ConvertAll(member => member.AttributeId);

        var known = await context.Attributes
            .AsNoTracking()
            .Where(attribute => attributeIds.Contains(attribute.Id))
            .Select(attribute => attribute.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (known.Count != attributeIds.Count)
        {
            return CatalogErrors.NotFound("attribute");
        }

        var byAttribute = existing.ToDictionary(member => member.AttributeId);
        var kept = new List<AttributeSetMember>(wanted.Count);

        foreach (var payload in wanted)
        {
            if (byAttribute.TryGetValue(payload.AttributeId, out var member))
            {
                kept.Add(member);
                continue;
            }

            var created = AttributeSetMember.Create(
                setId,
                payload.AttributeId,
                payload.IsRequired,
                payload.Position);

            context.AttributeSetMembers.Add(created);
            kept.Add(created);
        }

        // Membership rows carry no history and nothing points at them, so a removal is a genuine
        // delete rather than a soft one: an attribute that is no longer on the lamp form simply is
        // not on it, and the values already recorded against products are untouched.
        var removed = existing.Where(member => !kept.Contains(member)).ToList();

        if (removed.Count > 0)
        {
            context.AttributeSetMembers.RemoveRange(removed);
        }

        return kept;
    }
}

/// <summary>Updates an attribute set.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class UpdateAttributeSetCommandHandler(CatalogDbContext context, IAuditLogger audit)
    : ICommandHandler<UpdateAttributeSetCommand, AttributeSetResponse>
{
    /// <summary>The audited action for a change to an attribute set.</summary>
    public const string AuditAction = "catalog.attribute-set.updated";

    public async Task<Result<AttributeSetResponse>> HandleAsync(
        UpdateAttributeSetCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var set = await context.AttributeSets
            .FirstOrDefaultAsync(candidate => candidate.Id == command.AttributeSetId, cancellationToken)
            .ConfigureAwait(false);

        if (set is null)
        {
            return CatalogErrors.NotFound("attribute set");
        }

        var before = new { set.Name, set.Description };
        set.Describe(command.Name.Trim(), command.Description);

        var existing = await context.AttributeSetMembers
            .Where(member => member.AttributeSetId == set.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var applied = await CreateAttributeSetCommandHandler
            .ApplyMembershipAsync(context, set.Id, existing, command.Attributes, cancellationToken)
            .ConfigureAwait(false);

        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateAttributeSetCommandHandler.AuditEntityType,
                EntityId = set.Id.ToString(),
                Before = before,
                After = new { set.Name, set.Description, Attributes = applied.Value.Count },
            },
            cancellationToken).ConfigureAwait(false);

        var members = await AttributeSetProjection
            .LoadMembersAsync(context, [set.Id], cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(AttributeSetProjection.ToResponse(set, members.GetValueOrDefault(set.Id) ?? []));
    }
}

/// <summary>Removes an attribute set.</summary>
/// <param name="context">The Catalog data context.</param>
/// <param name="audit">Records the change.</param>
internal sealed class DeleteAttributeSetCommandHandler(CatalogDbContext context, IAuditLogger audit)
    : ICommandHandler<DeleteAttributeSetCommand>
{
    /// <summary>The audited action for a removed attribute set.</summary>
    public const string AuditAction = "catalog.attribute-set.deleted";

    public async Task<Result> HandleAsync(DeleteAttributeSetCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var set = await context.AttributeSets
            .FirstOrDefaultAsync(candidate => candidate.Id == command.AttributeSetId, cancellationToken)
            .ConfigureAwait(false);

        if (set is null)
        {
            return Result.Failure(CatalogErrors.NotFound("attribute set"));
        }

        if (await context.Categories
                .AnyAsync(category => category.AttributeSetId == set.Id, cancellationToken)
                .ConfigureAwait(false))
        {
            return Result.Failure(CatalogErrors.StillInUse("categories"));
        }

        var members = await context.AttributeSetMembers
            .Where(member => member.AttributeSetId == set.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        context.AttributeSetMembers.RemoveRange(members);
        context.AttributeSets.Remove(set);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = CreateAttributeSetCommandHandler.AuditEntityType,
                EntityId = set.Id.ToString(),
                Before = new { set.Code, set.Name },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Turns attribute sets into responses.</summary>
internal static class AttributeSetProjection
{
    /// <summary>One set's membership, resolved to the attributes' own labels.</summary>
    /// <param name="context">The Catalog data context.</param>
    /// <param name="setIds">The sets to load for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<Dictionary<Guid, List<AttributeSetMemberResponse>>> LoadMembersAsync(
        CatalogDbContext context,
        IReadOnlyList<Guid> setIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(setIds);

        if (setIds.Count == 0)
        {
            return [];
        }

        // One join rather than a membership read followed by an attribute read: the membership row
        // holds only an id, and every caller of this needs the label next to it.
        var rows = await (
            from member in context.AttributeSetMembers.AsNoTracking()
            join attribute in context.Attributes.AsNoTracking() on member.AttributeId equals attribute.Id
            where setIds.Contains(member.AttributeSetId)
            orderby member.Position, attribute.Name
            select new
            {
                member.AttributeSetId,
                member.AttributeId,
                attribute.Code,
                attribute.Name,
                attribute.DataType,
                member.IsRequired,
                member.Position,
            }).ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .GroupBy(row => row.AttributeSetId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(row => new AttributeSetMemberResponse(
                        row.AttributeId,
                        row.Code,
                        row.Name,
                        row.DataType,
                        row.IsRequired,
                        row.Position))
                    .ToList());
    }

    /// <summary>States a set.</summary>
    /// <param name="set">The set.</param>
    /// <param name="members">Its membership.</param>
    public static AttributeSetResponse ToResponse(
        AttributeSet set,
        IReadOnlyList<AttributeSetMemberResponse> members)
    {
        ArgumentNullException.ThrowIfNull(set);

        return new AttributeSetResponse(set.Id, set.Code, set.Name, set.Description, members);
    }
}
