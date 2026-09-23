import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionStore } from '@klarahome/data-access-auth';
import { KpiCard, PageHeader } from '@klarahome/ui-admin';
import { Alert, Button, EmptyState } from '@klarahome/ui-primitives';

import { visibleSections } from '../core/navigation';
import { QueueCountsStore } from '../core/queue-counts.store';

/**
 * What is waiting for you.
 *
 * Every tile is a **queue of work**, not a statistic: how many parcels are unpacked, how many
 * returns are undecided, how many messages failed — each linking to the screen where the work is
 * done. Turnover and conversion belong to the reporting screens, which have an API built for them;
 * a dashboard that opened with yesterday's revenue and no way to act on it is a poster, and the
 * people who open this at nine in the morning are not looking for a poster.
 *
 * The set of tiles is derived from the signed-in user's permissions, so this page is already the
 * clearest demonstration of the step's acceptance criterion: two roles see two dashboards, from
 * one declaration, without either being written twice.
 *
 * A user whose account carries no permissions at all — freshly created, roles not yet assigned —
 * gets a page that says so rather than an empty grid. That is a real state on day one of a
 * deployment and it looks exactly like a broken screen if it is not named.
 *
 * **The queues come first, and nothing sits above them.** The header used to carry a row of
 * quick-action buttons, which on a phone pushed the first tile below the fold; those now live
 * behind "+ New" in the top bar (`core/quick-actions.ts`), reachable from every screen. The
 * counts themselves come from `QueueCountsStore` — the same numbers the Orders and Products tabs
 * wear as badges — and opening this page always refreshes them, because they are its content.
 */
@Component({
  selector: 'kh-dashboard-page',
  imports: [Alert, Button, EmptyState, KpiCard, PageHeader, RouterLink],
  template: `
    <kh-page-header heading="Dashboard" description="Here's what needs your attention today." />

    @if (failure(); as message) {
      <kh-alert tone="warning" heading="Some figures could not be loaded">{{ message }}</kh-alert>
    }

    @if (tiles().length > 0 || loading()) {
      <div class="grid">
        @if (loading()) {
          @for (placeholder of [0, 1, 2, 3]; track placeholder) {
            <kh-kpi-card label="Loading" [loading]="true" />
          }
        } @else {
          @for (tile of tiles(); track tile.key) {
            <kh-kpi-card [label]="tile.label" [value]="tile.value" [hint]="tile.hint" [path]="tile.path" />
          }
        }
      </div>
    } @else if (hasAnyScreen()) {
      <kh-empty-state
        heading="Nothing is waiting for you"
        message="Every queue your account can see is empty. Use the navigation to go straight to a screen."
      />
    } @else {
      <kh-empty-state
        heading="Your account has no permissions yet"
        message="You are signed in, but no role has been assigned to this account. Ask whoever administers the platform to assign one; until then there is nothing here to show."
      >
        <a khButton variant="secondary" routerLink="/profile">Your profile and security</a>
      </kh-empty-state>
    }
  `,
  styles: `
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(14rem, 1fr));
      gap: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardPage {
  private readonly queues = inject(QueueCountsStore);
  private readonly session = inject(SessionStore);

  protected readonly tiles = this.queues.tiles;
  protected readonly loading = this.queues.loading;
  protected readonly failure = this.queues.failure;

  /** Whether anything at all is reachable — the difference between "all clear" and "no roles". */
  protected readonly hasAnyScreen = computed(() => visibleSections(this.session.session()).length > 1);

  constructor() {
    // Always, not only when stale: the counts are this page's content.
    this.queues.refresh(true);
  }
}
