using System.Text.Json;
using KlaraHome.Modules.Content.Domain;

namespace KlaraHome.Modules.Content.Infrastructure.Blocks;

/// <summary>
/// Reads a stored block and reports everything it points at.
/// </summary>
/// <remarks>
/// <para>
/// The read-time half of what <see cref="BlockValidator"/> does at write time. The validator gathers
/// references while it is already walking the document, because it is; this walks a document that has
/// already been validated, because a page is rendered far more often than it is saved and re-running
/// the validator would sanitise, canonicalise and re-serialise every block on every request.
/// </para>
/// <para>
/// It is schema-driven rather than hard-coded per block type, which is what stops the two drifting: a
/// new field of kind <c>MediaRef</c> is picked up here the moment it appears in the catalogue, with no
/// second place to remember.
/// </para>
/// </remarks>
internal static class BlockReferenceReader
{
    /// <summary>Everything one stored block points at.</summary>
    /// <param name="type">The block type.</param>
    /// <param name="config">Its stored configuration.</param>
    public static BlockReferences Read(BlockType type, JsonElement config)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return BlockReferences.Empty;
        }

        var descriptor = BlockCatalog.Describe(type);
        var media = new List<Guid>();
        var products = new List<Guid>();
        var categories = new List<Guid>();
        var collections = new List<string>();

        foreach (var field in descriptor.Fields)
        {
            Collect(field, config, media, products, categories, collections);
        }

        if (descriptor.Items is { Count: > 0 }
            && config.TryGetProperty(BlockValidator.ItemsProperty, out var items)
            && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var field in descriptor.Items)
                {
                    Collect(field, item, media, products, categories, collections);
                }
            }
        }

        return new BlockReferences(media, products, categories, collections);
    }

    /// <summary>
    /// The tile-image overrides a category-tiles block carries, keyed by the category they override.
    /// </summary>
    /// <remarks>
    /// A separate read because the pairing is the point: the images are already in the block's media
    /// references, but which category each one belongs to is lost by the time they are a flat list.
    /// </remarks>
    /// <param name="type">The block type.</param>
    /// <param name="config">Its stored configuration.</param>
    public static IReadOnlyDictionary<Guid, Guid> ReadCategoryImageOverrides(BlockType type, JsonElement config)
    {
        var overrides = new Dictionary<Guid, Guid>();

        if (type != BlockType.CategoryTiles
            || config.ValueKind != JsonValueKind.Object
            || !config.TryGetProperty(BlockValidator.ItemsProperty, out var items)
            || items.ValueKind != JsonValueKind.Array)
        {
            return overrides;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !TryReadGuid(item, "categoryId", out var categoryId)
                || !TryReadGuid(item, "imageFileId", out var imageId))
            {
                continue;
            }

            overrides[categoryId] = imageId;
        }

        return overrides;
    }

    private static void Collect(
        BlockField field,
        JsonElement owner,
        List<Guid> media,
        List<Guid> products,
        List<Guid> categories,
        List<string> collections)
    {
        if (!owner.TryGetProperty(field.Name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (field.IsList)
        {
            if (value.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var element in value.EnumerateArray())
            {
                CollectOne(field, element, media, products, categories, collections);
            }

            return;
        }

        CollectOne(field, value, media, products, categories, collections);
    }

    private static void CollectOne(
        BlockField field,
        JsonElement value,
        List<Guid> media,
        List<Guid> products,
        List<Guid> categories,
        List<string> collections)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            return;
        }

        switch (field.Kind)
        {
            case BlockFieldKind.MediaRef when Guid.TryParse(value.GetString(), out var mediaId):
                Add(media, mediaId);
                break;

            case BlockFieldKind.ProductRef when Guid.TryParse(value.GetString(), out var productId):
                Add(products, productId);
                break;

            case BlockFieldKind.CategoryRef when Guid.TryParse(value.GetString(), out var categoryId):
                Add(categories, categoryId);
                break;

            case BlockFieldKind.CollectionRef when value.GetString() is { Length: > 0 } slug:
                if (!collections.Contains(slug, StringComparer.Ordinal))
                {
                    collections.Add(slug);
                }

                break;

            default:
                break;
        }
    }

    private static void Add(List<Guid> target, Guid id)
    {
        if (!target.Contains(id))
        {
            target.Add(id);
        }
    }

    private static bool TryReadGuid(JsonElement owner, string property, out Guid value)
    {
        value = Guid.Empty;

        return owner.TryGetProperty(property, out var element)
               && element.ValueKind == JsonValueKind.String
               && Guid.TryParse(element.GetString(), out value);
    }
}
