using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Modules.Content.Domain;
using KlaraHome.Modules.Content.Endpoints;

namespace KlaraHome.Modules.Content.Infrastructure;

/// <summary>
/// Answers "who is asking, and what may they do to a page".
/// </summary>
/// <remarks>
/// <para>
/// One surface rather than the three the returns module has, because content has one audience: the
/// people who run the store. A shopper never transitions a page and a seller never writes one, so the
/// only question left is the one the editorial workflow exists to answer — may this person put
/// something in front of a shopper, or only write it.
/// </para>
/// <para>
/// <see cref="PageActor.System"/> is deliberately unreachable from here. It is what the scheduler
/// transitions as, and no HTTP caller may claim it — which is what makes "only the clock publishes a
/// scheduled page" a property of the machine rather than a convention.
/// </para>
/// </remarks>
/// <param name="caller">The signed-in caller.</param>
internal sealed class ContentScope(ICallerContext caller)
{
    /// <summary>The user to attribute a save, a publish or a rollback to.</summary>
    public Guid? ActorId => caller.UserId;

    /// <summary>Whether the caller may put a page in front of a shopper.</summary>
    public bool CanPublish => caller.HasPermission(ContentPermissions.ContentPublish);

    /// <summary>Whether the caller may write a custom-HTML block.</summary>
    /// <remarks>
    /// Necessary and not sufficient: the <c>content.custom-html</c> flag has to be on as well, and
    /// the handler checks both. The permission answers "who", and the flag answers "in this
    /// deployment, at all".
    /// </remarks>
    public bool CanWriteCustomHtml => caller.HasPermission(ContentPermissions.CustomHtmlWrite);

    /// <summary>
    /// Who the transition table should treat this caller as.
    /// </summary>
    /// <remarks>
    /// A publisher if they hold the publish permission, an editor otherwise. Both are richer than
    /// nothing: an editor holding neither would not have reached the endpoint, which already requires
    /// <see cref="ContentPermissions.ContentManage"/>.
    /// </remarks>
    public PageActor Actor => CanPublish ? PageActor.Publisher : PageActor.Editor;
}
