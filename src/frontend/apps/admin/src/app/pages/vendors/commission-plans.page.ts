import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  CategoryNode,
  CatalogAdminService,
  CommissionPlanResponse,
  CommissionPlanType,
  CommissionQuote,
  CommissionRulePayload,
  VendorsAdminService,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  CellTemplate,
  DataTable,
  DataTableColumn,
  EntityDrawer,
  FormShell,
  PageHeader,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { tableMoney } from '../../core/format';

/** One rule being edited. Prices are strings until they are sent. */
interface RuleDraft {
  categoryId: string;
  minPrice: string;
  maxPrice: string;
  rate: string;
  fixedFee: string;
}

const PLAN_TYPES: readonly { value: CommissionPlanType; label: string; hint: string }[] = [
  { value: 'Percentage', label: 'A percentage of the sale', hint: 'The usual arrangement.' },
  { value: 'Flat', label: 'A flat fee per unit', hint: 'The rate is ignored; the fee is charged per unit.' },
  { value: 'Tiered', label: 'Tiered by price', hint: 'Rules with price bands decide the rate.' },
];

/**
 * What the platform charges a seller for a sale.
 *
 * **A plan is a default plus a list of rules, and the narrowest rule wins.** A rule can be about a
 * category, a price band, or both; the resolver picks the most specific match and falls back to the
 * plan's own rate. That is why the preview beside the editor matters more than it looks: "12% with
 * four exceptions" is a sentence nobody can evaluate in their head, and `previewCommission` runs
 * **the same resolver that will freeze a rate onto an order line**, so the number here is the
 * number a settlement statement will carry.
 *
 * **Changing a plan does not re-rate anything already sold.** Step 14 freezes the commission onto
 * the order line at placement and Step 18 reads it off there rather than re-resolving — so an edit
 * takes effect on the next order and never on last month's statement. The screen says so, because
 * the opposite assumption is the one that produces an angry seller.
 *
 * Exactly one plan is the store's default. Making a second one default demotes the first, which is
 * a property of the record rather than of this screen.
 */
