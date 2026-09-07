import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { AnalyticsEvents, AnalyticsService, BreadcrumbTrail } from '@klarahome/util';
import { map } from 'rxjs';

import { ProductListing } from './listing/product-listing';
import { RecentSearchesStore } from '../core/recent-searches.store';

/**
 * Search results — `/search?q=…`.
 *
 * The same listing as a category page, narrowed by a query rather than by a path. What is
 * different is everything around it:
 *
 *  - **It is `noindex`** (declared on the route). Every query a bot invents becomes a URL, and a
 *    store's index fills with pages nobody wrote. It is still server-rendered, because a shared
 *    search link has to open on results rather than on a spinner.
 *  - **The query is remembered**, so the header's search box can offer it back. Locally, in this
 *    browser, and never sent anywhere — see `RecentSearchesStore`.
 *  - **The heading states the query**, which is what turns a page of results into an answer.
 */
@Component({
  selector: 'kh-search-page',
  imports: [ProductListing],
  template: `
    <header class="head">
      @if (query()) {
        <h1>Results for “{{ query() }}”</h1>
      } @else {
        <h1>Search</h1>
        <p class="hint">Type what you are looking for in the box above.</p>
      }
    </header>

    @if (query()) {
      <kh-product-listing [emptyHeading]="'Nothing matched “' + query() + '”'" />
    }
  `,
  styles: `
    .head {
      padding-block: var(--space-6) var(--space-2);
    }

    h1 {
      margin: 0;
      font-size: var(--text-2xl);
    }

    .hint {
      margin: var(--space-2) 0 0;
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SearchPage {
  private readonly route = inject(ActivatedRoute);
  private readonly recent = inject(RecentSearchesStore);
  private readonly analytics = inject(AnalyticsService);
  private readonly breadcrumbs = inject(BreadcrumbTrail);

  private readonly params = toSignal(this.route.queryParams, { requireSync: true });

  protected readonly query = computed(() => ((this.params()['q'] as string | undefined) ?? '').trim());

  constructor() {
    // Read once at construction rather than watched: a filter change re-navigates with the same
    // `q`, and re-recording the search on each of them would fill the recent list with one query.
    const term = this.query();
    if (term) {
      this.recent.record(term);
      this.analytics.track(AnalyticsEvents.search, { search_term: term });
      this.breadcrumbs.setLeafLabel(`Search: ${term}`);
    }
  }
}
