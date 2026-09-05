using KlaraHome.SharedKernel.Primitives;
using KlaraHome.Testing.Persistence;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.IntegrationTests.Persistence;

/// <summary>
/// What the conventions actually do, asserted against PostgreSQL. The unit tests pin the shape of
/// the model; these pin the behaviour that shape produces, which is the part that can be right in
/// metadata and wrong in the database.
/// </summary>
[Collection(PostgresDatabase.CollectionName)]
public sealed class ConventionBehaviourTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    // Each test gets its own tenant, so the tenant filter isolates them from each other and the
    // suite can run in any order without a shared-database cleanup step.
    private readonly Guid _tenant = Guid.CreateVersion7();

    public async ValueTask InitializeAsync()
    {
        Assert.SkipWhen(postgres.SkipReason is not null, postgres.SkipReason ?? string.Empty);

        await using var context = postgres.CreateContext(_tenant);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Audit_columns_populate_on_insert_without_the_caller_setting_them()
    {
        var actor = Guid.CreateVersion7();
        var clock = new FixedClock(Noon);

        await using (var writer = postgres.CreateContext(_tenant, actor, clock))
        {
            // Note what is *not* set here: tenant, created_at and created_by. That is the point.
            writer.AuditedThings.Add(new AuditedThing { DisplayName = "kettle" });
            await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = postgres.CreateContext(_tenant);
        var saved = await reader.AuditedThings.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(_tenant, saved.TenantId);
        Assert.Equal(Noon, saved.CreatedAt);
        Assert.Equal(actor, saved.CreatedBy);
        Assert.Null(saved.UpdatedAt);
        Assert.Null(saved.UpdatedBy);
    }

    [Fact]
    public async Task An_update_stamps_the_updater_and_never_rewrites_the_creator()
    {
        var creator = Guid.CreateVersion7();
        var editor = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();

        await using (var writer = postgres.CreateContext(_tenant, creator, new FixedClock(Noon)))
        {
            writer.AuditedThings.Add(new AuditedThing { Id = id, DisplayName = "lamp" });
            await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var editorContext = postgres.CreateContext(_tenant, editor, new FixedClock(Noon.AddHours(3))))
        {
            var thing = await editorContext.AuditedThings.SingleAsync(
                candidate => candidate.Id == id,
                TestContext.Current.CancellationToken);

            thing.DisplayName = "desk lamp";
            // A client that echoes created_at back must not be able to rewrite history.
            thing.CreatedAt = DateTimeOffset.UnixEpoch;
            thing.CreatedBy = editor;

            await editorContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = postgres.CreateContext(_tenant);
        var saved = await reader.AuditedThings.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(Noon.AddHours(3), saved.UpdatedAt);
        Assert.Equal(editor, saved.UpdatedBy);
        Assert.Equal(Noon, saved.CreatedAt);
        Assert.Equal(creator, saved.CreatedBy);
    }

    [Fact]
    public async Task Removing_a_soft_deletable_row_retires_it_instead_of_deleting_it()
    {
        var id = Guid.CreateVersion7();
        var actor = Guid.CreateVersion7();

        await using (var writer = postgres.CreateContext(_tenant))
        {
            writer.AuditedThings.Add(new AuditedThing { Id = id, DisplayName = "mug" });
            await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var remover = postgres.CreateContext(_tenant, actor, new FixedClock(Noon)))
        {
            var thing = await remover.AuditedThings.SingleAsync(TestContext.Current.CancellationToken);
            remover.AuditedThings.Remove(thing);
            await remover.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = postgres.CreateContext(_tenant);

        // Invisible to ordinary queries...
        Assert.Empty(await reader.AuditedThings.ToListAsync(TestContext.Current.CancellationToken));

        // ...but still there, with who retired it and when.
        var retired = await reader.AuditedThings
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == id, TestContext.Current.CancellationToken);

        Assert.Equal(Noon, retired.DeletedAt);
        Assert.Equal(actor, retired.DeletedBy);
    }

    [Fact]
    public async Task A_query_cannot_see_another_tenants_rows()
    {
        var otherTenant = Guid.CreateVersion7();

        await using (var ours = postgres.CreateContext(_tenant))
        {
            ours.AuditedThings.Add(new AuditedThing { DisplayName = "ours" });
            await ours.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var theirs = postgres.CreateContext(otherTenant))
        {
            theirs.AuditedThings.Add(new AuditedThing { DisplayName = "theirs" });
            await theirs.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = postgres.CreateContext(_tenant);
        var visible = await reader.AuditedThings.ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal("ours", Assert.Single(visible).DisplayName);
    }

    [Fact]
    public async Task A_row_cannot_be_moved_between_tenants_by_an_update()
    {
        var id = Guid.CreateVersion7();

        await using (var writer = postgres.CreateContext(_tenant))
        {
            writer.AuditedThings.Add(new AuditedThing { Id = id, DisplayName = "fixed" });
            await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using (var attacker = postgres.CreateContext(_tenant))
        {
            var thing = await attacker.AuditedThings.SingleAsync(TestContext.Current.CancellationToken);
            thing.TenantId = Guid.CreateVersion7();
            thing.DisplayName = "moved";
            await attacker.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = postgres.CreateContext(_tenant);
        var saved = await reader.AuditedThings.SingleAsync(TestContext.Current.CancellationToken);

        // The name change lands; the tenant change does not. Moving a row between tenants is not
        // an update, it is a data leak.
        Assert.Equal("moved", saved.DisplayName);
        Assert.Equal(_tenant, saved.TenantId);
    }

    [Fact]
    public async Task A_concurrent_update_is_refused_by_the_xmin_token()
    {
        var id = Guid.CreateVersion7();

        await using (var writer = postgres.CreateContext(_tenant))
        {
            writer.AuditedThings.Add(new AuditedThing { Id = id, DisplayName = "contested" });
            await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var first = postgres.CreateContext(_tenant);
        await using var second = postgres.CreateContext(_tenant);

        var mine = await first.AuditedThings.SingleAsync(TestContext.Current.CancellationToken);
        var theirs = await second.AuditedThings.SingleAsync(TestContext.Current.CancellationToken);

        mine.DisplayName = "mine";
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        theirs.DisplayName = "theirs";

        // The second writer read before the first wrote, so its xmin is stale. Without this the
        // later write would silently discard the earlier one.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => second.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Money_round_trips_at_four_decimal_places_with_its_currency()
    {
        await using (var writer = postgres.CreateContext(_tenant))
        {
            writer.AuditedThings.Add(new AuditedThing
            {
                DisplayName = "priced",
                SellingPrice = Money.Rupees(1234.56789m),
            });
            await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = postgres.CreateContext(_tenant);
        var saved = await reader.AuditedThings.SingleAsync(TestContext.Current.CancellationToken);

        // Money rounds to scale 4 on construction; the column stores exactly that, with no
        // binary-floating-point drift on the way through.
        Assert.Equal(1234.5679m, saved.SellingPrice.Amount);
        Assert.Equal(Money.Inr, saved.SellingPrice.Currency);
    }

    [Fact]
    public async Task Money_survives_a_value_no_double_could_hold()
    {
        await using (var writer = postgres.CreateContext(_tenant))
        {
            writer.AuditedThings.Add(new AuditedThing
            {
                DisplayName = "large",
                SellingPrice = Money.Rupees(99_999_999_999_999.0001m),
            });
            await writer.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = postgres.CreateContext(_tenant);
        var saved = await reader.AuditedThings.SingleAsync(TestContext.Current.CancellationToken);

        Assert.Equal(99_999_999_999_999.0001m, saved.SellingPrice.Amount);
    }

    [Fact]
    public async Task Identifiers_in_the_database_are_snake_case()
    {
        await using var context = postgres.CreateContext(_tenant);

        var columns = await context.Database
            .SqlQuery<string>($"""
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_schema = {ConventionsDbContext.SchemaName} AND table_name = 'audited_things'
                """)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Contains("display_name", columns);
        Assert.Contains("selling_price_amount", columns);
        Assert.Contains("selling_price_currency_code", columns);
        Assert.DoesNotContain(columns, column => column.Any(char.IsUpper));
    }
}
