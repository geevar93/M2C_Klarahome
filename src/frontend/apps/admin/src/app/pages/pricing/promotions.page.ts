import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  PricingAdminService,
  PromotionFilters,
  PromotionResponse,
  PromotionType,
} from '@klarahome/data-access-admin';
import {
  CellTemplate,
  DataTable,
  DataTableColumn,
  FilterBar,
  FilterDefinition,
  FilterValues,
  PageHeader,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Icon } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime, tableMoney } from '../../core/format';
import { PROMOTION_TYPES } from './promotion-vocabulary';

/**
 * Every campaign, live and otherwise.
 *
 * **"Active" and "live" are two different facts, and the list shows both.** A promotion is active
 * when somebody has switched it on and live when the clock is inside its window — so a campaign
 * built on Monday for a sale that starts on Friday is active and not live, and one whose end date
 * passed on Sunday is active and not live either. A single status column would have to lie about
 * one of them. The window is computed here rather than asked for, because `PromotionResponse`
 * carries the dates and the browser has a clock.
 *
 * Switching one on or off is done from the list, without opening it: taking a wrong discount down
 * is the one thing on this screen somebody does in a hurry, and it should not require reading
 * eighteen fields first. Everything else — the mechanic, the scope, the simulator — is the
 * builder's, one route down.
 */
@Component({
  selector: 'kh-promotions-page',
  imports: [Alert, Badge, Button, CellTemplate, DataTable, FilterBar, Icon, PageHeader, RouterLink],
  template: `
    <kh-page-header heading="Promotions" description="Coupons and cart rules, and when each one applies.">
      <a khButton routerLink="/promotions/new" variant="primary">
        <kh-icon name="plus" size="sm" />
        New promotion
      </a>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Promotions could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not take">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Promotions"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="promotions-list"
      exportMode="page"
      emptyMessage="No promotion matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        searchLabel="Search promotions"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="name" let-row>
        <a class="link" [routerLink]="['/promotions', row.id]">{{ row.name }}</a>
        <span class="note">
          @if (row.code) {
            Coupon <code>{{ row.code }}</code>
          } @else {
            Automatic — no code to type
          }
        </span>
      </ng-template>

      <ng-template khCell="state" let-row>
        <kh-badge [tone]="row.isActive ? 'success' : 'neutral'">
          {{ row.isActive ? 'On' : 'Off' }}
        </kh-badge>
        <span class="note">{{ windowLabel(row) }}</span>
      </ng-template>

      <ng-template khCell="usage" let-row>
        <span>{{ row.usageCount }}</span>
        @if (row.usageLimitTotal !== null) {
          <span class="note">of {{ row.usageLimitTotal }}</span>
        }
      </ng-template>

      <ng-template khCell="actions" let-row>
        <button
          khButton
          type="button"
          size="sm"
          variant="tertiary"
          [disabled]="busyId() === row.id"
          (click)="toggle(row)"
        >
          {{ row.isActive ? 'Switch off' : 'Switch on' }}
        </button>
      </ng-template>
    </kh-data-table>
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .link {
      display: block;
      font-weight: var(--weight-medium);
    }

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    code {
      font-family: var(--font-mono);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PromotionsPage {
  private readonly pricing = inject(PricingAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly list = this.pricing.promotions();
  protected readonly values = signal<FilterValues>({});
  protected readonly busyId = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly rowKey = (row: PromotionResponse) => row.id;
  protected readonly rowLabel = (row: PromotionResponse) => row.name;

  protected readonly columns: readonly DataTableColumn<PromotionResponse>[] = [
    { key: 'name', label: 'Promotion', kind: 'custom' },
    { key: 'type', label: 'Mechanic', value: (row) => row.type, width: '9rem' },
    { key: 'appliesTo', label: 'Applies to', value: (row) => row.appliesTo, width: '8rem' },
    { key: 'value', label: 'Value', value: (row) => this.valueLabel(row), width: '8rem' },
    { key: 'state', label: 'State', kind: 'custom', width: '12rem' },
    { key: 'usage', label: 'Used', kind: 'custom', width: '7rem' },
    {
      key: 'priority',
      label: 'Priority',
      kind: 'number',
      value: (row) => row.priority,
      hiddenByDefault: true,
    },
    { key: 'stacking', label: 'Stacking', value: (row) => row.stacking, hiddenByDefault: true },
    {
      key: 'minOrderValue',
      label: 'Minimum basket',
      kind: 'number',
      value: (row) => (row.minOrderValue > 0 ? tableMoney(row.minOrderValue) : '—'),
      hiddenByDefault: true,
    },
    { key: 'actions', label: '', kind: 'custom', width: '8rem' },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'type',
      label: 'Mechanic',
      kind: 'select',
      options: PROMOTION_TYPES.map((entry) => ({ value: entry.value, label: entry.label })),
    },
    {
      key: 'activeOnly',
      label: 'State',
      kind: 'select',
      options: [{ value: 'true', label: 'Switched on only' }],
    },
  ];

  constructor() {
    this.list.load();
  }

  protected valueLabel(row: PromotionResponse): string {
    if (row.type === 'FreeShipping') return 'Free delivery';
    return row.type === 'Percentage' ? `${row.value}%` : tableMoney(row.value);
  }

  /** What the dates say, which is not what `isActive` says. See the class remarks. */
  protected windowLabel(row: PromotionResponse): string {
    const now = Date.now();
    const starts = new Date(row.startsAt).getTime();
    const ends = row.endsAt ? new Date(row.endsAt).getTime() : null;

    if (Number.isNaN(starts)) return '';
    if (now < starts) return `Starts ${tableDateTime(row.startsAt)}`;
    if (ends !== null && now > ends) return `Ended ${tableDateTime(row.endsAt)}`;
    return ends === null ? 'In window — no end date' : `Until ${tableDateTime(row.endsAt)}`;
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: PromotionFilters = {
      type: (values['type'] as PromotionType) || undefined,
      search: values['q'],
      activeOnly: values['activeOnly'] === 'true' ? true : undefined,
    };
    this.list.setFilters(filters);
  }

  protected toggle(row: PromotionResponse): void {
    this.busyId.set(row.id);
    this.actionError.set(null);

    const request = row.isActive
      ? this.pricing.deactivatePromotion(row.id)
      : this.pricing.activatePromotion(row.id);

    request.subscribe({
      next: () => {
        this.busyId.set(null);
        this.toasts.success(row.isActive ? 'Promotion switched off.' : 'Promotion switched on.');
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.actionError.set(describeError(error, 'That promotion could not be changed.'));
      },
    });
  }
}
