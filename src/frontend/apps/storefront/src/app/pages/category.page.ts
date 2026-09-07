import { ChangeDetectionStrategy, Component, computed, effect, inject } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CategoryNode, CategoryResponse, StoreCatalogService } from '@klarahome/data-access-catalog';
import { BreadcrumbTrail, SeoService } from '@klarahome/util';
import { map } from 'rxjs';

import { CatalogMapper } from '../core/catalog.mapper';
import { ProductListing } from './listing/product-listing';

/**
 * A category landing page — `/c/:categorySlug`.
 *
 * The category arrives from a resolver, so the server renders the real `<h1>`, description and
 * canonical rather than a skeleton; the *products* below it are the shared `ProductListing`, which
 * loads them from the search projection with the category as its narrowing filter.
 *
 * Two things happen on the way in that a listing on its own could not do:
 *
 *  - **The breadcrumb becomes the taxonomy.** The route contributes one crumb ("Category"); the
 *    real trail is Home → Furniture → Seating → Armchairs, walked out of the cached category tree.
 *    A shopper who arrived from a search result uses it to go *up*, which is the whole point of a
 *    breadcrumb on a phone.
 *  - **The child categories are offered above the results.** A shopper on "Furniture" wants
 *    "Seating" more often than they want a filter, and the links are also how a crawler reaches
 *    the deeper pages.
 */
@Component({
  selector: 'kh-category-page',
  imports: [ProductListing, RouterLink],
  template: `
    <header class="head">
      <h1>{{ category().name }}</h1>
      @if (category().description) {
        <p class="description">{{ category().description }}</p>
      }
    </header>

    @if (children().length > 0) {
      <nav class="children" aria-label="Subcategories">
        <ul>
          @for (child of children(); track child.id) {
            <li>
              <a [routerLink]="['/c', child.slug]">{{ child.name }}</a>
            </li>
          }
        </ul>
      </nav>
    }

    <kh-product-listing
      [categoryId]="category().id"
      [emptyHeading]="'Nothing in ' + category().name + ' matched'"
      [publishItemList]="true"
    />
  `,
  styles: `
    .head {
      padding-block: var(--space-6) var(--space-2);
    }

    h1 {
      margin: 0;
      font-size: var(--text-2xl);
    }

    .description {
      margin: var(--space-2) 0 0;
      max-inline-size: 68ch;
      color: var(--color-text-muted);
    }

    .children ul {
      display: flex;
      gap: var(--space-2);
      margin: var(--space-4) 0 0;
      padding: 0 0 var(--space-2);
      list-style: none;
      overflow-x: auto;
    }

    .children a {
      display: inline-flex;
      align-items: center;
      min-block-size: var(--touch-target-min);
      padding-inline: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-full);
      background: var(--color-surface-raised);
      color: var(--color-text);
      text-decoration: none;
      font-size: var(--text-sm);
      white-space: nowrap;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CategoryPage {
  private readonly route = inject(ActivatedRoute);
  private readonly seo = inject(SeoService);
  private readonly breadcrumbs = inject(BreadcrumbTrail);
  private readonly mapper = inject(CatalogMapper);

  protected readonly category = toSignal(
    this.route.data.pipe(map((data) => data['category'] as CategoryResponse)),
    { requireSync: true },
  );

  private readonly tree = toSignal(inject(StoreCatalogService).categories(), {
    initialValue: [] as CategoryNode[],
  });

  /** Where this category sits in the taxonomy, from the cached tree. */
  private readonly path = computed(() => this.mapper.categoryPath(this.tree(), this.category().slug));

  /** The immediate children, which are offered above the results and crawled from here. */
  protected readonly children = computed(
    () =>
      this.path()
        .at(-1)
        ?.children.filter((child) => child.isActive) ?? [],
  );

  constructor() {
    // Synchronously on the replayed value, so the title and canonical are written before the
    // server serialises the document — the same reasoning as `CmsPage`.
    this.route.data
      .pipe(takeUntilDestroyed())
      .subscribe((data) => this.apply(data['category'] as CategoryResponse));

    // The tree arrives after the first render on a cold cache, so the trail is published from an
    // effect rather than once: an ancestor path that appeared a tick late would otherwise never
    // be shown.
    effect(() => {
      const ancestors = this.path().slice(0, -1);
      this.breadcrumbs.setAncestors(ancestors.map((node) => ({ label: node.name, path: `/c/${node.slug}` })));
    });
  }

  private apply(category: CategoryResponse): void {
    const path = `/c/${category.slug}`;

    this.seo.apply({
      title: category.seo.metaTitle || category.name,
      description: category.seo.metaDescription || category.description || '',
      canonicalPath: category.seo.canonicalUrl || path,
      noIndex: category.seo.noIndex,
    });

    this.breadcrumbs.setLeafLabel(category.name);
  }
}
