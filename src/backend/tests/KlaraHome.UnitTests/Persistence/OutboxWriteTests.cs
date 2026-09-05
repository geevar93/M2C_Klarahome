using System.Text.Json;
using KlaraHome.Contracts.IntegrationEvents;
using KlaraHome.Infrastructure.Persistence.Outbox;

namespace KlaraHome.UnitTests.Persistence;

/// <summary>A contract used only by the outbox tests.</summary>
internal sealed record ThingHappened(Guid ThingId, string Note) : IntegrationEvent;

/// <summary>
/// The outbox's serialisation format is a durable contract: a message written by one deployment is
/// read by the next one. These tests pin the parts of it that a refactor could silently change.
/// </summary>
public sealed class OutboxSerializationTests
{
    [Fact]
    public void The_stored_type_name_carries_no_assembly_or_version()
    {
        var name = OutboxSerialization.NameOf(typeof(ThingHappened));

        Assert.Equal("KlaraHome.UnitTests.Persistence.ThingHappened", name);
        Assert.DoesNotContain("Version=", name, StringComparison.Ordinal);
        Assert.DoesNotContain("Culture=", name, StringComparison.Ordinal);
    }

    [Fact]
    public void A_payload_round_trips_through_the_shared_options()
    {
        var published = new ThingHappened(Guid.Parse("22222222-2222-2222-2222-222222222222"), "note")
        {
            CorrelationId = "abc",
        };

        // Serialised through a runtime Type, exactly as the outbox writer does: the caller hands it
        // an IIntegrationEvent and the concrete type is only known at run time.
        var json = JsonSerializer.Serialize(published, published.GetType(), OutboxSerialization.Options);
        var restored = JsonSerializer.Deserialize<ThingHappened>(json, OutboxSerialization.Options);

        Assert.Equal(published, restored);
    }

    [Fact]
    public void Property_names_stay_as_declared()
    {
        // If one side of the queue camel-cases and the other does not, every field deserialises as
        // its default and the failure is silent. Pinning the casing is what stops that.
        var json = JsonSerializer.Serialize(
            new ThingHappened(Guid.Empty, "n"),
            OutboxSerialization.Options);

        Assert.Contains("\"ThingId\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"thingId\"", json, StringComparison.Ordinal);
    }
}

/// <summary>
/// The dispatcher resolves a handler from the name stored on the row. If that lookup silently
/// misses, messages are marked processed and the events are lost.
/// </summary>
public sealed class IntegrationEventTypeMapTests
{
    [Fact]
    public void A_contract_type_resolves_from_its_stored_name()
    {
        var map = new IntegrationEventTypeMap([typeof(ThingHappened).Assembly]);

        Assert.True(map.TryResolve(OutboxSerialization.NameOf(typeof(ThingHappened)), out var resolved));
        Assert.Equal(typeof(ThingHappened), resolved);
    }

    [Fact]
    public void An_unknown_name_does_not_resolve()
    {
        var map = new IntegrationEventTypeMap([typeof(ThingHappened).Assembly]);

        Assert.False(map.TryResolve("KlaraHome.Contracts.IntegrationEvents.NoSuchEvent", out _));
    }

    [Fact]
    public void The_abstract_base_is_not_registered_as_a_publishable_event()
    {
        var map = new IntegrationEventTypeMap([typeof(IntegrationEvent).Assembly]);

        Assert.False(map.TryResolve(OutboxSerialization.NameOf(typeof(IntegrationEvent)), out _));
    }
}
