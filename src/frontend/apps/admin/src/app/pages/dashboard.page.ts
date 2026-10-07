import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  DashboardService,
  DashboardSummaryResponse,
  OrderSummaryResponse,
} from '@klarahome/data-access-admin';
import { SessionStore } from '@klarahome/data-access-auth';
import { KpiCard, PageHeader, StatusBadge } from '@klarahome/ui-admin';
import { Alert, Button, EmptyState, ICON_NAMES, Icon, IconName } from '@klarahome/ui-primitives';

import { tableDateTime, tableMoney } from '../core/format';
import { visibleSections } from '../core/navigation';
import { QueueCountsStore } from '../core/queue-counts.store';
import { quickActionsFor } from '../core/quick-actions';

/** What each work queue looks like on the dashboard: its glyph, and how urgent a non-zero count is. */
const QUEUE_LOOK: Readonly<Record<string, { icon: string; tone: 'warning' | 'danger' }>> = {
  'to-pack': { icon: 'package', tone: 'warning' },
  overdue: { icon: 'clock', tone: 'danger' },
  returns: { icon: 'refresh', tone: 'warning' },
  moderation: { icon: 'check', tone: 'warning' },
  vendors: { icon: 'user', tone: 'warning' },
  notifications: { icon: 'alert', tone: 'danger' },
};

/**
 * The home screen: what needs doing first, then what just happened, then the shortcuts.
 *
 * "Needs attention" leads because a back office is opened to do work, and each tile is a link into
 * the pre-filtered screen. The numbers are the work queues the API can count
 * (`QueueCountsStore`); there are no revenue, conversion or stock figures here because no
 * endpoint supplies them, and a card of invented numbers is worse than no card.
 */
