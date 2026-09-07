import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { StorePageResponse } from '@klarahome/data-access-content';
import { CmsBlockRenderer } from '@klarahome/ui-patterns';
import { BreadcrumbTrail, SeoService } from '@klarahome/util';
import { map } from 'rxjs';

import { CmsBlockMapper } from '../core/cms-block.mapper';

/**
 * A CMS page — the legal and informational pages, reached at `/pages/:slug`.
 *
 * The page arrives from a resolver, so this component never renders without it and the server
 * emits the real title and description rather than a skeleton. Three things happen on the way in:
 * the SEO tags are taken from the page's own `seo` payload (the CMS is where an editor writes
 * them, and this is where they are used), the breadcrumb's last crumb becomes the page title, and
 * the `WebPage` structured data is published.
 *
 * The blocks themselves go through `CmsBlockRenderer`, the same component the home page uses — a
 * legal page and a landing page are the same document type, and rendering them two ways would mean
 * a block that looked right in one place and wrong in the other.
 */
@Component({
  selector: 'kh-cms-page',
  imports: [CmsBlockRenderer],
  template: `
    <article>
      <h1>{{ page().title }}</h1>
      @if (page().summary) {
        <p class="summary">{{ page().summary }}</p>
      }

      <!-- \`prioritiseFirst\` is false: the LCP element on an editorial page is its heading, and a
           hero further down claiming \`fetchpriority="high"\` would compete with it. -->
      <kh-cms-block-renderer [blocks]="blocks()" [prioritiseFirst]="false" />
    </article>
  `,
  styles: `
    article {
      padding-block: var(--space-6) var(--space-10);
    }

    h1 {
      font-size: var(--text-3xl);
    }

    .summary {
      color: var(--color-text-muted);
      font-size: var(--text-lg);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CmsPage {
  private readonly route = inject(ActivatedRoute);
  private readonly seo = inject(SeoService);
  private readonly breadcrumbs = inject(BreadcrumbTrail);
  private readonly mapper = inject(CmsBlockMapper);

  protected readonly page = toSignal(this.route.data.pipe(map((data) => data['page'] as StorePageResponse)), {
    requireSync: true,
  });

  protected readonly blocks = computed(() => this.mapper.toViews(this.page().blocks));

  constructor() {
    // Subscribed rather than run in an `effect`, and subscribed in the constructor rather than in
    // `ngOnInit`, for one reason: `route.data` replays its current value synchronously, so the
    // title and the canonical tag are written **before** the server serialises the document. An
    // effect would be scheduled for after the first change detection pass, by which time the HTML
    // a crawler receives has already been produced. The subscription also survives a slug change,
    // which reuses this component rather than rebuilding it.
    this.route.data
      .pipe(takeUntilDestroyed())
      .subscribe((data) => this.applySeo(data['page'] as StorePageResponse));
  }

  private applySeo(page: StorePageResponse): void {
    const path = `/pages/${page.slug}`;

    this.seo.apply({
      title: page.seo.metaTitle || page.title,
      description: page.seo.metaDescription || page.summary || '',
      canonicalPath: page.seo.canonicalUrl || path,
      noIndex: page.seo.noIndex,
      ogType: 'article',
      imageUrl: page.seo.ogImage?.url ?? undefined,
    });

    this.breadcrumbs.setLeafLabel(page.title);

    this.seo.setJsonLd('page', {
      '@context': 'https://schema.org',
      '@type': 'WebPage',
      name: page.title,
      url: this.seo.absolute(path),
      description: page.seo.metaDescription || page.summary || '',
      dateModified: page.updatedAt ?? page.publishedAt ?? undefined,
    });
  }
}
