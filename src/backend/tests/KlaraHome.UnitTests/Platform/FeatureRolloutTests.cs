using System.Text.Json;
using KlaraHome.Modules.Platform.Domain;

namespace KlaraHome.UnitTests.Platform;

/// <summary>
/// Rollout evaluation decides who sees an unfinished feature. Every one of these rules exists to
/// keep that answer stable and conservative.
/// </summary>
public sealed class FeatureRolloutTests
{
    private static readonly Guid Alice = new("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b");
    private static readonly Guid Bob = new("0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5c");

    [Fact]
    public void The_default_rollout_reaches_everybody_including_anonymous_callers()
    {
        Assert.True(FeatureRollout.Everyone.Includes("any.flag", userId: null, segment: null));
        Assert.True(FeatureRollout.Everyone.Includes("any.flag", Alice, segment: null));
    }

    [Fact]
    public void A_zero_percent_rollout_reaches_nobody_who_is_not_named()
    {
        var rollout = new FeatureRollout { Percentage = 0 };

        Assert.False(rollout.Includes("any.flag", Alice, null));
        Assert.False(rollout.Includes("any.flag", null, null));
    }

    [Fact]
    public void An_allow_listed_user_is_included_whatever_the_percentage_says()
    {
        var rollout = new FeatureRollout { Percentage = 0, UserIds = [Alice] };

        Assert.True(rollout.Includes("any.flag", Alice, null));
        Assert.False(rollout.Includes("any.flag", Bob, null));
    }

    [Fact]
    public void A_named_segment_is_included_whatever_the_percentage_says()
    {
        var rollout = new FeatureRollout { Percentage = 0, Segments = ["internal"] };

        Assert.True(rollout.Includes("any.flag", Bob, "internal"));
        Assert.True(rollout.Includes("any.flag", Bob, "INTERNAL"));
        Assert.False(rollout.Includes("any.flag", Bob, "customers"));
    }

    [Fact]
    public void A_partial_rollout_excludes_anonymous_callers()
    {
        var rollout = new FeatureRollout { Percentage = 99 };

        // There is no stable identity to bucket an anonymous caller by, so the honest answer is
        // "off" rather than a feature that flickers between page loads.
        Assert.False(rollout.Includes("any.flag", null, null));
    }

    [Fact]
    public void A_users_bucket_is_the_same_on_every_call()
    {
        var first = FeatureRollout.BucketOf("platform.example", Alice);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.Equal(first, FeatureRollout.BucketOf("platform.example", Alice));
        }

        Assert.InRange(first, 0, 99);
    }

    [Fact]
    public void Two_flags_at_the_same_percentage_do_not_select_the_same_users()
    {
        // If the bucket ignored the key, every 10% rollout would land on the same tenth of the user
        // base and nine tenths of users would never see anything.
        var differences = 0;

        for (var index = 0; index < 200; index++)
        {
            var user = Guid.CreateVersion7();

            if (FeatureRollout.BucketOf("flag.one", user) != FeatureRollout.BucketOf("flag.two", user))
            {
                differences++;
            }
        }

        Assert.True(differences > 150, $"Only {differences} of 200 users bucketed differently across two flags.");
    }

    [Fact]
    public void A_rollout_survives_a_round_trip_through_json()
    {
        var rollout = new FeatureRollout
        {
            Percentage = 25,
            UserIds = [Alice, Bob],
            Segments = ["internal", "beta"],
        };

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var restored = JsonSerializer.Deserialize<FeatureRollout>(
            JsonSerializer.Serialize(rollout, options),
            options);

        Assert.NotNull(restored);
        Assert.Equal(25, restored.Percentage);
        Assert.Equal([Alice, Bob], restored.UserIds);
        Assert.Equal(["internal", "beta"], restored.Segments);
    }

    [Fact]
    public void A_disabled_flag_is_off_even_for_an_allow_listed_user()
    {
        var flag = FeatureFlag.Declare(
            "platform.example",
            enabled: false,
            "example",
            new FeatureRollout { UserIds = [Alice] });

        Assert.False(flag.IsEnabledFor(Alice, null));
    }

    [Fact]
    public void Reconfiguring_a_flag_replaces_both_the_switch_and_the_rollout()
    {
        var flag = FeatureFlag.Declare("platform.example", enabled: true, "example");

        flag.Configure(enabled: true, new FeatureRollout { Percentage = 0, UserIds = [Bob] });

        Assert.True(flag.IsEnabledFor(Bob, null));
        Assert.False(flag.IsEnabledFor(Alice, null));
    }
}
