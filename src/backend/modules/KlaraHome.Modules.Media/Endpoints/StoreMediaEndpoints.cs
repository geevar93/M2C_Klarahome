using KlaraHome.Contracts.Media;
using KlaraHome.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace KlaraHome.Modules.Media.Endpoints;

/// <summary>
/// The one public media route: a stable, unsigned address for an image the storefront only knows
/// by file id.
/// </summary>
/// <remarks>
/// <para>
/// Every catalogue projection — the listing card, the cart line, the search suggestion, the
/// recently-viewed strip — carries a <c>fileId</c> and not a URL, because the row is denormalised
/// for speed and does not join the media registry. Something has to turn that id into bytes, and
/// the only component that knows the object key, the imgproxy signature and whether the file is
/// still servable is this module. Before this route existed the Angular apps guessed a
/// <c>/{width}x/{fileId}</c> path that no service ever served, and every id-only image was broken
/// on any deployment that configured an image host.
/// </para>
/// <para>
/// It answers with a redirect rather than the bytes. Proxying the object store through the API
/// would put every product photograph through the application tier; a 302 costs one small round
/// trip and the browser then talks to the CDN directly. The redirect is cacheable for a day: the
/// file id is immutable and a rendition URL for it changes only when the deployment's imgproxy
/// configuration does.
/// </para>
/// </remarks>
internal static class StoreMediaEndpoints
{
    /// <summary>How long a browser may reuse the redirect without asking again.</summary>
    private static readonly TimeSpan RedirectLifetime = TimeSpan.FromDays(1);

    public static IEndpointRouteBuilder MapStoreMediaEndpoints(this IEndpointRouteBuilder store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var group = store
            .MapGroup("/media")
            .WithTags("Media")
            .RequireRateLimiting(RateLimitPolicies.StorefrontRead);

        group.MapGet("/{id:guid}/image", async (
                Guid id,
                int? width,
                IMediaLibrary library,
                HttpContext context) =>
            {
                var file = await library.GetAsync(id, context.RequestAborted).ConfigureAwait(false);

                // A private file has no public URL by design (MediaLibrary.Project), and a deleted
                // or unscanned one has none yet. Either way there is nothing honest to send the
                // browser to, and a 404 is what lets <img onerror> fall back to its placeholder.
                if (file is null || file.Url is null)
                {
                    return Results.NotFound();
                }

                context.Response.Headers[HeaderNames.CacheControl] =
                    $"public, max-age={(int)RedirectLifetime.TotalSeconds}";

                return Results.Redirect(Pick(file, width), permanent: false);
            })
            .WithName("storeMediaImage")
            .WithSummary("Redirects to the rendition of a public image nearest the requested width.")
            .AllowAnonymous()
            .Produces(StatusCodes.Status302Found)
            .Produces(StatusCodes.Status404NotFound);

        return store;
    }

    /// <summary>
    /// The smallest rendition at least as wide as asked for, the widest one when none is, and the
    /// original when there are no renditions at all (no imgproxy, or not a raster image).
    /// </summary>
    private static string Pick(MediaFile file, int? width)
    {
        if (file.Variants.Count == 0)
        {
            return file.Url!;
        }

        if (width is null or <= 0)
        {
            return file.Variants[^1].Url;
        }

        foreach (var variant in file.Variants)
        {
            if (variant.Width >= width)
            {
                return variant.Url;
            }
        }

        return file.Variants[^1].Url;
    }
}
