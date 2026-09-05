using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using KlaraHome.SharedKernel.Domain;
using KlaraHome.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KlaraHome.Infrastructure.Persistence;

/// <summary>
/// The database conventions from docs/03-database-design.md §1, applied to every module model
/// from one place. A module configures what is specific to its tables; everything a table has
/// <em>because it is a table in this system</em> is decided here, once.
/// </summary>
/// <remarks>
/// Applied after a module's own <c>OnModelCreating</c>, so an entity can always override a
/// convention deliberately — but never has to restate one.
/// </remarks>
public static class ModelConventions
{
    /// <summary>The concurrency token: PostgreSQL's own row-version system column.</summary>
    public const string ConcurrencyTokenName = "xmin";

    /// <summary>Money is stored as <c>numeric(18,4)</c> — never a float.</summary>
    public const string MoneyColumnType = "numeric(18,4)";

    /// <summary>ISO 4217 alphabetic code, fixed width.</summary>
    public const string CurrencyColumnType = "char(3)";

    /// <summary>Every instant is a UTC <c>timestamptz</c>; the display timezone is a setting.</summary>
    public const string TimestampColumnType = "timestamptz";

    /// <summary>Name of the query filter that hides retired rows.</summary>
    public const string SoftDeleteFilter = "SoftDelete";

    /// <summary>Name of the query filter that confines every query to the ambient tenant.</summary>
    public const string TenantFilter = "Tenant";

    /// <summary>Name of the query filter that confines a vendor caller to their own seller's rows.</summary>
    public const string VendorFilter = "Vendor";

    private static readonly MethodInfo ApplySoftDeleteFilterMethod = typeof(ModelConventions)
        .GetMethod(nameof(ApplySoftDeleteFilter), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ApplyTenantFilterMethod = typeof(ModelConventions)
        .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ApplyVendorFilterMethod = typeof(ModelConventions)
        .GetMethod(nameof(ApplyVendorFilter), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Applies every global convention: the owning schema, optimistic concurrency, tenant
    /// scoping and its index, UTC timestamps, and the soft-delete and tenant query filters.
    /// </summary>
    /// <param name="modelBuilder">The model being built.</param>
    /// <param name="context">The context being configured; supplies the schema and the ambient tenant.</param>
    public static void ApplyKlaraHomeConventions(this ModelBuilder modelBuilder, KlaraHomeDbContext context)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        ArgumentNullException.ThrowIfNull(context);

        modelBuilder.HasDefaultSchema(context.Schema);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // An owned type is part of its owner's table and gets none of this.
            if (entity.IsOwned())
            {
                continue;
            }

            ApplyConcurrencyToken(entity);
            ApplyTenantScoping(modelBuilder, entity, context);
            ApplyVendorScoping(modelBuilder, entity, context);
            ApplyUtcTimestamps(entity);
            ApplySoftDelete(modelBuilder, entity);
        }
    }

    /// <summary>
    /// Maps a <see cref="Money"/> to the two columns the design mandates: an amount at
    /// <c>numeric(18,4)</c> and a sibling <c>*_currency_code</c> defaulted to INR.
    /// </summary>
    /// <remarks>
    /// A complex property rather than an owned entity: money is a value, it has no identity, and
    /// EF must neither track it separately nor give it a table of its own.
    /// </remarks>
    /// <typeparam name="TEntity">The owning entity type.</typeparam>
    /// <param name="builder">The entity being configured.</param>
    /// <param name="property">The money property.</param>
    /// <param name="columnPrefix">Column prefix; defaults to the snake_cased property name.</param>
    public static EntityTypeBuilder<TEntity> HasMoney<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, Money>> property,
        string? columnPrefix = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(property);

        var prefix = columnPrefix ?? SnakeCase(((MemberExpression)property.Body).Member.Name);

        builder.ComplexProperty(property, money =>
        {
            money.Property(value => value.Amount)
                .HasColumnName(prefix + "_amount")
                .HasColumnType(MoneyColumnType);

            money.Property(value => value.Currency)
                .HasColumnName(prefix + "_currency_code")
                .HasColumnType(CurrencyColumnType)
                .HasDefaultValue(Money.Inr);
        });

