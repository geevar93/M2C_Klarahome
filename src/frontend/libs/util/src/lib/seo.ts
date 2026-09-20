import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { NavigationStart, Router } from '@angular/router';

/**
 * Title, meta, canonical and JSON-LD — the whole indexable surface of a page, in one place.
 *
 * Two properties matter more than the API shape:
 *
 *  1. **It runs on the server.** Everything here writes into the document Angular is rendering,
 *     so the tags are in the HTML a crawler receives rather than added by script afterwards. A
 *     title set on the client is a title a crawler may or may not wait for; one set during SSR is
 *     simply there.
 *  2. **It removes what it wrote.** A single-page app navigating from a product to a category
 *     keeps the previous page's tags unless somebody clears them, and a stale canonical is worse
 *     than no canonical — it tells a crawler two different URLs are the same page.
 *
 * Site-wide values (the title template, the canonical origin, whether indexing is allowed at all)
 * come from the API's SEO configuration and are installed once by the shell with `configure`.
 */

/** The site-wide half, read from `GET /store/content/seo-config` (Step 20). */
export interface SeoSiteConfig {
  /** Absolute origin every canonical is resolved against — `https://klarahome.example`. */
  readonly canonicalBaseUrl: string;
  /**
   * Whether this deployment may be indexed at all. **False on every non-production environment**,
   * and it wins over any per-page value: a staging site that ranks is a real incident.
   */
  readonly allowIndexing: boolean;
  /**
   * `{title} | {store}`. Both tokens are substituted — the contract that ships this value says so
   * (`SeoSettings.TitleTemplate`), and a token the client does not know is a token a shopper reads
   * in their browser tab.
   */
  readonly titleTemplate: string;
  /** The store's name, for the `{store}` token. Comes from the branding settings, not from SEO. */
  readonly storeName: string;
  /** Used when a page supplies no description of its own. */
  readonly defaultMetaDescription: string;
  /**
   * The branding tagline, the last resort when neither the page nor the SEO settings describe it.
   * One sentence the store wrote about itself beats a search result with no snippet and a share
   * card with no description line.
   */
  readonly storeTagline: string;
  /** `summary_large_image` unless the store says otherwise. */
  readonly twitterCardType: string;
}

/** The per-page half. Everything is optional; what is absent falls back to the site config. */
export interface SeoMetadata {
  /** The page title, before the template is applied. */
  readonly title?: string;
  readonly description?: string;
  /**
   * The canonical path — `/p/teak-coffee-table`, not a full URL. Resolved against the configured
   * origin, so one built image serves two hostnames without either being baked into a page.
   */
  readonly canonicalPath?: string;
  /** Overrides the computed robots directive. Faceted URLs are the reason it exists. */
  readonly robots?: string;
  /** Absolute URL of the sharing image. */
  readonly imageUrl?: string;
  /** `website`, `product`, `article`. Defaults to `website`. */
  readonly ogType?: string;
  /** True for a page that may be crawled but must not be indexed — a filtered PLP, a search result. */
  readonly noIndex?: boolean;
}

const DEFAULT_SITE_CONFIG: SeoSiteConfig = {
  canonicalBaseUrl: '',
  allowIndexing: false,
  titleTemplate: '{title}',
  storeName: '',
  defaultMetaDescription: '',
  storeTagline: '',
  twitterCardType: 'summary_large_image',
};

/** The attribute every tag this service owns is marked with, so it can find its own again. */
const OWNED = 'data-kh-seo';

/**
 * The site-wide fallback share image, used whenever a page supplies none of its own — most pages
 * that are not a product or a vendor. `apps/storefront/public/brand/og-default.png`, 1200x630,
 * generated from `og-default.svg` (docs/steps/step-30-design-system-theming-and-visual-identity.md,
 * brand assets). A path, not a hardcoded host, so it resolves against whichever origin the
 * deployment's `canonicalBaseUrl` names.
 */
const DEFAULT_OG_IMAGE_PATH = '/brand/og-default.png';

/** Open Graph's spelling of the document language `index.html` declares. */
const OG_LOCALE = 'en_IN';

