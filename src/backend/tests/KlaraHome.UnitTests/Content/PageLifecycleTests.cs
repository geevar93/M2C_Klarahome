using KlaraHome.Modules.Content.Domain;

namespace KlaraHome.UnitTests.Content;

/// <summary>
/// The editorial workflow's transition table.
/// </summary>
/// <remarks>
/// Tested while writing it under the build sprint's rule 1, which names a state machine's transition
/// table explicitly. The table is read by three callers that must agree — the handler that moves a
/// page, the scheduler that publishes one, and the admin screen that draws the buttons — and every
/// disagreement between them is either a button that does nothing or a page nobody can put live.
/// </remarks>
public sealed class PageLifecycleTests
{
    /// <summary>The ordinary route: written, reviewed, published.</summary>
    /// <remarks>
    /// The states are named as strings rather than as enum values because the enum is internal to the
    /// module: an <c>[InlineData]</c> of them would make this test method's signature more accessible
    /// than its parameter type, which the compiler refuses.
    /// </remarks>
    [Theory]
    [InlineData("Draft", "InReview")]
    [InlineData("InReview", "Published")]
    [InlineData("Published", "Unpublished")]
    [InlineData("Unpublished", "Published")]
    public void The_editorial_route_exists(string from, string to)
        => Assert.True(PageLifecycle.Exists(Status(from), Status(to)));

    /// <summary>An editor may take a page as far as review and no further.</summary>
    /// <remarks>
    /// The single most important property of this table. If it were false the separation between
    /// <c>content.content.manage</c> and <c>content.page.publish</c> would be decoration, and an
    /// agency account could put a page in front of every shopper.
    /// </remarks>
    [Theory]
    [InlineData("Published")]
    [InlineData("Scheduled")]
    [InlineData("Archived")]
    public void An_editor_cannot_publish(string name)
    {
        var to = Status(name);

        Assert.True(PageLifecycle.Exists(PageStatus.Draft, to));
        Assert.False(PageLifecycle.Allows(PageStatus.Draft, to, PageActor.Editor));
        Assert.True(PageLifecycle.Allows(PageStatus.Draft, to, PageActor.Publisher));
    }

    /// <summary>Only the scheduler publishes a scheduled page.</summary>
    /// <remarks>
    /// What makes the scheduled state mean anything. A publisher who wants it live now publishes it
    /// from draft, which is a different edge and a different line in the audit trail.
    /// </remarks>
    [Fact]
    public void Only_the_scheduler_publishes_a_scheduled_page()
    {
        Assert.True(PageLifecycle.Allows(PageStatus.Scheduled, PageStatus.Published, PageActor.System));
        Assert.False(PageLifecycle.Allows(PageStatus.Scheduled, PageStatus.Published, PageActor.Publisher));
        Assert.False(PageLifecycle.Allows(PageStatus.Scheduled, PageStatus.Published, PageActor.Editor));
    }

    /// <summary>A scheduled page is frozen: the only way out of it is back to draft.</summary>
    [Fact]
    public void A_scheduled_page_can_only_go_back_to_draft()
    {
        var next = PageLifecycle.NextFrom(PageStatus.Scheduled, PageActor.Publisher);

        Assert.Equal([PageStatus.Draft], next);
    }

    /// <summary>Nothing happens to an archived page.</summary>
    [Fact]
    public void An_archived_page_is_terminal()
    {
        Assert.True(PageLifecycle.IsTerminal(PageStatus.Archived));
        Assert.Empty(PageLifecycle.NextFrom(PageStatus.Archived, PageActor.Publisher));
    }

    /// <summary>A live page is still editable; a frozen one is not.</summary>
    /// <remarks>
    /// A typo on a legal page is fixed by fixing it, not by taking the page down first. A scheduled
    /// page is the opposite case: it has been approved, and an edit in place would mean the content
    /// that goes live is not the content that was approved.
    /// </remarks>
    [Theory]
    [InlineData("Draft", true)]
    [InlineData("InReview", true)]
    [InlineData("Published", true)]
    [InlineData("Unpublished", true)]
    [InlineData("Scheduled", false)]
    [InlineData("Archived", false)]
    public void Editability_follows_the_workflow(string status, bool editable)
        => Assert.Equal(editable, PageLifecycle.IsEditable(Status(status)));

    /// <summary>Every edge the table has is one an actor can take.</summary>
    /// <remarks>
    /// An edge granted to nobody is unreachable, and an unreachable edge in a table that an admin
    /// screen draws its buttons from is a state a page can enter and never leave.
    /// </remarks>
    [Fact]
    public void No_edge_is_granted_to_nobody()
    {
        foreach (var from in Enum.GetValues<PageStatus>())
        {
            foreach (var to in Enum.GetValues<PageStatus>())
            {
                if (PageLifecycle.Exists(from, to))
                {
                    Assert.True(PageLifecycle.Allows(from, to, PageActor.Anyone), $"{from} -> {to}");
                }
            }
        }
    }

    /// <summary>The state a name refers to.</summary>
    private static PageStatus Status(string name) => Enum.Parse<PageStatus>(name);

    /// <summary>The storefront serves exactly one state.</summary>
    [Fact]
    public void Only_a_published_page_is_live()
    {
        foreach (var status in Enum.GetValues<PageStatus>())
        {
            Assert.Equal(status == PageStatus.Published, PageLifecycle.IsLive(status));
        }
    }
}
