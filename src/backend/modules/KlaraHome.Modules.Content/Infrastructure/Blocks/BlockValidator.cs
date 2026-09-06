using System.Globalization;
using System.Text.Json;
using KlaraHome.Modules.Content.Application.Validation;
using KlaraHome.Modules.Content.Domain;

namespace KlaraHome.Modules.Content.Infrastructure.Blocks;

/// <summary>Serialisation for the open-shaped columns in this schema.</summary>
/// <remarks>
/// camelCase, matching the API payloads these documents are handed to and received from. A
/// <c>jsonb</c> column read by a storefront component is part of the API surface, and a casing
/// convention applied on one side and not the other is a class of bug worth designing out.
/// </remarks>
internal static class ContentJson
{
    /// <summary>The options every document in this schema is read and written with.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <summary>The Postgres type every open-shaped column in this schema uses.</summary>
    public const string ColumnType = "jsonb";

    /// <summary>Turns a stored document into a detached <see cref="JsonElement"/>.</summary>
    /// <remarks>
    /// A <see cref="JsonDocument"/> holds a pooled buffer and has to be disposed; the element it
    /// hands out is only valid while it lives, so it is cloned before the document goes.
    /// </remarks>
    /// <param name="json">The stored document.</param>
    public static JsonElement Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // A stored document that will not parse is a row written before a schema change or by
            // hand. Rendering an empty block is a far better answer than a 500 on the home page.
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
    }
}

/// <summary>
/// Everything a block points at, gathered while it was being validated.
/// </summary>
/// <remarks>
/// Collected during validation rather than rediscovered at render time, and that is worth the small
/// awkwardness of returning two things from one call. The walk that checks a field's kind is exactly
/// the walk that would find the ids, so doing it twice would be doing the same tree traversal twice
/// per block per page render.
/// </remarks>
/// <param name="MediaIds">Media files the block refers to, in the order it named them.</param>
/// <param name="ProductIds">Products it refers to.</param>
/// <param name="CategoryIds">Categories it refers to.</param>
/// <param name="CollectionSlugs">Curated collections it refers to.</param>
internal sealed record BlockReferences(
    IReadOnlyList<Guid> MediaIds,
    IReadOnlyList<Guid> ProductIds,
    IReadOnlyList<Guid> CategoryIds,
    IReadOnlyList<string> CollectionSlugs)
{
    /// <summary>A block that refers to nothing.</summary>
    public static BlockReferences Empty { get; } = new([], [], [], []);

    /// <summary>The union of several blocks' references, deduplicated and in first-seen order.</summary>
    /// <param name="parts">The blocks' references.</param>
    public static BlockReferences Merge(IEnumerable<BlockReferences> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var media = new List<Guid>();
        var products = new List<Guid>();
        var categories = new List<Guid>();
        var collections = new List<string>();

        foreach (var part in parts)
        {
            AddDistinct(media, part.MediaIds);
            AddDistinct(products, part.ProductIds);
            AddDistinct(categories, part.CategoryIds);

            foreach (var slug in part.CollectionSlugs)
            {
                if (!collections.Contains(slug, StringComparer.Ordinal))
                {
                    collections.Add(slug);
                }
            }
        }

        return new BlockReferences(media, products, categories, collections);
    }

    private static void AddDistinct(List<Guid> target, IReadOnlyList<Guid> source)
    {
        foreach (var id in source)
        {
            if (!target.Contains(id))
            {
                target.Add(id);
            }
        }
    }
}

/// <summary>What validating one block produced.</summary>
/// <param name="Config">The canonical configuration document, ready to store.</param>
/// <param name="References">Everything the block points at.</param>
internal sealed record BlockValidation(string Config, BlockReferences References);

/// <summary>
/// Checks a block's configuration against its type's schema, and canonicalises it.
/// </summary>
/// <remarks>
/// <para>
/// Validation happens on the way <em>in</em>, once, rather than on the way out on every render. A
/// malformed block is therefore an error message next to the field that is wrong, at the moment an
/// editor pressed save — instead of a component throwing during server-side rendering, which is a
/// blank home page and a stack trace nobody can attribute to the person who caused it.
/// </para>
/// <para>
/// The output is canonical: fields in schema order, unknown properties refused rather than dropped,
/// rich text sanitised, and numbers and booleans stored as numbers and booleans rather than as
/// whatever the admin form's input produced. Canonicalising means two blocks that mean the same thing
/// are byte-identical in the database, which is what makes a version diff readable.
/// </para>
/// <para>
/// Unknown properties are <b>refused</b>, not ignored, and that is the deliberate choice. A silently
/// dropped field is a merchandiser who typed <c>headLine</c>, saw no error, and cannot work out why
/// the hero has no heading.
/// </para>
/// </remarks>
internal static class BlockValidator
{
    /// <summary>The property a block's repeated children live under.</summary>
    public const string ItemsProperty = "items";

