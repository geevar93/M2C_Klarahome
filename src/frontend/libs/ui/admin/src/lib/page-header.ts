import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
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
    @if (back(); as target) {
      <a class="back" [routerLink]="target.path">
        <kh-icon name="chevron-left" size="sm" />
        {{ target.label }}
      </a>
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
      margin-block-end: var(--space-5);
    }

    .back {
      display: inline-flex;
      gap: var(--space-1);
      align-items: center;
      margin-block-end: var(--space-2);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
      text-decoration: none;
    }

    .back:hover {
      color: var(--color-text);
    }

    .row {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      align-items: flex-start;
      justify-content: space-between;
    }

    .titles {
      min-width: 0;
    }

    h1 {
      margin: 0;
      font-size: var(--text-2xl);
      font-weight: var(--weight-bold);
      letter-spacing: var(--tracking-display);
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
  /**
   * Where this page sits, as a trail. The shell draws the full trail above the page; the header
   * keeps the last linked step as a "back" link, which is the one a person actually uses.
   */
  readonly crumbs = input<readonly AdminCrumb[]>([]);

  protected readonly back = computed(() => {
    const linked = this.crumbs().filter((crumb) => crumb.path);
    const last = linked[linked.length - 1];
    return last?.path ? { label: last.label, path: last.path } : null;
  });
}
