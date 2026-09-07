import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { DashboardService, DashboardTile } from '@klarahome/data-access-admin';
import { HasPermission, SessionStore } from '@klarahome/data-access-auth';
import { KpiCard, PageHeader } from '@klarahome/ui-admin';
import { Alert, Button, EmptyState } from '@klarahome/ui-primitives';

import { visibleSections } from '../core/navigation';
import { describeError } from '../core/describe-error';

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
 */
@Component({
  selector: 'kh-dashboard-page',
  imports: [Alert, Button, EmptyState, HasPermission, KpiCard, PageHeader, RouterLink],
  template: `
    <kh-page-header heading="Dashboard" [description]="greeting()">
      <!-- The structural directive rather than a conditional block over the session: the control
           is not in the DOM at all for a user without the permission, so it cannot be tabbed onto
           or read out. The route behind it is guarded too — this hides it, the guard refuses it,
           and the API enforces it. -->
      <a
        khButton
        variant="secondary"
        routerLink="/settings/audit-log"
        *khHasPermission="'platform.audit.read'"
      >
        Audit log
      </a>
    </kh-page-header>

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
  private readonly dashboard = inject(DashboardService);
  private readonly session = inject(SessionStore);

  protected readonly tiles = signal<readonly DashboardTile[]>([]);
  protected readonly loading = signal(true);
  protected readonly failure = signal<string | null>(null);

  protected readonly greeting = computed(() => {
    const name = this.session.session()?.displayName;
    return name ? `Signed in as ${name}.` : null;
  });

  /** Whether anything at all is reachable — the difference between "all clear" and "no roles". */
  protected readonly hasAnyScreen = computed(() => visibleSections(this.session.session()).length > 1);

  constructor() {
    this.dashboard.tiles().subscribe({
      next: (tiles) => {
        this.tiles.set(tiles);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.failure.set(describeError(error, 'The dashboard figures could not be loaded.'));
        this.loading.set(false);
      },
    });
  }
}
