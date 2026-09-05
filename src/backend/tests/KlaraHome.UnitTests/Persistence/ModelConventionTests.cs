using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Outbox;
using KlaraHome.SharedKernel.Primitives;
using KlaraHome.Testing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace KlaraHome.UnitTests.Persistence;

/// <summary>
/// Asserts the database conventions from docs/03-database-design.md §1 against the built EF model.
/// </summary>
/// <remarks>
/// The model is built with the real Npgsql provider but no connection is ever opened: EF resolves
/// column types, names, indexes and filters without touching a server. That makes these assertions
/// exact and instant, and leaves the database tests free to prove the behaviour that only a real
/// database can — which they do in KlaraHome.IntegrationTests.
/// </remarks>
public sealed class ModelConventionTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static IModel BuildModel()
    {
        using var context = new ConventionsDbContext(
            ConventionsContextFactory.Options("Host=unused"),
            new FixedTenantContext(Tenant));

        return context.Model;
    }

    private static IEntityType Entity<TEntity>() => BuildModel().FindEntityType(typeof(TEntity))!;

    [Fact]
    public void Every_table_lands_in_the_schema_the_context_owns()
    {
        var model = BuildModel();

        Assert.Equal(ConventionsDbContext.SchemaName, model.GetDefaultSchema());
        Assert.Equal(ConventionsDbContext.SchemaName, Entity<AuditedThing>().GetSchema());
    }

    [Fact]
    public void The_messaging_tables_stay_in_the_platform_schema_whatever_context_maps_them()
    {
        // Every module context maps the outbox so it can write to it in its own transaction. If
        // the table followed the context's schema, each module would get a private queue and the
        // dispatcher would drain exactly one of them.
        Assert.Equal(MessagingTables.Schema, Entity<OutboxMessage>().GetSchema());
        Assert.Equal(MessagingTables.Schema, Entity<InboxMessage>().GetSchema());
    }

    [Theory]
    [InlineData(typeof(AuditedThing), "audited_things")]
    [InlineData(typeof(PlainThing), "plain_things")]
    [InlineData(typeof(OutboxMessage), "outbox_messages")]
    public void Table_names_are_snake_case_and_plural(Type entityType, string expected)
        => Assert.Equal(expected, BuildModel().FindEntityType(entityType)!.GetTableName());

    [Theory]
    [InlineData("DisplayName", "display_name")]
    [InlineData("TenantId", "tenant_id")]
    [InlineData("CreatedAt", "created_at")]
    [InlineData("DeletedBy", "deleted_by")]
    public void Column_names_are_snake_case(string property, string expected)
    {
        var entity = Entity<AuditedThing>();
        var column = entity.FindProperty(property)!
            .GetColumnName(StoreObjectIdentifier.Create(entity, StoreObjectType.Table)!.Value);

        Assert.Equal(expected, column);
    }

    [Fact]
    public void Money_maps_to_an_amount_and_a_currency_code()
    {
        var money = Entity<AuditedThing>().FindComplexProperty(nameof(AuditedThing.SellingPrice))!.ComplexType;
        var table = StoreObjectIdentifier.Create(Entity<AuditedThing>(), StoreObjectType.Table)!.Value;

        var amount = money.FindProperty(nameof(Money.Amount))!;
        var currency = money.FindProperty(nameof(Money.Currency))!;

        Assert.Equal("selling_price_amount", amount.GetColumnName(table));
        Assert.Equal(ModelConventions.MoneyColumnType, amount.GetColumnType());

        Assert.Equal("selling_price_currency_code", currency.GetColumnName(table));
        Assert.Equal(ModelConventions.CurrencyColumnType, currency.GetColumnType());
        Assert.Equal(Money.Inr, currency.GetDefaultValue());
    }

    [Fact]
    public void Money_is_never_stored_as_a_floating_point_type()
    {
        // The rule that matters is not "numeric(18,4)" but "not a float": a rounding error in a
        // price is an accounting problem, not a display problem.
        var money = Entity<AuditedThing>().FindComplexProperty(nameof(AuditedThing.SellingPrice))!.ComplexType;
        var amount = money.FindProperty(nameof(Money.Amount))!;

        Assert.Equal(typeof(decimal), amount.ClrType);
        Assert.DoesNotContain("double", amount.GetColumnType(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("real", amount.GetColumnType(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(typeof(AuditedThing))]
    [InlineData(typeof(PlainThing))]
    [InlineData(typeof(OutboxMessage))]
    public void Every_entity_gets_the_xmin_concurrency_token(Type entityType)
    {
        var property = BuildModel().FindEntityType(entityType)!.FindProperty(ModelConventions.ConcurrencyTokenName)!;

        Assert.True(property.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        Assert.Equal(typeof(uint), property.ClrType);
    }

    [Fact]
    public void Timestamps_are_timestamptz_so_there_is_one_answer_to_when()
    {
        var entity = Entity<AuditedThing>();

        foreach (var name in new[] { "CreatedAt", "UpdatedAt", "DeletedAt" })
        {
            Assert.Equal(ModelConventions.TimestampColumnType, entity.FindProperty(name)!.GetColumnType());
        }
    }

    [Fact]
    public void A_tenant_scoped_entity_is_indexed_and_filtered_by_tenant()
    {
        var entity = Entity<AuditedThing>();

        Assert.False(entity.FindProperty(nameof(AuditedThing.TenantId))!.IsNullable);
        Assert.Contains(
            entity.GetIndexes(),
            index => index.Properties.Count == 1
                     && index.Properties[0].Name == nameof(AuditedThing.TenantId));
        Assert.Contains(entity.GetDeclaredQueryFilters(), filter => filter.Key == ModelConventions.TenantFilter);
    }

    [Fact]
    public void A_soft_deletable_entity_is_filtered_to_live_rows()
        => Assert.Contains(
            Entity<AuditedThing>().GetDeclaredQueryFilters(),
            filter => filter.Key == ModelConventions.SoftDeleteFilter);

    [Fact]
    public void An_entity_that_opts_into_nothing_gets_no_filters()
    {
        // The conventions must be opt-in. An entity silently acquiring a tenant filter it has no
        // column for would fail at query time, far from the cause.
        Assert.Empty(Entity<PlainThing>().GetDeclaredQueryFilters());
    }

    [Fact]
    public void The_outbox_poll_is_backed_by_a_partial_index()
    {
        var index = Entity<OutboxMessage>()
            .GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == "ix_outbox_messages_pending");

        Assert.Equal(nameof(OutboxMessage.OccurredAt), index.Properties.Single().Name);
        Assert.Equal("processed_at IS NULL", index.GetFilter());
    }

    [Fact]
    public void The_inbox_key_is_the_message_and_the_handler()
    {
        var key = Entity<InboxMessage>().FindPrimaryKey()!;

        Assert.Equal(
            [nameof(InboxMessage.MessageId), nameof(InboxMessage.Handler)],
            key.Properties.Select(property => property.Name));
    }

    [Fact]
    public void Each_context_keeps_its_migration_history_in_its_own_schema()
    {
        // Sharing one history table across modules would let the first migrator to run convince
        // every other module that it was already up to date.
        using var context = new ConventionsDbContext(
            ConventionsContextFactory.Options("Host=unused"),
            new FixedTenantContext(Tenant));

        var history = context.GetService<Microsoft.EntityFrameworkCore.Migrations.IHistoryRepository>();

        Assert.Contains(PersistenceExtensions.HistoryTableName, history.GetCreateScript(), StringComparison.Ordinal);
        Assert.Contains(ConventionsDbContext.SchemaName, history.GetCreateScript(), StringComparison.Ordinal);
    }
}