@Component({
  selector: 'kh-dashboard-page',
  imports: [Alert, Button, EmptyState, Icon, KpiCard, PageHeader, RouterLink, StatusBadge],
  template: `
    <kh-page-header [heading]="greeting()" description="Here's what needs your attention today." />

    @if (failure(); as message) {
      <kh-alert tone="warning" heading="Some figures could not be loaded">{{ message }}</kh-alert>
    }

    @if (kpis(); as figures) {
      <!-- Today's commercial numbers, from the same facts as the Sales-by-day report. Absent when this
           account may not read reports, or the call failed: it never blocks the work queues below. -->
      <section class="kpis" aria-labelledby="today-heading">
        <h2 id="today-heading" class="section">Today</h2>
        <div class="grid">
          @for (figure of figures; track figure.label) {
            <kh-kpi-card
              [label]="figure.label"
              [value]="figure.value"
              [hint]="figure.hint"
              [path]="figure.path"
              [icon]="figure.icon"
            />
          }
        </div>
      </section>
    }

    @if (tiles().length > 0 || loading()) {
      <section aria-labelledby="attention-heading">
        <h2 id="attention-heading" class="section">Needs attention</h2>
        <div class="grid">
          @if (loading()) {
            @for (placeholder of [0, 1, 2, 3]; track placeholder) {
              <kh-kpi-card label="Loading" [loading]="true" />
            }
          } @else {
            @for (tile of tiles(); track tile.key) {
              <kh-kpi-card
                [label]="tile.label"
                [value]="tile.value"
                [hint]="tile.hint"
                [path]="tile.path"
                [icon]="lookFor(tile.key).icon"
                [tone]="lookFor(tile.key).tone"
              />
            }
          }
        </div>
      </section>
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

    @if (recent().length > 0 || shortcuts().length > 0) {
      <div class="two">
        @if (recent().length > 0) {
          <section class="card recent" aria-labelledby="recent-heading">
            <header>
              <h2 id="recent-heading">Recent orders</h2>
              <a routerLink="/orders">View all</a>
            </header>
            <ul>
              @for (order of recent(); track order.id) {
                <li>
                  <a [routerLink]="['/orders', order.id]">
                    <span class="main">
                      <span class="number">{{ order.orderNumber }}</span>
                      <span class="who"
                        >{{ order.customerName }}
                        @if (order.customerMobile && order.customerMobile !== order.customerName) {
                          · {{ order.customerMobile }}
                        }
                        · {{ placed(order) }}</span
                      >
                    </span>
                    <kh-status-badge [status]="order.status" />
                    <span class="amount">{{ money(order) }}</span>
                  </a>
                </li>
              }
            </ul>
          </section>
        }

        @if (shortcuts().length > 0) {
          <section class="card" aria-labelledby="shortcuts-heading">
            <header><h2 id="shortcuts-heading">Shortcuts</h2></header>
            <ul class="shortcuts">
              @for (action of shortcuts(); track action.targetPath) {
                <li>
                  <a [routerLink]="action.targetPath">
                    <span class="glyph" aria-hidden="true"
                      ><kh-icon [name]="iconFor(action.icon)" size="sm"
                    /></span>
                    <span class="main">
                      <span class="number">{{ action.label }}</span>
                      <span class="who">{{ action.hint }}</span>
                    </span>
                    <kh-icon name="chevron-right" size="sm" />
                  </a>
                </li>
              }
            </ul>
          </section>
        }
      </div>
    }
  `,
  styles: `
    kh-alert {
      display: block;
      margin-block-end: var(--space-4);
    }

    .section {
      margin: 0 0 var(--space-3);
      font-size: var(--text-sm);
      font-weight: var(--weight-semibold);
      color: var(--color-text-muted);
      letter-spacing: 0.04em;
      text-transform: uppercase;
    }

    .kpis {
      margin-block-end: var(--space-6);
    }

    .grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--space-3);
    }

    /* One shrinkable track: an auto track grows to its widest nowrap child (the order lines), and
       that is what pushed the cards past the right edge of a phone. */
    .two {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      gap: var(--space-4);
      margin-block-start: var(--space-6);
    }

    .card {
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-card);
    }

    .card header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: var(--space-4);
      border-block-end: 1px solid var(--color-border);
    }

    .card h2 {
      margin: 0;
      font-size: var(--text-base);
    }

    .card header a {
      font-size: var(--text-sm);
    }

    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    li + li {
      border-block-start: 1px solid var(--color-border);
    }

    li a {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      padding: var(--space-3) var(--space-4);
      color: inherit;
      text-decoration: none;
    }

    li a:hover {
      background: var(--color-surface);
    }

    .main {
      display: flex;
      flex: 1;
      flex-direction: column;
      min-width: 0;
    }

    .number {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .who {
      overflow: hidden;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .amount {
      min-width: 4.5rem;
      font-size: var(--text-sm);
      font-variant-numeric: tabular-nums;
      text-align: end;
    }

    .glyph {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      flex: none;
      width: 2rem;
      height: 2rem;
      border-radius: var(--radius-md);
      background: var(--color-primary-subtle);
      color: var(--color-primary);
    }

    /* Two across on a phone, three from lg: six queues are a 3×2 block, not 4 and an orphan pair. */
    @media (min-width: 1024px) {
      .grid {
        grid-template-columns: repeat(3, minmax(0, 1fr));
        gap: var(--space-4);
      }

      .kpis .grid {
        grid-template-columns: repeat(4, minmax(0, 1fr));
      }

      .two {
        grid-template-columns: minmax(0, 3fr) minmax(0, 2fr);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DashboardPage {
  private readonly queues = inject(QueueCountsStore);
  private readonly session = inject(SessionStore);
  private readonly dashboard = inject(DashboardService);

  protected readonly tiles = this.queues.tiles;
  protected readonly loading = this.queues.loading;
  protected readonly failure = this.queues.failure;
  protected readonly recent = signal<readonly OrderSummaryResponse[]>([]);
  private readonly summary = signal<DashboardSummaryResponse | null>(null);

  /** The KPI cards, or null when there is nothing to show. COD is skipped when the API says null (a seller). */
  protected readonly kpis = computed(() => {
    const summary = this.summary();
    if (!summary) return null;
    const cards = [
      {
        label: 'Orders today',
        value: summary.ordersToday as number | string,
        hint: 'Placed since midnight',
        path: '/orders',
        icon: 'package',
      },
      {
        label: 'Revenue today',
        value: tableMoney(summary.revenueToday, summary.currencyCode),
        hint: 'Orders placed today',
        path: '/orders',
        icon: 'download',
      },
    ];
    if (summary.codPendingOrders !== null && summary.codPendingAmount !== null) {
      cards.push({
        label: 'Cash on delivery to collect',
        value: tableMoney(summary.codPendingAmount, summary.currencyCode),
        hint: `${summary.codPendingOrders} ${summary.codPendingOrders === 1 ? 'order' : 'orders'} unpaid`,
        path: '/orders',
        icon: 'clock',
      });
    }
    cards.push({
      label: 'Low stock',
      value: summary.lowStockCount,
      hint: 'At or below the reorder level',
      path: '/inventory/stock',
      icon: 'alert',
    });
    return cards;
  });

  protected readonly hasAnyScreen = computed(() => visibleSections(this.session.session()).length > 1);
  protected readonly shortcuts = computed(() => quickActionsFor(this.session.session()));

  /** "Good morning, Asha" — the first name only; the full one is in the account menu. */
  protected readonly greeting = computed(() => {
    const hour = new Date().getHours();
    const part = hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';
    const name = this.session.session()?.displayName?.trim() ?? '';
    // An email address is not a name: fall back to the bare greeting rather than "Good evening, a@b.in".
    const first = name.includes('@') ? '' : name.split(/\s+/)[0];
    return first ? `${part}, ${first}` : part;
  });

  constructor() {
    // Always, not only when stale: the counts are this page's content.
    this.queues.refresh(true);
    this.dashboard.recentOrders().subscribe((orders) => this.recent.set(orders));
    this.dashboard.summary().subscribe((summary) => this.summary.set(summary));
  }

  protected lookFor(key: string): { icon: string; tone: 'warning' | 'danger' } {
    return QUEUE_LOOK[key] ?? { icon: 'alert', tone: 'warning' };
  }

  protected iconFor(name: string): IconName {
    return (ICON_NAMES as readonly string[]).includes(name) ? (name as IconName) : 'plus';
  }

  protected money(order: OrderSummaryResponse): string {
    return tableMoney(order.grandTotal, order.currencyCode);
  }

  protected placed(order: OrderSummaryResponse): string {
    return tableDateTime(order.placedAt);
  }
}