    /// <summary>
    /// Validates one block and produces the document to store.
    /// </summary>
    /// <param name="type">The block type.</param>
    /// <param name="config">The configuration as it was sent.</param>
    /// <param name="path">
    /// Where this block sits in the request, for the field-error keys — <c>blocks[2]</c>. The admin
    /// app puts the message beside the input that caused it, so the key has to name the input.
    /// </param>
    /// <param name="errors">The field errors so far. Added to, never replaced.</param>
    /// <returns>The canonical document and its references, or null when the block was refused.</returns>
    public static BlockValidation? Validate(
        BlockType type,
        JsonElement config,
        string path,
        Dictionary<string, List<string>> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        var descriptor = BlockCatalog.Describe(type);

        if (config.ValueKind is not (JsonValueKind.Object or JsonValueKind.Undefined))
        {
            Add(errors, path, "A block's configuration must be an object.");
            return null;
        }

        var media = new List<Guid>();
        var products = new List<Guid>();
        var categories = new List<Guid>();
        var collections = new List<string>();

        // Messages rather than keys: two problems with the same field add to one entry, and a count
        // of keys would call that block valid.
        var before = errors.Sum(entry => entry.Value.Count);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();

            RefuseUnknown(descriptor, config, path, errors);

            foreach (var field in descriptor.Fields)
            {
                WriteField(field, config, $"{path}.{field.Name}", writer, errors, media, products, categories, collections);
            }

            if (descriptor.Items is { Count: > 0 })
            {
                WriteItems(descriptor, config, path, writer, errors, media, products, categories, collections);
            }

            writer.WriteEndObject();
        }

        CheckTypeRules(type, products, collections, path, errors);

        if (errors.Sum(entry => entry.Value.Count) > before)
        {
            return null;
        }

