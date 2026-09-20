import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { Icon } from './icon';

/**
 * The one way a storefront page starts.
 *
 * Every customer-facing page opens the same way: an optional way back, the `<h1>`, an optional
 * status beside it, an optional lead sentence, and the page's primary action on the same row from
 * the `sm` breakpoint. It is one component because the alternative is what the account area had
 * before it — eight pages, three heading sizes, two of them with a back link, one with an action —
 * and a customer who cannot predict where the heading or the action will be on the next page has
 * to look for them on every page.
 *
 * The heading is always rendered, including while the page is loading. A page whose `<h1>` only
 * appears once its data has arrived is a page with no name for the seconds that matter most, and
 * the shell moves focus to `main h1` on every navigation (`app.ts`), which needs it to exist.
 */
@Component({
  selector: 'kh-page-header',
  imports: [Icon, RouterLink],
  template: `
    @if (backHref(); as href) {
      <a class="back" [routerLink]="href">
        <kh-icon name="chevron-left" size="sm" />
        {{ backLabel() }}
      </a>
    }

    <div class="row">
      <div class="title">
        <h1>
          {{ title() }}
          <ng-content select="[khPageHeaderStatus]" />
        </h1>
        @if (lead()) {
          <p class="lead">{{ lead() }}</p>
        }
      </div>

      <div class="actions">
        <ng-content />
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
      margin-block-end: var(--space-6);
    }

    .back {
      display: inline-flex;
      align-items: center;
      gap: var(--space-1);
      margin-block-end: var(--space-2);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
      text-decoration: none;
    }

    .back:hover,
    .back:focus-visible {
      color: var(--color-text);
      text-decoration: underline;
    }

    .row {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
    }

    @media (min-width: 480px) {
      .row {
        flex-direction: row;
        align-items: flex-start;
        justify-content: space-between;
      }
    }

    .title {
      min-width: 0;
    }

    h1 {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
      margin: 0;
      font-size: var(--text-2xl);
    }

    .lead {
      margin: var(--space-2) 0 0;
      max-width: 60ch;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      flex: none;
    }

    .actions:empty {
      display: none;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageHeader {
  readonly title = input.required<string>();
  readonly lead = input<string | null>(null);
  /** Where "back" goes. No link is rendered when it is null. */
  readonly backHref = input<string | readonly string[] | null>(null);
  readonly backLabel = input('Back');
}
