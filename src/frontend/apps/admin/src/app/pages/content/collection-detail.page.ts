import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import {
  CatalogAdminService,
  CategoryNode,
  CollectionResponse,
  CollectionSort,
  ContentAdminService,
  MediaFileResponse,
  ProductCardResponse,
  RuleConditionBody,
  RuleField,
  RuleOperator,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  CellTemplate,
  ConfirmDialog,
  DataTable,
  DataTableColumn,
  EntityOption,
  EntityPicker,
  PageHeader,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';
import { Observable, map } from 'rxjs';

import { describeError, fieldErrors } from '../../core/describe-error';
import { tableDateTime, tableMoney } from '../../core/format';
import { MediaPicker } from '../catalog/media-picker';
import { COLLECTION_SORTS, RULE_FIELDS, RULE_OPERATORS } from './content-vocabulary';

/** One condition being edited. `values` is a comma-separated string until it is sent. */
interface ConditionDraft {
  field: RuleField;
  key: string;
  operator: RuleOperator;
  values: string;
}

/**
 * One collection: what is in it, and what decides that.
 *
 * **A rule does not replace the pins; it fills in around them.** Every item carries `isPinned`, and
 * a refresh leaves pinned rows alone — which is the whole reason a rule-driven collection is
 * editable at all. A merchandiser pins the three products the campaign is actually about and lets
 * the rule find the rest; without the flag, the next refresh would quietly undo their choice.
 *
 * **Refreshing is a real operation with a real cost.** The rows are materialised (Step 20), so the
 * collection is only as current as its last refresh — three catalogue events and a sweep keep it
 * roughly right, and this button is what makes it exactly right now. The screen says when it last
 * happened rather than implying the rule is live.
 *
 * **Setting no rule makes the collection manual again.** `setCollectionRule` with a null rule is
 * the only way back, and it is offered as its own control rather than hidden behind clearing every
 * condition — an empty condition list is a rule that matches everything, which is a very different
 * thing from no rule at all.
 */
@Component({
  selector: 'kh-collection-detail-page',
  imports: [
    Alert,
    Badge,
    Button,
    CellTemplate,
    Checkbox,
    ConfirmDialog,
    Control,
    DataTable,
    EntityPicker,
    Field,
    HasPermission,
    Icon,
    MediaPicker,
    PageHeader,
    Skeleton,
  ],
  template: `
    <kh-page-header
      [heading]="collection()?.name ?? 'Collection'"
      [description]="subtitle()"
      [crumbs]="[{ label: 'Collections', path: '/content/collections' }]"
    >
      @if (collection(); as current) {
        <kh-badge [tone]="current.kind === 'Rule' ? 'info' : 'neutral'">
          {{ current.kind === 'Rule' ? 'Filled by a rule' : 'Chosen by hand' }}
        </kh-badge>

        <ng-container *khHasPermission="'content.content.manage'">
          @if (current.kind === 'Rule') {
            <button khButton type="button" size="sm" [disabled]="busy()" (click)="refresh()">
              {{ busy() ? 'Refreshing…' : 'Refresh now' }}
            </button>
          }
          <button khButton type="button" size="sm" variant="danger" (click)="deleting.set(true)">
            Delete
          </button>
        </ng-container>
      }
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="This collection could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="20rem" />
    } @else if (collection(); as current) {
      @if (summary().length > 0) {
        <kh-alert tone="danger" heading="That did not take">
          <ul>
            @for (message of summary(); track message) {
              <li>{{ message }}</li>
            }
          </ul>
        </kh-alert>
      }

      <div class="layout">
        <section>
          <h2>What is in it</h2>
          @if (current.kind === 'Rule') {
            <p class="hint">
              These rows are what the rule found when it last ran
              {{ current.refreshedAt ? 'on ' + dateTime(current.refreshedAt) : '— which it never has' }}. A
              pinned row survives the next refresh; the rest are replaced.
            </p>
          } @else {
            <p class="hint">Chosen by hand. Nothing refreshes these rows.</p>
          }

          @if (items.error(); as message) {
            <kh-alert tone="danger" heading="The products could not be loaded">{{ message }}</kh-alert>
          }

          <kh-data-table
            label="Products in this collection"
            [columns]="columns"
            [rows]="items.rows()"
            [rowKey]="rowKey"
            [rowLabel]="rowLabel"
            [loading]="items.loading()"
            [page]="page()"
            exportMode="page"
            emptyMessage="Nothing is in this collection yet."
            (nextPage)="items.next()"
            (previousPage)="items.previous()"
          >
            <ng-template khCell="name" let-row>
              <span class="name">{{ row.name }}</span>
              <span class="note">{{ row.brandName ?? '—' }} · /{{ row.slug }}</span>
            </ng-template>

            <ng-template khCell="actions" let-row>
              <button
                khButton
                type="button"
                size="sm"
                variant="tertiary"
                *khHasPermission="'content.content.manage'"
                [disabled]="busy()"
                (click)="unpin(row)"
              >
                Remove
              </button>
            </ng-template>
          </kh-data-table>

          <section class="panel" *khHasPermission="'content.content.manage'">
            <h3>Pin a product</h3>
            <p class="hint">A pinned product stays whatever the rule decides.</p>
            <div class="row">
              <kh-entity-picker
                label="Product"
                inputId="pin-product"
                hint="Search by name or SKU, or paste a product id."
                [search]="productSearch"
                (chose)="pinProductId.set($event?.id ?? '')"
              />
              <button khButton type="button" [disabled]="busy() || !pinProductId()" (click)="pin()">
                Pin it
              </button>
            </div>
          </section>
        </section>

        <aside>
          <section class="panel" *khHasPermission="'content.content.manage'">
            <h2>The rule</h2>
            <p class="hint">
              Conditions are about facts the catalogue already holds. Leaving the rule off keeps the
              collection manual.
            </p>

            <kh-checkbox
              label="Every condition must match"
              inputId="rule-match-all"
              [checked]="matchAll()"
              (checkedChange)="matchAll.set($event)"
            />

            @for (condition of conditions(); track $index) {
              <div class="condition">
                <kh-field [label]="'About'" [for]="'cond-field-' + $index">
                  <select
                    khControl
                    [id]="'cond-field-' + $index"
                    [value]="condition.field"
                    (change)="setCondition($index, 'field', $any($event.target).value)"
                  >
                    @for (choice of ruleFields; track choice.value) {
                      <option [value]="choice.value">{{ choice.label }}</option>
                    }
                  </select>
                </kh-field>

                @if (condition.field === 'Attribute') {
                  <kh-field [label]="'Attribute code'" [for]="'cond-key-' + $index">
                    <input
                      khControl
                      [id]="'cond-key-' + $index"
                      type="text"
                      [value]="condition.key"
                      (input)="setCondition($index, 'key', $any($event.target).value)"
                    />
                  </kh-field>
                }

                <kh-field [label]="'Which'" [for]="'cond-op-' + $index">
                  <select
                    khControl
                    [id]="'cond-op-' + $index"
                    [value]="condition.operator"
                    (change)="setCondition($index, 'operator', $any($event.target).value)"
                  >
                    @for (choice of ruleOperators; track choice.value) {
                      <option [value]="choice.value">{{ choice.label }}</option>
                    }
                  </select>
                </kh-field>

                <kh-field [label]="'Values'" [for]="'cond-values-' + $index" [hint]="valuesHint(condition)">
                  @if (condition.field === 'Category') {
                    <select
                      khControl
                      [id]="'cond-values-' + $index"
                      multiple
                      size="5"
                      (change)="setConditionValues($index, $any($event.target))"
                    >
                      @for (option of categoryOptions(); track option.id) {
                        <option [value]="option.id" [selected]="condition.values.includes(option.id)">
                          {{ option.label }}
                        </option>
                      }
                    </select>
                  } @else {
                    <input
                      khControl
                      [id]="'cond-values-' + $index"
                      type="text"
                      [value]="condition.values"
                      (input)="setCondition($index, 'values', $any($event.target).value)"
                    />
                  }
                </kh-field>

                <button khButton type="button" size="sm" variant="tertiary" (click)="removeCondition($index)">
                  Remove this condition
                </button>
              </div>
            }

            <button khButton type="button" size="sm" (click)="addCondition()">
              <kh-icon name="plus" size="sm" />
              Add a condition
            </button>

            <div class="row">
              <kh-field label="Order" for="rule-sort">
                <select
                  khControl
                  id="rule-sort"
                  [value]="sort()"
                  (change)="sort.set($any($event.target).value)"
                >
                  @for (choice of sorts; track choice.value) {
                    <option [value]="choice.value">{{ choice.label }}</option>
                  }
                </select>
              </kh-field>
              <kh-field label="At most" for="rule-limit" hint="How many rows the rule may fill.">
                <input
                  khControl
                  id="rule-limit"
                  type="number"
                  min="1"
                  [value]="limit()"
                  (input)="limit.set($any($event.target).value)"
                />
              </kh-field>
            </div>

            <kh-checkbox
              label="Include products that are out of stock"
              inputId="rule-oos"
              [checked]="includeOutOfStock()"
              (checkedChange)="includeOutOfStock.set($event)"
            />

            <div class="actions">
              <button khButton type="button" variant="primary" [disabled]="busy()" (click)="saveRule()">
                Save the rule
              </button>
              @if (collection()?.kind === 'Rule') {
                <button
                  khButton
                  type="button"
                  variant="tertiary"
                  [disabled]="busy()"
                  (click)="clearingRule.set(true)"
                >
                  Make it manual
                </button>
              }
            </div>
          </section>

          <section class="panel">
            <h2>The collection</h2>

            <kh-field label="Name" for="collection-name">
              <input
                khControl
                id="collection-name"
                type="text"
                maxlength="160"
                [value]="name()"
                (input)="name.set($any($event.target).value)"
              />
            </kh-field>

            <kh-field label="URL slug" for="collection-slug">
              <input
                khControl
                id="collection-slug"
                type="text"
                [value]="slug()"
                (input)="slug.set($any($event.target).value)"
              />
            </kh-field>

            <kh-field label="Description" for="collection-description" [optional]="true">
              <textarea
                khControl
                id="collection-description"
                rows="3"
                [value]="description()"
                (input)="description.set($any($event.target).value)"
              ></textarea>
            </kh-field>

            <div class="hero">
              <span class="hero-label">Hero image</span>
              <span class="note">{{ heroFileId() ? 'Chosen' : 'None' }}</span>
              <div class="hero-actions">
                <button khButton type="button" size="sm" (click)="pickerOpen.set(true)">Choose</button>
                @if (heroFileId()) {
                  <button khButton type="button" size="sm" variant="tertiary" (click)="heroFileId.set(null)">
                    Remove
                  </button>
                }
              </div>
            </div>

            <kh-field label="Meta title" for="collection-meta-title" [optional]="true">
              <input
                khControl
                id="collection-meta-title"
                type="text"
                maxlength="200"
                [value]="metaTitle()"
                (input)="metaTitle.set($any($event.target).value)"
              />
            </kh-field>

            <kh-field label="Meta description" for="collection-meta-description" [optional]="true">
              <textarea
                khControl
                id="collection-meta-description"
                rows="3"
                [value]="metaDescription()"
                (input)="metaDescription.set($any($event.target).value)"
              ></textarea>
            </kh-field>

            <kh-checkbox
              label="Live on the storefront"
              inputId="collection-active"
              [checked]="isActive()"
              (checkedChange)="isActive.set($event)"
            />
            <kh-checkbox
              label="Listed in collection directories"
              inputId="collection-listed"
              [checked]="isListed()"
              (checkedChange)="isListed.set($event)"
            />

            <button
              khButton
              type="button"
              variant="primary"
              class="save"
              *khHasPermission="'content.content.manage'"
              [disabled]="busy()"
              (click)="saveDetails()"
            >
              {{ busy() ? 'Saving…' : 'Save collection' }}
            </button>
          </section>
        </aside>
      </div>
    }

    <kh-media-picker
      [open]="pickerOpen()"
      [multiple]="false"
      ownerType="Collection"
      [ownerId]="id"
      (picked)="chooseHero($event)"
      (closed)="pickerOpen.set(false)"
    />

    <kh-confirm-dialog
      [open]="clearingRule()"
      heading="Make this collection manual"
      message="The rule is removed. The rows it has found stay, and nothing refreshes them again."
      confirmLabel="Make it manual"
      tone="warning"
      [busy]="busy()"
      (confirmed)="clearRule()"
      (cancelled)="clearingRule.set(false)"
    />

    <kh-confirm-dialog
      [open]="deleting()"
      heading="Delete this collection"
      message="Any menu item or banner pointing at it stops working. Hiding it instead keeps both."
      confirmLabel="Delete"
      [confirmPhrase]="collection()?.slug ?? null"
      [busy]="busy()"
      (confirmed)="remove()"
      (cancelled)="deleting.set(false)"
    />
  `,
  styles: `
    kh-alert {
      margin-block: var(--space-4);
    }

    .layout {
      display: grid;
      gap: var(--space-6);
      grid-template-columns: minmax(0, 1fr);
    }

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: minmax(0, 3fr) minmax(22rem, 2fr);
        align-items: start;
      }
    }

    h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
    }

    h3 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-base);
    }

    aside {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    .panel {
      margin-block-start: var(--space-4);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    aside .panel {
      margin-block-start: 0;
    }

    .condition {
      margin-block: var(--space-3);
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
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

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin-block-start: var(--space-4);
    }

    .save {
      margin-block-start: var(--space-4);
    }

    .hint {
      margin: 0 0 var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .name {
      display: block;
      font-weight: var(--weight-medium);
    }

    .note {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .hero {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      flex-wrap: wrap;
      margin-block: var(--space-3);
    }

    .hero-label {
      font-weight: var(--weight-medium);
    }

    .hero-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin-inline-start: auto;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CollectionDetailPage {
  private readonly content = inject(ContentAdminService);
  private readonly catalog = inject(CatalogAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly ruleFields = RULE_FIELDS;
  protected readonly ruleOperators = RULE_OPERATORS;
  protected readonly sorts = COLLECTION_SORTS;
  protected readonly dateTime = tableDateTime;

  protected readonly id = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly collection = signal<CollectionResponse | null>(null);
  protected readonly items = this.content.collectionItems(this.id);

  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly summary = signal<readonly string[]>([]);
  protected readonly deleting = signal(false);
  protected readonly clearingRule = signal(false);
  protected readonly pickerOpen = signal(false);
  protected readonly pinProductId = signal('');

  protected readonly name = signal('');
  protected readonly slug = signal('');
  protected readonly description = signal('');
  protected readonly heroFileId = signal<string | null>(null);
  protected readonly metaTitle = signal('');
  protected readonly metaDescription = signal('');
  protected readonly isActive = signal(true);
  protected readonly isListed = signal(true);

  protected readonly matchAll = signal(true);
  protected readonly conditions = signal<readonly ConditionDraft[]>([]);
  protected readonly sort = signal<CollectionSort>('Newest');
  protected readonly limit = signal('48');
  protected readonly includeOutOfStock = signal(false);

  protected readonly categoryOptions = signal<readonly { id: string; label: string }[]>([]);

  protected readonly page = computed(() => ({
    nextCursor: this.items.nextCursor(),
    hasPrevious: this.items.hasPrevious(),
    size: this.items.size(),
    total: this.items.total(),
  }));

  protected readonly subtitle = computed(() => {
    const current = this.collection();
    if (!current) return null;
    return `/${current.slug} · ${current.itemCount} product${current.itemCount === 1 ? '' : 's'} · created ${tableDateTime(current.createdAt)}`;
  });

  /** Finds products for the picker (Step 28B, deliverable 15). */
  protected readonly productSearch = (term: string): Observable<readonly EntityOption[]> =>
    this.catalog.searchProducts(term).pipe(
      map((products) =>
        products.map((product) => ({
          id: product.id,
          label: product.name,
          hint: `${product.status} · /${product.slug}`,
        })),
      ),
    );

  protected readonly rowKey = (row: ProductCardResponse) => row.productId;
  protected readonly rowLabel = (row: ProductCardResponse) => row.name;

  protected readonly columns: readonly DataTableColumn<ProductCardResponse>[] = [
    { key: 'name', label: 'Product', kind: 'custom' },
    {
      key: 'price',
      label: 'Price',
      kind: 'number',
      value: (row) => tableMoney(row.price, row.currencyCode),
      width: '9rem',
    },
    {
      key: 'isPurchasable',
      label: 'Buyable',
      value: (row) => (row.isPurchasable ? 'Yes' : 'No'),
      width: '7rem',
    },
    { key: 'actions', label: '', kind: 'custom', width: '8rem' },
  ];

  constructor() {
    this.load();
    this.items.load();

    this.catalog.categoryTree(false).subscribe({
      next: (tree) => this.categoryOptions.set(flatten(tree, 0)),
      error: () => this.categoryOptions.set([]),
    });
  }

  // ---- The rule ---------------------------------------------------------------------------------

  protected valuesHint(condition: ConditionDraft): string {
    if (condition.field === 'Category') return 'Choose one or more';
    if (['Price', 'DiscountPercent', 'Rating', 'PublishedWithinDays'].includes(condition.field)) {
      return 'A number';
    }
    return 'Comma separated';
  }

  protected addCondition(): void {
    this.conditions.update((current) => [
      ...current,
      { field: 'Category', key: '', operator: 'In', values: '' },
    ]);
  }

  protected removeCondition(index: number): void {
    this.conditions.update((current) => current.filter((_condition, position) => position !== index));
  }

  protected setCondition(index: number, field: keyof ConditionDraft, value: string): void {
    this.conditions.update((current) =>
      current.map((condition, position) =>
        position === index ? { ...condition, [field]: value } : condition,
      ),
    );
  }

  protected setConditionValues(index: number, select: HTMLSelectElement): void {
    const chosen = Array.from(select.selectedOptions)
      .map((option) => option.value)
      .join(',');
    this.setCondition(index, 'values', chosen);
  }

  protected saveRule(): void {
    if (this.busy()) return;

    const conditions: RuleConditionBody[] = this.conditions()
      .map((condition) => ({
        field: condition.field,
        key: condition.field === 'Attribute' ? condition.key || null : null,
        operator: condition.operator,
        values: condition.values
          .split(',')
          .map((value) => value.trim())
          .filter((value) => value.length > 0),
      }))
      .filter((condition) => (condition.values ?? []).length > 0);

    this.busy.set(true);
    this.summary.set([]);

    this.content
      .setCollectionRule(this.id, {
        rule: {
          matchAll: this.matchAll(),
          conditions,
          sort: this.sort(),
          limit: Math.max(1, Number(this.limit()) || 48),
          includeOutOfStock: this.includeOutOfStock(),
        },
      })
      .subscribe({
        next: (saved) => {
          this.busy.set(false);
          this.fill(saved);
          this.items.refresh();
          this.toasts.success('Rule saved. The rows have been refilled.');
        },
        error: (error: unknown) => this.fail(error, 'The rule could not be saved.'),
      });
  }

  protected clearRule(): void {
    this.busy.set(true);
    this.content.setCollectionRule(this.id, { rule: null }).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.clearingRule.set(false);
        this.fill(saved);
        this.toasts.success('This collection is manual now.');
      },
      error: (error: unknown) => {
        this.clearingRule.set(false);
        this.fail(error, 'The rule could not be removed.');
      },
    });
  }

  protected refresh(): void {
    this.busy.set(true);
    this.content.refreshCollection(this.id).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.fill(saved);
        this.items.refresh();
        this.toasts.success('Refreshed.');
      },
      error: (error: unknown) => this.fail(error, 'It could not be refreshed.'),
    });
  }

  // ---- Items ------------------------------------------------------------------------------------

  /**
   * Pins one product.
   *
   * One request that adds one row. It used to send the rows on screen back with the new one,
   * because the only route was the whole-list replace — which was correct for the fifty rows in
   * view and silently deleted every row beyond them. The add and remove routes landed at Step 28B
   * (deliverable 9), and this screen is why.
   */
  protected pin(): void {
    const productId = this.pinProductId().trim();
    if (!productId || this.busy()) return;

    this.apply(this.content.addCollectionItem(this.id, { productId, isPinned: true }));
  }

  /** Takes one product out, leaving the rest alone. */
  protected unpin(row: ProductCardResponse): void {
    if (this.busy()) return;

    this.apply(this.content.removeCollectionItem(this.id, row.productId));
  }

  /** Runs a membership change and folds its answer back into the screen. */
  private apply(change: Observable<CollectionResponse>): void {
    this.busy.set(true);
    this.summary.set([]);

    change.subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.pinProductId.set('');
        this.fill(saved);
        this.items.refresh();
      },
      error: (error: unknown) => this.fail(error, 'The products could not be changed.'),
    });
  }

  // ---- Details ----------------------------------------------------------------------------------

  protected chooseHero(files: readonly MediaFileResponse[]): void {
    this.pickerOpen.set(false);
    const file = files[0];
    if (file) this.heroFileId.set(file.id);
  }

  protected saveDetails(): void {
    if (this.busy()) return;

    this.busy.set(true);
    this.summary.set([]);

    this.content
      .updateCollection(this.id, {
        slug: this.slug() || null,
        name: this.name() || null,
        description: this.description() || null,
        seo: {
          metaTitle: this.metaTitle() || null,
          metaDescription: this.metaDescription() || null,
          metaKeywords: null,
          canonicalUrl: null,
          ogTitle: null,
          ogDescription: null,
          ogImageFileId: null,
          ogType: null,
          noIndex: false,
          noFollow: false,
          sitemapPriority: null,
        },
        heroImageFileId: this.heroFileId(),
        isActive: this.isActive(),
        isListed: this.isListed(),
      })
      .subscribe({
        next: (saved) => {
          this.busy.set(false);
          this.fill(saved);
          this.toasts.success('Collection saved.');
        },
        error: (error: unknown) => this.fail(error, 'It could not be saved.'),
      });
  }

  protected remove(): void {
    this.busy.set(true);
    this.content.deleteCollection(this.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.deleting.set(false);
        this.toasts.success('Collection deleted.');
        void this.router.navigate(['/content/collections']);
      },
      error: (error: unknown) => {
        this.deleting.set(false);
        this.fail(error, 'It could not be deleted.');
      },
    });
  }

  // ---- Loading ----------------------------------------------------------------------------------

  private load(): void {
    this.loading.set(true);
    this.content.collection(this.id).subscribe({
      next: (loaded) => {
        this.loading.set(false);
        this.fill(loaded);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'That collection could not be loaded.'));
      },
    });
  }

  private fill(loaded: CollectionResponse): void {
    this.collection.set(loaded);
    this.name.set(loaded.name);
    this.slug.set(loaded.slug);
    this.description.set(loaded.description ?? '');
    this.heroFileId.set(loaded.heroImage?.fileId ?? null);
    this.metaTitle.set(loaded.seo.metaTitle ?? '');
    this.metaDescription.set(loaded.seo.metaDescription ?? '');
    this.isActive.set(loaded.isActive);
    this.isListed.set(loaded.isListed);

    const rule = loaded.rule;
    if (rule) {
      this.matchAll.set(rule.matchAll);
      this.sort.set(rule.sort);
      this.limit.set(String(rule.limit));
      this.includeOutOfStock.set(rule.includeOutOfStock);
      this.conditions.set(
        rule.conditions.map((condition) => ({
          field: condition.field,
          key: condition.key ?? '',
          operator: condition.operator,
          values: condition.values.join(','),
        })),
      );
    }
  }

  private fail(error: unknown, fallback: string): void {
    this.busy.set(false);
    const errors = fieldErrors(error);
    this.summary.set(
      errors ? Object.values(errors).flatMap((messages) => [...messages]) : [describeError(error, fallback)],
    );
  }
}

function flatten(nodes: readonly CategoryNode[], depth: number): { id: string; label: string }[] {
  return nodes.flatMap((node) => [
    { id: node.id, label: `${'— '.repeat(depth)}${node.name}` },
    ...flatten(node.children ?? [], depth + 1),
  ]);
}