@Component({
  selector: 'kh-commission-plans-page',
  imports: [
    Alert,
    Badge,
    Button,
    CellTemplate,
    Checkbox,
    Control,
    DataTable,
    EntityDrawer,
    Field,
    FormShell,
    HasPermission,
    Icon,
    PageHeader,
    Skeleton,
  ],
  template: `
    <kh-page-header heading="Commission plans" description="What the platform charges a seller, and on what.">
      <button
        khButton
        type="button"
        variant="primary"
        *khHasPermission="'vendors.commission.manage'"
        (click)="startCreate()"
      >
        <kh-icon name="plus" size="sm" />
        New plan
      </button>
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="Plans could not be loaded">{{ message }}</kh-alert>
    }

    <kh-alert tone="info" heading="Editing a plan changes future orders only">
      The commission is frozen onto an order line when the order is placed, and a settlement reads it from
      there. Nothing already sold is re-rated.
    </kh-alert>

    <div class="layout">
      @if (loading()) {
        <kh-skeleton height="14rem" />
      } @else {
        <kh-data-table
          label="Commission plans"
          [columns]="columns"
          [rows]="plans()"
          [rowKey]="rowKey"
          [rowLabel]="rowLabel"
          exportMode="page"
          emptyMessage="No commission plan has been created yet."
        >
          <ng-template khCell="name" let-row>
            <button type="button" class="link" (click)="startEdit(row)">{{ row.name }}</button>
            <span class="note">
              <code>{{ row.code }}</code>
              @if (row.description) {
                · {{ row.description }}
              }
            </span>
          </ng-template>

          <ng-template khCell="state" let-row>
            <kh-badge [tone]="row.isActive ? 'success' : 'neutral'">
              {{ row.isActive ? 'Active' : 'Retired' }}
            </kh-badge>
            @if (row.isDefault) {
              <kh-badge tone="primary">Default</kh-badge>
            }
          </ng-template>
        </kh-data-table>
      }

      <aside class="panel">
        <h2>What would this cost a seller?</h2>
        <p class="hint">
          Runs the resolver that freezes the rate onto an order line, so the answer here is the answer a
          statement will carry — and it names the rule that matched.
        </p>

        <kh-field label="Seller id" for="preview-vendor">
          <input
            khControl
            id="preview-vendor"
            type="text"
            [value]="previewVendorId()"
            (input)="previewVendorId.set($any($event.target).value)"
          />
        </kh-field>

        <kh-field label="Category" for="preview-category" [optional]="true">
          <select
            khControl
            id="preview-category"
            [value]="previewCategoryId()"
            (change)="previewCategoryId.set($any($event.target).value)"
          >
            <option value="">Any category</option>
            @for (option of categoryOptions(); track option.id) {
              <option [value]="option.id">{{ option.label }}</option>
            }
          </select>
        </kh-field>

        <kh-field label="Unit price" for="preview-price">
          <input
            khControl
            id="preview-price"
            type="number"
            min="0"
            step="0.01"
            [value]="previewPrice()"
            (input)="previewPrice.set($any($event.target).value)"
          />
        </kh-field>

        <button khButton type="button" [disabled]="previewing()" (click)="preview()">
          {{ previewing() ? 'Asking…' : 'Work it out' }}
        </button>

        @if (previewError(); as message) {
          <kh-alert tone="danger" heading="It could not be worked out">{{ message }}</kh-alert>
        }

        @if (quote(); as answer) {
          <dl class="answer">
            <dt>Plan</dt>
            <dd>{{ answer.planName }}</dd>
            <dt>Rate</dt>
            <dd>{{ answer.ratePercent }}%</dd>
            <dt>Fee per unit</dt>
            <dd>{{ money(answer.fixedFee) }}</dd>
            <dt>Rule matched</dt>
            <dd>{{ answer.matchedCategoryId ? 'A category rule' : "the plan's default" }}</dd>
          </dl>
        }
      </aside>
    </div>

    @if (drawerOpen()) {
      <kh-entity-drawer
        [heading]="editing() ? 'Edit plan' : 'New plan'"
        [subtitle]="editing()?.code ?? null"
        (closed)="drawerOpen.set(false)"
      >
        <kh-form-shell
          heading="Commission plan"
          [summary]="summary()"
          [saving]="saving()"
          [dirty]="true"
          [submitLabel]="editing() ? 'Save' : 'Create'"
          (submitted)="save()"
          (cancelled)="drawerOpen.set(false)"
        >
          <kh-field label="Name" for="plan-name" [error]="form.fields.name.error()">
            <input
              khControl
              id="plan-name"
              type="text"
              maxlength="120"
              [value]="form.fields.name.value()"
              (input)="form.fields.name.set($any($event.target).value)"
              (touched)="form.fields.name.markTouched()"
            />
          </kh-field>

          @if (!editing()) {
            <kh-field
              label="Code"
              for="plan-code"
              hint="Stable, and not editable afterwards."
              [error]="form.fields.code.error()"
            >
              <input
                khControl
                id="plan-code"
                type="text"
                maxlength="40"
                [value]="form.fields.code.value()"
                (input)="form.fields.code.set($any($event.target).value)"
              />
            </kh-field>
          }

          <kh-field label="Description" for="plan-description" [optional]="true">
            <textarea
              khControl
              id="plan-description"
              rows="2"
              [value]="form.fields.description.value()"
              (input)="form.fields.description.set($any($event.target).value)"
            ></textarea>
          </kh-field>

          <kh-field label="How it charges" for="plan-type" [hint]="typeHint()">
            <select
              khControl
              id="plan-type"
              [value]="planType()"
              (change)="planType.set($any($event.target).value)"
            >
              @for (choice of planTypes; track choice.value) {
                <option [value]="choice.value">{{ choice.label }}</option>
              }
            </select>
          </kh-field>

          <div class="row">
            <kh-field label="Default rate (%)" for="plan-rate">
              <input
                khControl
                id="plan-rate"
                type="number"
                min="0"
                max="100"
                step="0.01"
                [value]="form.fields.defaultRate.value()"
                (input)="form.fields.defaultRate.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Default fee per unit" for="plan-fee">
              <input
                khControl
                id="plan-fee"
                type="number"
                min="0"
                step="0.01"
                [value]="form.fields.defaultFixedFee.value()"
                (input)="form.fields.defaultFixedFee.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <fieldset>
            <legend>Exceptions</legend>
            <p class="hint">
              The narrowest match wins: a rule for a category and a price band beats one for the category
              alone, which beats the plan's default.
            </p>

            @for (rule of rules(); track $index) {
              <div class="rule">
                <kh-field [label]="'Category'" [for]="'rule-category-' + $index" [optional]="true">
                  <select
                    khControl
                    [id]="'rule-category-' + $index"
                    [value]="rule.categoryId"
                    (change)="setRule($index, 'categoryId', $any($event.target).value)"
                  >
                    <option value="">Any category</option>
                    @for (option of categoryOptions(); track option.id) {
                      <option [value]="option.id">{{ option.label }}</option>
                    }
                  </select>
                </kh-field>

                <div class="row">
                  <kh-field [label]="'From price'" [for]="'rule-min-' + $index" [optional]="true">
                    <input
                      khControl
                      [id]="'rule-min-' + $index"
                      type="number"
                      min="0"
                      step="0.01"
                      [value]="rule.minPrice"
                      (input)="setRule($index, 'minPrice', $any($event.target).value)"
                    />
                  </kh-field>
                  <kh-field [label]="'To price'" [for]="'rule-max-' + $index" [optional]="true">
                    <input
                      khControl
                      [id]="'rule-max-' + $index"
                      type="number"
                      min="0"
                      step="0.01"
                      [value]="rule.maxPrice"
                      (input)="setRule($index, 'maxPrice', $any($event.target).value)"
                    />
                  </kh-field>
                </div>

                <div class="row">
                  <kh-field [label]="'Rate (%)'" [for]="'rule-rate-' + $index">
                    <input
                      khControl
                      [id]="'rule-rate-' + $index"
                      type="number"
                      min="0"
                      max="100"
                      step="0.01"
                      [value]="rule.rate"
                      (input)="setRule($index, 'rate', $any($event.target).value)"
                    />
                  </kh-field>
                  <kh-field [label]="'Fee per unit'" [for]="'rule-fee-' + $index">
                    <input
                      khControl
                      [id]="'rule-fee-' + $index"
                      type="number"
                      min="0"
                      step="0.01"
                      [value]="rule.fixedFee"
                      (input)="setRule($index, 'fixedFee', $any($event.target).value)"
                    />
                  </kh-field>
                  <button khButton type="button" size="sm" variant="tertiary" (click)="removeRule($index)">
                    Remove
                  </button>
                </div>
              </div>
            }

            <button khButton type="button" size="sm" (click)="addRule()">
              <kh-icon name="plus" size="sm" />
              Add an exception
            </button>
          </fieldset>

          @if (editing()) {
            <kh-checkbox
              label="Active"
              inputId="plan-active"
              [checked]="isActive()"
              (checkedChange)="isActive.set($event)"
            />
            <kh-checkbox
              label="Make this the store's default plan"
              inputId="plan-default"
              [checked]="isDefault()"
              (checkedChange)="isDefault.set($event)"
            />
          }
        </kh-form-shell>
      </kh-entity-drawer>
    }
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .layout {
      display: grid;
      gap: var(--space-6);
      grid-template-columns: minmax(0, 1fr);
    }

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: minmax(0, 3fr) minmax(18rem, 1fr);
        align-items: start;
      }
    }

    .panel {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .panel h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
    }

    .hint {
      margin: 0 0 var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .link {
      display: block;
      padding: 0;
      border: none;
      background: none;
      color: var(--color-link);
      font: inherit;
      font-weight: var(--weight-medium);
      text-align: start;
      cursor: pointer;
    }

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    code {
      font-family: var(--font-mono);
    }

    fieldset {
      margin-block: var(--space-4);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    legend {
      padding-inline: var(--space-2);
      font-weight: var(--weight-medium);
    }

    .rule {
      margin-block-end: var(--space-3);
      padding-block-end: var(--space-3);
      border-block-end: 1px solid var(--color-border);
    }

    .row {
      display: flex;
      gap: var(--space-3);
      align-items: flex-end;
      flex-wrap: wrap;
    }

    .row > kh-field {
      flex: 1 1 8rem;
    }

    .answer {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: var(--space-1) var(--space-4);
      margin-block-start: var(--space-4);
    }

    .answer dt {
      color: var(--color-text-muted);
    }

    .answer dd {
      margin: 0;
      text-align: end;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CommissionPlansPage {
  private readonly vendors = inject(VendorsAdminService);
  private readonly catalog = inject(CatalogAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly planTypes = PLAN_TYPES;

  protected readonly plans = signal<readonly CommissionPlanResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly drawerOpen = signal(false);
  protected readonly editing = signal<CommissionPlanResponse | null>(null);
  protected readonly planType = signal<CommissionPlanType>('Percentage');
  protected readonly rules = signal<readonly RuleDraft[]>([]);
  protected readonly isActive = signal(true);
  protected readonly isDefault = signal(false);

  protected readonly categoryOptions = signal<readonly { id: string; label: string }[]>([]);

  protected readonly previewVendorId = signal('');
  protected readonly previewCategoryId = signal('');
  protected readonly previewPrice = signal('1000');
  protected readonly previewing = signal(false);
  protected readonly previewError = signal<string | null>(null);
  protected readonly quote = signal<CommissionQuote | null>(null);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('A name')], this.submitted),
    code: formField('', [required('A code')], this.submitted),
    description: formField('', [], this.submitted),
    defaultRate: formField('0', [], this.submitted),
    defaultFixedFee: formField('0', [], this.submitted),
  });

  protected readonly typeHint = computed(
    () => PLAN_TYPES.find((choice) => choice.value === this.planType())?.hint ?? '',
  );

  protected readonly rowKey = (row: CommissionPlanResponse) => row.id;
  protected readonly rowLabel = (row: CommissionPlanResponse) => row.name;

  protected readonly columns: readonly DataTableColumn<CommissionPlanResponse>[] = [
    { key: 'name', label: 'Plan', kind: 'custom' },
    { key: 'planType', label: 'How', value: (row) => row.planType, width: '9rem' },
    {
      key: 'defaultRate',
      label: 'Default rate',
      kind: 'number',
      value: (row) => `${row.defaultRate}%`,
      width: '9rem',
    },
    {
      key: 'defaultFixedFee',
      label: 'Fee per unit',
      kind: 'number',
      value: (row) => tableMoney(row.defaultFixedFee),
      width: '9rem',
    },
    { key: 'rules', label: 'Exceptions', kind: 'number', value: (row) => row.rules.length, width: '7rem' },
    {
      key: 'vendorCount',
      label: 'Sellers on it',
      kind: 'number',
      value: (row) => row.vendorCount,
      width: '8rem',
    },
    { key: 'state', label: 'State', kind: 'custom', width: '12rem' },
  ];

  constructor() {
    this.load();
    this.catalog.categoryTree(false).subscribe({
      next: (tree) => this.categoryOptions.set(flatten(tree, 0)),
      error: () => this.categoryOptions.set([]),
    });
  }

  protected money(amount: number): string {
    return tableMoney(amount);
  }

  // ---- Rules ------------------------------------------------------------------------------------

  protected addRule(): void {
    this.rules.update((current) => [
      ...current,
      { categoryId: '', minPrice: '', maxPrice: '', rate: '0', fixedFee: '0' },
    ]);
  }

  protected removeRule(index: number): void {
    this.rules.update((current) => current.filter((_rule, position) => position !== index));
  }

  protected setRule(index: number, field: keyof RuleDraft, value: string): void {
    this.rules.update((current) =>
      current.map((rule, position) => (position === index ? { ...rule, [field]: value } : rule)),
    );
  }

  // ---- The editor -------------------------------------------------------------------------------

  protected startCreate(): void {
    this.editing.set(null);
    this.planType.set('Percentage');
    this.rules.set([]);
    this.isActive.set(true);
    this.isDefault.set(false);
    this.summary.set([]);
    this.form.reset({ name: '', code: '', description: '', defaultRate: '0', defaultFixedFee: '0' });
    this.drawerOpen.set(true);
  }

  protected startEdit(plan: CommissionPlanResponse): void {
    this.editing.set(plan);
    this.planType.set(plan.planType);
    this.isActive.set(plan.isActive);
    this.isDefault.set(plan.isDefault);
    this.summary.set([]);
    this.rules.set(
      plan.rules.map((rule) => ({
        categoryId: rule.categoryId ?? '',
        minPrice: rule.minPrice === null ? '' : String(rule.minPrice),
        maxPrice: rule.maxPrice === null ? '' : String(rule.maxPrice),
        rate: String(rule.rate),
        fixedFee: String(rule.fixedFee),
      })),
    );
    this.form.reset({
      name: plan.name,
      code: plan.code,
      description: plan.description ?? '',
      defaultRate: String(plan.defaultRate),
      defaultFixedFee: String(plan.defaultFixedFee),
    });
    this.drawerOpen.set(true);
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    const rules: CommissionRulePayload[] = this.rules().map((rule) => ({
      categoryId: rule.categoryId || null,
      minPrice: rule.minPrice.trim() === '' ? null : Number(rule.minPrice) || 0,
      maxPrice: rule.maxPrice.trim() === '' ? null : Number(rule.maxPrice) || 0,
      rate: Number(rule.rate) || 0,
      fixedFee: Number(rule.fixedFee) || 0,
    }));

    this.saving.set(true);
    this.summary.set([]);

    const existing = this.editing();
    const request = existing
      ? this.vendors.updateCommissionPlan(existing.id, {
          name: values.name,
          description: values.description || null,
          planType: this.planType(),
          defaultRate: Number(values.defaultRate) || 0,
          defaultFixedFee: Number(values.defaultFixedFee) || 0,
          isActive: this.isActive(),
          isDefault: this.isDefault(),
          rules,
        })
      : this.vendors.createCommissionPlan({
          code: values.code,
          name: values.name,
          description: values.description || null,
          planType: this.planType(),
          defaultRate: Number(values.defaultRate) || 0,
          defaultFixedFee: Number(values.defaultFixedFee) || 0,
          rules,
        });

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.drawerOpen.set(false);
        this.toasts.success(existing ? 'Plan saved.' : 'Plan created.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const errors = fieldErrors(error);
        this.summary.set(
          errors ? this.form.applyServerErrors(errors) : [describeError(error, 'It could not be saved.')],
        );
      },
    });
  }

  // ---- The preview ------------------------------------------------------------------------------

  protected preview(): void {
    const vendorId = this.previewVendorId().trim();
    if (!vendorId) {
      this.previewError.set('Give a seller id — a plan is resolved for a seller, not on its own.');
      return;
    }

    this.previewing.set(true);
    this.previewError.set(null);

    this.vendors
      .previewCommission(vendorId, Number(this.previewPrice()) || 0, this.previewCategoryId() || undefined)
      .subscribe({
        next: (quote) => {
          this.previewing.set(false);
          this.quote.set(quote);
        },
        error: (error: unknown) => {
          this.previewing.set(false);
          this.quote.set(null);
          this.previewError.set(describeError(error, 'No commission could be worked out.'));
        },
      });
  }

  private load(): void {
    this.loading.set(true);
    this.vendors.commissionPlans(true).subscribe({
      next: (plans) => {
        this.loading.set(false);
        this.plans.set(plans);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'They could not be loaded.'));
      },
    });
  }
}

function flatten(nodes: readonly CategoryNode[], depth: number): { id: string; label: string }[] {
  return nodes.flatMap((node) => [
    { id: node.id, label: `${'— '.repeat(depth)}${node.name}` },
    ...flatten(node.children ?? [], depth + 1),
  ]);
}