        return builder;
    }

    /// <summary>
    /// Maps <c>xmin</c> as the concurrency token. PostgreSQL already stamps every row with the
    /// transaction that last wrote it, so optimistic concurrency costs no column, no trigger and
    /// no extra write — the provider recognises a <c>uint</c> row version and reads the system
    /// column instead (docs/01-architecture.md §6).
    /// </summary>
    private static void ApplyConcurrencyToken(IMutableEntityType entity)
    {
        // An append-only table has no lost update to detect, and a partitioned one cannot return a
        // system column at all - see IAppendOnly.
        if (typeof(IAppendOnly).IsAssignableFrom(entity.ClrType))
        {
            return;
        }

        if (entity.FindProperty(ConcurrencyTokenName) is not null)
        {
            return;
        }

        var property = entity.AddProperty(ConcurrencyTokenName, typeof(uint));
        property.IsConcurrencyToken = true;
        property.ValueGenerated = ValueGenerated.OnAddOrUpdate;
        property.SetColumnName(ConcurrencyTokenName);
    }

    /// <summary>
    /// Every tenant-scoped table is indexed on <c>tenant_id</c> and filtered by it, so a handler
    /// that forgets a <c>where tenant_id =</c> cannot read another tenant's rows
    /// (docs/03-database-design.md §1).
    /// </summary>
    private static void ApplyTenantScoping(
        ModelBuilder modelBuilder,
        IMutableEntityType entity,
        KlaraHomeDbContext context)
    {
        if (!typeof(ITenantScoped).IsAssignableFrom(entity.ClrType))
        {
            return;
        }

        var tenantId = entity.FindProperty(nameof(ITenantScoped.TenantId));
        if (tenantId is null)
        {
            return;
        }

        tenantId.IsNullable = false;

        if (entity.FindIndex(new[] { tenantId }) is null)
        {
            entity.AddIndex(tenantId);
        }

        ApplyTenantFilterMethod
            .MakeGenericMethod(entity.ClrType)
            .Invoke(null, [modelBuilder, context]);
    }

    /// <summary>
    /// The filter closes over the context rather than over its tenant id. EF turns a reference to
    /// a context member into a query parameter, so one compiled model serves every tenant and the
    /// value is read per query — capturing the id here would bake the first tenant into the
    /// cached model.
    /// </summary>
    private static void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder, KlaraHomeDbContext context)
        where TEntity : class, ITenantScoped
        => modelBuilder.Entity<TEntity>()
            .HasQueryFilter(TenantFilter, entity => entity.TenantId == context.TenantId);

    /// <summary>
    /// A vendor-scoped table is indexed on <c>vendor_id</c> and filtered by the caller's vendor,
    /// so a vendor user cannot read another seller's rows even from a query that forgot to say so
    /// (docs/07-security-compliance.md §2).
    /// </summary>
    private static void ApplyVendorScoping(
        ModelBuilder modelBuilder,
        IMutableEntityType entity,
        KlaraHomeDbContext context)
    {
        if (!typeof(IVendorScoped).IsAssignableFrom(entity.ClrType))
        {
            return;
        }

        var vendorId = entity.FindProperty(nameof(IVendorScoped.VendorId));
        if (vendorId is null)
        {
            return;
        }

        if (entity.FindIndex(new[] { vendorId }) is null)
        {
            entity.AddIndex(vendorId);
        }

        ApplyVendorFilterMethod
            .MakeGenericMethod(entity.ClrType)
            .Invoke(null, [modelBuilder, context]);
    }

    /// <summary>
    /// Open for a caller with no vendor scope, closed for one with it. Platform staff and
    /// background work see every seller's rows — that is what makes them platform-wide — while a
    /// vendor user sees exactly their own.
    /// </summary>
    /// <remarks>
    /// Like the tenant filter this closes over the context rather than the id, so the value is a
    /// query parameter read per request instead of being baked into the cached model. The null
    /// check is on the <em>caller's</em> scope, never on the row's: a row with no vendor belongs
    /// to the platform, and a vendor user has no business seeing it either.
    /// </remarks>
    private static void ApplyVendorFilter<TEntity>(ModelBuilder modelBuilder, KlaraHomeDbContext context)
        where TEntity : class, IVendorScoped
        => modelBuilder.Entity<TEntity>()
            .HasQueryFilter(
                VendorFilter,
                entity => context.VendorId == null || entity.VendorId == context.VendorId);

    /// <summary>
    /// <c>timestamptz</c> for every instant. Postgres normalises it to UTC and Npgsql round-trips
    /// a <see cref="DateTimeOffset"/> without a local-time detour, so "when did this happen" has
    /// one answer regardless of where the query ran.
    /// </summary>
    private static void ApplyUtcTimestamps(IMutableEntityType entity)
    {
        foreach (var property in entity.GetProperties())
        {
            var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

            if (type == typeof(DateTimeOffset) || type == typeof(DateTime))
            {
                property.SetColumnType(TimestampColumnType);
            }
        }
    }

    private static void ApplySoftDelete(ModelBuilder modelBuilder, IMutableEntityType entity)
    {
        if (!typeof(ISoftDeletable).IsAssignableFrom(entity.ClrType))
        {
            return;
        }

        ApplySoftDeleteFilterMethod
            .MakeGenericMethod(entity.ClrType)
            .Invoke(null, [modelBuilder]);
    }

    /// <summary>
    /// Expressed against the entity's own CLR type rather than assembled from metadata, so EF can
    /// translate it and the generated SQL carries the predicate a developer would have written.
    /// </summary>
    private static void ApplySoftDeleteFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ISoftDeletable
        => modelBuilder.Entity<TEntity>()
            .HasQueryFilter(SoftDeleteFilter, entity => entity.DeletedAt == null);

    /// <summary>
    /// Mirrors the naming plugin for the one place a column name is generated by hand, so
    /// <c>SellingPrice</c> yields <c>selling_price_amount</c> rather than a name only this method
    /// would produce.
    /// </summary>
    /// <param name="name">The CLR member name.</param>
    internal static string SnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);

        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];

            if (char.IsUpper(character) && index > 0 && !char.IsUpper(name[index - 1]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
