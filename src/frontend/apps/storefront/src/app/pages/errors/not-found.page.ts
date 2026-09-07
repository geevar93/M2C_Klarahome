import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Button, EmptyState } from '@klarahome/ui-primitives';
import { SeoService } from '@klarahome/util';

/**
 * 404.
 *
 * `noindex` is set explicitly. A not-found page that is indexable is how a store ends up with
 * four hundred "Page not found" results under its own name, and the `status: 404` declared on
 * this route in `app.routes.server.ts` is the other half of saying so.
 *
 * The search box is the point of the page: somebody who followed a dead link to a product wants
 * the product, not an apology.
 */
@Component({
  selector: 'kh-not-found-page',
  imports: [Button, EmptyState, RouterLink],
  template: `
    <h1 class="kh-visually-hidden">Page not found</h1>
    <kh-empty-state
      heading="We could not find that page"
      message="The link may be old, or the product may no longer be listed. Search for what you were looking for, or start again from the home page."
    >
      <div class="actions">
        <a khButton variant="primary" routerLink="/">Go to the home page</a>
        <a khButton variant="secondary" routerLink="/search">Search the shop</a>
      </div>
    </kh-empty-state>
  `,
  styles: `
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      justify-content: center;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotFoundPage {
  constructor() {
    inject(SeoService).apply({
      title: 'Page not found',
      description: 'The page you were looking for is not here.',
      noIndex: true,
      robots: 'noindex, follow',
    });
  }
}