@Injectable({ providedIn: 'root' })
export class SeoService {
  private readonly document = inject(DOCUMENT);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly router = inject(Router, { optional: true });

  /** Whether a page has set its own title since the current navigation began. */
  private pageTitled = false;
  /** The route's static title for the current page, kept so a late store config can re-format it. */
  private routeTitle: string | null = null;

  private site: SeoSiteConfig = DEFAULT_SITE_CONFIG;

  /**
   * The last metadata applied, so a configuration that arrives afterwards can be honoured.
   *
   * The shell's configuration and the page's own metadata are two independent round trips, and
   * nothing orders them: a page that renders first would otherwise keep a title built from the
   * defaults for the rest of its life, which during server rendering means it ships that way.
   */
  private applied: SeoMetadata | null = null;

  /**
   * Installs site-wide configuration, merging onto what is already there.
   *
   * It merges rather than replaces because the two halves arrive from two endpoints: the template
   * and the canonical origin come from the SEO configuration, and the store name comes from the
   * branding settings. Whichever answers second must not erase the first.
   */
  configure(site: Partial<SeoSiteConfig>): void {
    this.site = { ...this.site, ...site };

    if (this.applied) {
      this.apply(this.applied);
    }
    if (!this.pageTitled) this.setRouteTitle(this.routeTitle);
  }

  /** The configured origin, for a caller that needs to build an absolute URL of its own. */
  /** The store's name, for a title that has nothing else to say. */
  get storeName(): string {
    return this.site.storeName || 'Klara Home';
  }

  get canonicalBaseUrl(): string {
    return this.site.canonicalBaseUrl.replace(/\/+$/, '');
  }

  /**
   * Applies one page's metadata, replacing whatever the previous page left behind.
   *
   * `noindex` is decided here rather than by the caller: a page that asks to be indexed on a
   * deployment that may not be indexed does not get its way.
   */
  constructor() {
    this.router?.events.subscribe((event) => {
      if (event instanceof NavigationStart) this.pageTitled = false;
    });
  }

  /**
   * Whether a page has named itself since the current navigation began.
   *
   * A page with resolvers is constructed *during* activation, and the router runs its
   * `TitleStrategy` only after `NavigationEnd` — so a product that set its own name as the title
   * would have it overwritten by the route's static "Product" a moment later. The strategy asks
   * this first and stands down; a route whose page never named itself keeps the static title.
   */
  pageOwnsCurrentTitle(): boolean {
    return this.pageTitled;
  }

  /**
   * The route's own title, as the router's `TitleStrategy` hands it over.
   *
   * Written through here rather than straight to `Title` so it is formatted with the store's
   * template and, like a page's own metadata, re-formatted when the store configuration arrives
   * after the first navigation — otherwise the first page of a visit reads "Cart" while every
   * page after it reads "Cart | Klara Home".
   */
  setRouteTitle(title: string | null): void {
    this.routeTitle = title;
    this.title.setTitle(title ? this.format(title) : this.storeName);
  }

  apply(metadata: SeoMetadata): void {
    this.applied = metadata;

    const pageTitle = metadata.title?.trim() ?? '';
    // A blank title is not written: the document's title is `AppTitleStrategy`'s to set as the
    // router activates a route, and a page that calls `apply()` for its description or canonical
    // without a `title` of its own — most of them, now that the route itself supplies the fallback
    // — must not blank out what the strategy just set.
    if (pageTitle) {
      this.title.setTitle(this.format(pageTitle));
      this.pageTitled = true;
    }

    const description =
      metadata.description?.trim() || this.site.defaultMetaDescription || this.site.storeTagline;
    this.setTag('name', 'description', description);

    const robots = !this.site.allowIndexing
      ? 'noindex, nofollow'
      : (metadata.robots ?? (metadata.noIndex ? 'noindex, follow' : 'index, follow'));
    this.setTag('name', 'robots', robots);

    this.setCanonical(metadata.canonicalPath);

    // Open Graph and Twitter, written together: a card with a title and no image reads as broken
    // in every messaging app in India, which is where a product link is actually shared.
    this.setTag('property', 'og:title', pageTitle);
    this.setTag('property', 'og:description', description);
    this.setTag('property', 'og:type', metadata.ogType ?? 'website');
    this.setTag('property', 'og:url', this.absolute(metadata.canonicalPath));
    this.setTag('property', 'og:image', metadata.imageUrl ?? this.absolute(DEFAULT_OG_IMAGE_PATH));
    // The two optional ones a share card reads to say whose link it is and in what language. The
    // locale matches `<html lang="en-IN">`, in the underscore form Open Graph uses.
    this.setTag('property', 'og:site_name', this.site.storeName);
    this.setTag('property', 'og:locale', OG_LOCALE);
    this.setTag('name', 'twitter:card', this.site.twitterCardType);
  }

