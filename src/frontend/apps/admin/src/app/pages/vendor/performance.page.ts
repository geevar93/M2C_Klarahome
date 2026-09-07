import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  ReportDefinition,
  ReportingAdminService,
  VendorBalanceResponse,
  VendorResponse,
  SettlementsAdminService,
  VendorsAdminService,
} from '@klarahome/data-access-admin';
import { KpiCard, PageHeader } from '@klarahome/ui-admin';
import { Alert, Button, Rating, Skeleton } from '@klarahome/ui-primitives';

import { describeError } from '../../core/describe-error';
import { tableDateTime, tableMoney } from '../../core/format';
import { ReportRunner } from '../reports/report-runner';

/**
 * How a seller is doing.
 *
 * **Nothing on this page is computed here.** The tiles come off the seller's balance and their
 * record; the figures below come from the reporting module, run through the same runner the
 * reports screen uses. That is the point: a seller's performance page and the store's reports must
 * not be able to disagree about what a month's sales were, and the only way to guarantee that is
 * for them to be the same query.
 *
 * **The report picker is the API's list, filtered to what a seller may run.** `GET /admin/reports`
 * already excludes the store's own reports for a seller (Step 21), so there is nothing to filter
 * here — the picker shows what came back. A deployment that adds a vendor-scoped report gets it on
 * this page with no change to this file.
 *
 * **The money tiles say what is settled and what is not**, because "how much have I made" and "how
 * much am I about to be paid" are different questions and a single balance answers neither: sales
 * from an open period are earned and not yet payable, and a closed period is payable and not yet
 * sent.
 */
@Component({
  selector: 'kh-vendor-performance-page',
  imports: [Alert, Button, KpiCard, PageHeader, Rating, ReportRunner, RouterLink, Skeleton],
  template: `
    <kh-page-header heading="How you are doing" description="Your sales, your money and your standing.">
      <a khButton routerLink="/ledger" variant="tertiary">Your ledger</a>
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="Your figures could not be loaded">{{ message }}</kh-alert>
    }

    @if (loading()) {
      <kh-skeleton height="8rem" />
    } @else {
      <div class="tiles">
        <kh-kpi-card
          label="Balance now"
          [value]="balance() ? money(balance()!.currentBalance, balance()!.currencyCode) : null"
          hint="Everything credited less everything debited"
        />
        <kh-kpi-card
          label="Earned, not yet settled"
          [value]="balance() ? money(balance()!.unsettledBalance, balance()!.currencyCode) : null"
          hint="The period is still open"
        />
        <kh-kpi-card
          label="Waiting for a payout"
          [value]="balance() ? money(balance()!.awaitingPayout, balance()!.currencyCode) : null"
          hint="Closed and payable"
          path="/settlements/cycles"
        />
        <kh-kpi-card
          label="Last paid"
          [value]="balance()?.lastPaidAt ? dateTime(balance()!.lastPaidAt!) : '—'"
          path="/payouts"
        />
      </div>

      @if (vendor(); as current) {
        <section class="standing">
          <div>
            <span class="label">Your rating</span>
            @if (current.rating !== null) {
              <kh-rating [average]="current.rating" [showEmpty]="true" />
            } @else {
              <span class="note">Nobody has reviewed you yet.</span>
            }
          </div>
          <div>
            <span class="label">Your dispatch promise</span>
            <span>{{ current.dispatchSlaHours }} hours</span>
            <span class="note">An order not handed over within this counts as late.</span>
          </div>
          <div>
            <span class="label">Your return window</span>
            <span>
              {{
                current.returnPolicy.acceptsReturns
                  ? current.returnPolicy.windowDays + ' days'
                  : 'You do not accept returns'
              }}
            </span>
          </div>
        </section>
      }
    }

    <section class="reports">
      <h2>Your figures</h2>

      @if (reports().length === 0) {
        <p class="note">No report is available to you yet.</p>
      } @else {
        <div class="picker">
          @for (report of reports(); track report.key) {
            <button
              khButton
              type="button"
              size="sm"
              [variant]="report.key === selectedKey() ? 'primary' : 'tertiary'"
              (click)="selectedKey.set(report.key)"
            >
              {{ report.name }}
            </button>
          }
        </div>

        @if (selected(); as report) {
          <kh-report-runner [reportKey]="report.key" [definition]="report" [showVendorFilter]="false" />
        }
      }
    </section>
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .tiles {
      display: grid;
      gap: var(--space-3);
      grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
      margin-block-end: var(--space-4);
    }

    .standing {
      display: grid;
      gap: var(--space-4);
      grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
      margin-block-end: var(--space-6);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .label {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    h2 {
      margin: 0 0 var(--space-3);
      font-size: var(--text-lg);
    }

    .picker {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
      margin-block-end: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VendorPerformancePage {
  private readonly vendors = inject(VendorsAdminService);
  private readonly settlements = inject(SettlementsAdminService);
  private readonly reporting = inject(ReportingAdminService);

  protected readonly dateTime = tableDateTime;

  protected readonly vendor = signal<VendorResponse | null>(null);
  protected readonly balance = signal<VendorBalanceResponse | null>(null);
  protected readonly reports = signal<readonly ReportDefinition[]>([]);
  protected readonly selectedKey = signal('');

  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);

  protected readonly selected = computed(
    () => this.reports().find((report) => report.key === this.selectedKey()) ?? null,
  );

  constructor() {
    this.load();

    this.reporting.definitions().subscribe({
      next: (definitions) => {
        this.reports.set(definitions);
        this.selectedKey.set(definitions[0]?.key ?? '');
      },
      // Not fatal: the tiles above are still the seller's own money.
      error: () => this.reports.set([]),
    });
  }

  protected money(amount: number, currency: string): string {
    return tableMoney(amount, currency);
  }

  private load(): void {
    this.loading.set(true);
    this.vendors.mine().subscribe({
      next: (vendor) => {
        this.loading.set(false);
        this.vendor.set(vendor);

        this.settlements.balance(vendor.id).subscribe({
          next: (balance) => this.balance.set(balance),
          // A seller with no ledger rows yet has no balance, which is not an error.
          error: () => this.balance.set(null),
        });
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'Your seller account could not be loaded.'));
      },
    });
  }
}
