import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '@klarahome/ui-primitives';

/** One step in the trail above a page title. The last one is the page itself and is not a link. */
export interface AdminCrumb {
  readonly label: string;
  readonly path?: string | null;
}

/**
 * The title of an admin screen, its trail, and the actions that belong to the whole page.
 *
 * The `<h1>` lives here and nowhere else, which is not a style rule: `RouteFocusManager` moves
 * focus to `main h1` on every navigation, so a screen without one leaves a keyboard user's focus
 * on the link they followed, reading the previous page. One component means one place that cannot
 * be forgotten.
 */
@Component({
  selector: 'kh-page-header',
  imports: [Icon, RouterLink],
  template: `
    @if (crumbs().length > 0) {
      <nav aria-label="Breadcrumb">
        <ol>
          @for (crumb of crumbs(); track crumb.label; let last = $last) {
            <li>
              @if (crumb.path && !last) {
                <a [routerLink]="crumb.path">{{ crumb.label }}</a>
                <kh-icon name="chevron-right" size="sm" />
              } @else {
                <span [attr.aria-current]="last ? 'page' : null">{{ crumb.label }}</span>
              }
            </li>
          }
        </ol>
      </nav>
    }

    <div class="row">
      <div class="titles">
        <h1 tabindex="-1">{{ heading() }}</h1>
        @if (description(); as text) {
          <p class="description">{{ text }}</p>
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

    nav ol {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
      align-items: center;
      margin: 0 0 var(--space-2);
      padding: 0;
      list-style: none;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    nav li {
      display: flex;
      gap: var(--space-1);
      align-items: center;
    }

    nav a {
      color: inherit;
    }

    .row {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      align-items: flex-start;
      justify-content: space-between;
    }

    h1 {
      margin: 0;
      font-size: var(--text-2xl);
      line-height: var(--leading-tight);
    }

    .description {
      margin: var(--space-1) 0 0;
      max-width: 60ch;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageHeader {
  readonly heading = input.required<string>();
  readonly description = input<string | null>(null);
  readonly crumbs = input<readonly AdminCrumb[]>([]);
}