  /**
   * Adds or replaces one JSON-LD document, keyed so a page can carry several.
   *
   * Keyed rather than appended, because the shell writes `Organization`, `WebSite` and
   * `BreadcrumbList` on every navigation and appending would leave a crawler reading four
   * breadcrumb trails on the fourth page of a session.
   */
  setJsonLd(key: string, data: unknown): void {
    const head = this.document.head;
    if (!head) return;

    let script = head.querySelector<HTMLScriptElement>(`script[${OWNED}="${key}"]`);
    if (!script) {
      script = this.document.createElement('script');
      script.type = 'application/ld+json';
      script.setAttribute(OWNED, key);
      head.appendChild(script);
    }
    // `textContent`, never `innerHTML`: the payload carries product names typed by a vendor, and a
    // closing script tag inside one of them would end the block and start a document
    // (docs/07-security-compliance.md §4).
    script.textContent = JSON.stringify(data);
  }

  /** Removes one JSON-LD document. A page that added it clears it as it leaves. */
  clearJsonLd(key: string): void {
    this.document.head?.querySelector(`script[${OWNED}="${key}"]`)?.remove();
  }

  /** Resolves a path against the canonical origin. An absolute URL is returned unchanged. */
  absolute(path: string | undefined): string {
    if (!path) return '';
    if (/^https?:\/\//i.test(path)) return path;
    const base = this.canonicalBaseUrl;
    if (!base) return path;
    return `${base}${path.startsWith('/') ? '' : '/'}${path}`;
  }

  /** Applies the title template, substituting both tokens the contract defines. */
  /**
   * The page title in the store's own template — `{title} | {store}` by default. Public so the
   * router's title strategy formats its fallback the same way a page formats its own; two
   * templates for one tab is how "Cushions | Klara Home" and "Cart · Klara Home" end up side by
   * side in a customer's history.
   */
  format(pageTitle: string): string {
    const applied = this.site.titleTemplate
      .replace('{title}', pageTitle)
      .replace('{store}', this.site.storeName);

    // A store name that has not arrived would otherwise leave the template's separator dangling —
    // "Cushions | " — so the separators are trimmed off either end rather than printed empty.
    return applied.replace(/^[\s|–—-]+|[\s|–—-]+$/g, '') || pageTitle;
  }

  private setCanonical(path: string | undefined): void {
    const head = this.document.head;
    if (!head) return;

    const href = this.absolute(path);
    const existing = head.querySelector<HTMLLinkElement>(`link[rel="canonical"][${OWNED}]`);

    // No path and no origin means we cannot state a canonical honestly, so we state none.
    if (!href) {
      existing?.remove();
      return;
    }

    const link = existing ?? this.document.createElement('link');
    link.setAttribute('rel', 'canonical');
    link.setAttribute(OWNED, 'canonical');
    link.setAttribute('href', href);
    if (!existing) head.appendChild(link);
  }

  /** Sets a meta tag, or removes it when the value is empty — an empty description is worse than none. */
  private setTag(attribute: 'name' | 'property', key: string, content: string): void {
    const selector = `${attribute}="${key}"`;
    if (!content) {
      this.meta.removeTag(selector);
      return;
    }
    this.meta.updateTag({ [attribute]: key, content }, selector);
  }
}