        return new BlockValidation(
            System.Text.Encoding.UTF8.GetString(buffer.ToArray()),
            new BlockReferences(media, products, categories, collections));
    }

    /// <summary>
    /// The rules that are about a block type rather than about one of its fields.
    /// </summary>
    /// <remarks>
    /// One so far, and it is the carousel's. A carousel that named both a collection and a list of
    /// products would have two answers to what it shows, and one that named neither would have none —
    /// both render as an empty strip on the home page with nothing on screen to say why. Refusing at
    /// save time is the only place either can be told apart from "the collection is empty today".
    /// </remarks>
    private static void CheckTypeRules(
        BlockType type,
        List<Guid> products,
        List<string> collections,
        string path,
        Dictionary<string, List<string>> errors)
    {
        if (type != BlockType.ProductCarousel)
        {
            return;
        }

        if (collections.Count > 0 && products.Count > 0)
        {
            Add(
                errors,
                $"{path}.collectionSlug",
                "A carousel shows either a collection or a chosen list of products, not both.");
        }
        else if (collections.Count == 0 && products.Count == 0)
        {
            Add(
                errors,
                $"{path}.collectionSlug",
                "A carousel needs either a collection or at least one product.");
        }
    }

    /// <summary>Rejects any property the schema does not declare.</summary>
    private static void RefuseUnknown(
        BlockDescriptor descriptor,
        JsonElement config,
        string path,
        Dictionary<string, List<string>> errors)
    {
        if (config.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in config.EnumerateObject())
        {
            var declared = descriptor.Fields.Any(field =>
                string.Equals(field.Name, property.Name, StringComparison.Ordinal));

            if (declared || (descriptor.Items is { Count: > 0 } && property.Name == ItemsProperty))
            {
                continue;
            }

            Add(
                errors,
                $"{path}.{property.Name}",
                $"A {descriptor.Label} block has no '{property.Name}' setting.");
        }
    }

    /// <summary>Validates and writes the repeated children of a block that has them.</summary>
    private static void WriteItems(
        BlockDescriptor descriptor,
        JsonElement config,
        string path,
        Utf8JsonWriter writer,
        Dictionary<string, List<string>> errors,
        List<Guid> media,
        List<Guid> products,
        List<Guid> categories,
        List<string> collections)
    {
        writer.WritePropertyName(ItemsProperty);
        writer.WriteStartArray();

        if (!config.TryGetProperty(ItemsProperty, out var items) || items.ValueKind == JsonValueKind.Null)
        {
            writer.WriteEndArray();
            return;
        }

        if (items.ValueKind != JsonValueKind.Array)
        {
            Add(errors, $"{path}.{ItemsProperty}", "The items must be a list.");
            writer.WriteEndArray();
            return;
        }

        var count = items.GetArrayLength();

        if (count > descriptor.MaxItems)
        {
            Add(
                errors,
                $"{path}.{ItemsProperty}",
                $"A {descriptor.Label} block may hold at most {descriptor.MaxItems} items.");
        }

        var index = 0;

        foreach (var item in items.EnumerateArray())
        {
            if (index >= descriptor.MaxItems)
            {
                break;
            }

            var itemPath = $"{path}.{ItemsProperty}[{index}]";

            if (item.ValueKind != JsonValueKind.Object)
            {
                Add(errors, itemPath, "Each item must be an object.");
                index++;
                continue;
            }

            foreach (var property in item.EnumerateObject())
            {
                if (!descriptor.Items!.Any(field =>
                        string.Equals(field.Name, property.Name, StringComparison.Ordinal)))
                {
                    Add(errors, $"{itemPath}.{property.Name}", $"An item has no '{property.Name}' setting.");
                }
            }

            writer.WriteStartObject();

            foreach (var field in descriptor.Items!)
            {
                WriteField(
                    field,
                    item,
                    $"{itemPath}.{field.Name}",
                    writer,
                    errors,
                    media,
                    products,
                    categories,
                    collections);
            }

            writer.WriteEndObject();
            index++;
        }

        writer.WriteEndArray();
    }

    /// <summary>Validates one field and writes it in its canonical form.</summary>
    private static void WriteField(
        BlockField field,
        JsonElement owner,
        string path,
        Utf8JsonWriter writer,
        Dictionary<string, List<string>> errors,
        List<Guid> media,
        List<Guid> products,
        List<Guid> categories,
        List<string> collections)
    {
        var present = owner.ValueKind == JsonValueKind.Object
                      && owner.TryGetProperty(field.Name, out var value)
                      && value.ValueKind != JsonValueKind.Null;

        if (!present)
        {
            if (field.IsRequired)
            {
                Add(errors, path, "This is required.");
            }

            // Absent optional fields are written as null rather than omitted, so every stored block
            // of a type has the same key set and a storefront component never has to test for a
            // missing property as well as an empty one.
            writer.WriteNull(field.Name);
            return;
        }

        owner.TryGetProperty(field.Name, out var element);

        if (field.IsList)
        {
            WriteList(field, element, path, writer, errors, media, products, categories, collections);
            return;
        }

        writer.WritePropertyName(field.Name);
        WriteScalar(field, element, path, writer, errors, media, products, categories, collections);
    }

    /// <summary>Validates and writes a list-valued field.</summary>
    private static void WriteList(
        BlockField field,
        JsonElement element,
        string path,
        Utf8JsonWriter writer,
        Dictionary<string, List<string>> errors,
        List<Guid> media,
        List<Guid> products,
        List<Guid> categories,
        List<string> collections)
    {
        writer.WritePropertyName(field.Name);

        if (element.ValueKind != JsonValueKind.Array)
        {
            Add(errors, path, "This must be a list.");
            writer.WriteStartArray();
            writer.WriteEndArray();
            return;
        }

        if (element.GetArrayLength() > field.MaxLength)
        {
            Add(errors, path, $"At most {field.MaxLength} values.");
        }

        writer.WriteStartArray();

        var index = 0;

        foreach (var item in element.EnumerateArray())
        {
            if (index >= field.MaxLength)
            {
                break;
            }

            WriteScalar(
                field,
                item,
                $"{path}[{index}]",
                writer,
                errors,
                media,
                products,
                categories,
                collections);

            index++;
        }

        writer.WriteEndArray();
    }

    /// <summary>Validates and writes one value of a field's declared kind.</summary>
    private static void WriteScalar(
        BlockField field,
        JsonElement element,
        string path,
        Utf8JsonWriter writer,
        Dictionary<string, List<string>> errors,
        List<Guid> media,
        List<Guid> products,
        List<Guid> categories,
        List<string> collections)
    {
        switch (field.Kind)
        {
            case BlockFieldKind.Boolean:
                if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    writer.WriteBooleanValue(element.GetBoolean());
                }
                else
                {
                    Add(errors, path, "This must be true or false.");
                    writer.WriteBooleanValue(false);
                }

                break;

            case BlockFieldKind.Integer:
                if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var number))
                {
                    if (number < 0 || number > field.MaxLength)
                    {
                        Add(errors, path, $"This must be between 0 and {field.MaxLength}.");
                    }

                    writer.WriteNumberValue(number);
                }
                else
                {
                    Add(errors, path, "This must be a whole number.");
                    writer.WriteNumberValue(0);
                }

                break;

            case BlockFieldKind.MediaRef:
            case BlockFieldKind.ProductRef:
            case BlockFieldKind.CategoryRef:
                WriteReference(field, element, path, writer, errors, media, products, categories);
                break;

            case BlockFieldKind.CollectionRef:
                {
                    var slug = element.ValueKind == JsonValueKind.String ? element.GetString() : null;

                    if (string.IsNullOrWhiteSpace(slug) || !ContentFormats.Slug().IsMatch(slug))
                    {
                        Add(errors, path, "This must be a collection's address.");
                        writer.WriteNullValue();
                    }
                    else
                    {
                        collections.Add(slug);
                        writer.WriteStringValue(slug);
                    }
                }

                break;

            case BlockFieldKind.Link:
                {
                    var link = element.ValueKind == JsonValueKind.String ? element.GetString() : null;

                    if (!ContentFormats.IsLinkTarget(link))
                    {
                        Add(errors, path, "A link must be a path beginning with / or an http(s) address.");
                        writer.WriteNullValue();
                    }
                    else
                    {
                        writer.WriteStringValue(link!.Trim());
                    }
                }

                break;

            case BlockFieldKind.RichText:
                {
                    var text = element.ValueKind == JsonValueKind.String ? element.GetString() : null;

                    if (text is null)
                    {
                        Add(errors, path, "This must be text.");
                        writer.WriteNullValue();
                        break;
                    }

                    if (text.Length > field.MaxLength)
                    {
                        Add(errors, path, $"At most {field.MaxLength} characters.");
                    }

                    // Sanitised rather than refused, and the editor is told. Refusing outright would
                    // mean a paste from a word processor being rejected with nothing an editor could
                    // do about it; storing it silently would mean they never learn what was dropped.
                    var clean = HtmlSanitizer.Sanitize(text);

                    if (!string.Equals(clean, text.Trim(), StringComparison.Ordinal))
                    {
                        Add(errors, path, "Some formatting is not allowed here and was removed. Check the copy.");
                    }

                    writer.WriteStringValue(clean);
                }

                break;

            case BlockFieldKind.Html:
                {
                    var html = element.ValueKind == JsonValueKind.String ? element.GetString() : null;

                    if (html is null)
                    {
                        Add(errors, path, "This must be text.");
                        writer.WriteNullValue();
                        break;
                    }

                    if (html.Length > field.MaxLength)
                    {
                        Add(errors, path, $"At most {field.MaxLength} characters.");
                    }

                    // Deliberately unsanitised. This is the escape hatch the custom-HTML permission
                    // and its feature flag exist to guard; sanitising it would make the block useless
                    // and would leave nothing that could carry an embed.
                    writer.WriteStringValue(html);
                }

                break;

            case BlockFieldKind.Choice:
                {
                    var choice = element.ValueKind switch
                    {
                        JsonValueKind.String => element.GetString(),
                        JsonValueKind.Number => element.GetInt32().ToString(CultureInfo.InvariantCulture),
                        _ => null,
                    };

                    if (choice is null || field.Choices is null
                                       || !field.Choices.Contains(choice, StringComparer.Ordinal))
                    {
                        Add(
                            errors,
                            path,
                            $"This must be one of: {string.Join(", ", field.Choices ?? [])}.");
                        writer.WriteNullValue();
                    }
                    else
                    {
                        writer.WriteStringValue(choice);
                    }
                }

                break;

            default:
                {
                    var text = element.ValueKind == JsonValueKind.String ? element.GetString() : null;

                    if (text is null)
                    {
                        Add(errors, path, "This must be text.");
                        writer.WriteNullValue();
                        break;
                    }

                    var trimmed = text.Trim();

                    if (trimmed.Length > field.MaxLength)
                    {
                        Add(errors, path, $"At most {field.MaxLength} characters.");
                    }

                    // Plain text, so any markup in it is text rather than markup. The storefront
                    // renders these as interpolated strings and never as HTML, and this is the second
                    // half of that promise.
                    writer.WriteStringValue(HtmlSanitizer.Sanitize(trimmed).Trim());
                }

                break;
        }
    }

    /// <summary>Validates and writes an id-valued field, recording what it pointed at.</summary>
    private static void WriteReference(
        BlockField field,
        JsonElement element,
        string path,
        Utf8JsonWriter writer,
        Dictionary<string, List<string>> errors,
        List<Guid> media,
        List<Guid> products,
        List<Guid> categories)
    {
        var raw = element.ValueKind == JsonValueKind.String ? element.GetString() : null;

        if (!Guid.TryParse(raw, out var id) || id == Guid.Empty)
        {
            Add(errors, path, "This must be an identifier.");
            writer.WriteNullValue();
            return;
        }

        switch (field.Kind)
        {
            case BlockFieldKind.MediaRef:
                media.Add(id);
                break;

            case BlockFieldKind.ProductRef:
                products.Add(id);
                break;

            default:
                categories.Add(id);
                break;
        }

        writer.WriteStringValue(id);
    }

    /// <summary>Records one field error.</summary>
    private static void Add(Dictionary<string, List<string>> errors, string path, string message)
    {
        if (!errors.TryGetValue(path, out var messages))
        {
            messages = [];
            errors[path] = messages;
        }

        messages.Add(message);
    }
}
