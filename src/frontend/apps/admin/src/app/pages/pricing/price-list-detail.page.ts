import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import {
  CatalogAdminService,
  EffectivePrice,
  PriceListItemPayload,
  PriceListItemResponse,
  PriceListResponse,
  PricingAdminService,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  CellTemplate,
  DataTable,
  DataTableColumn,
  EntityOption,
  EntityPicker,
  PageHeader,
} from '@klarahome/ui-admin';
import { Alert, Button, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { Observable, map } from 'rxjs';

import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime, tableMoney } from '../../core/format';

/** One row being added or edited before it is sent. */
interface DraftItem {
  listingId: string;
  price: string;
  minQuantity: string;
}

/**
 * The prices in one list.
 *
 * **A row is keyed on the listing *and* the quantity**, which is what makes this a table rather
 * than a form: one listing routinely carries three rows — a unit price, a price from ten, a price
 * from fifty — and an editor that treated the listing as the key would silently overwrite two of
 * them. The drafts panel therefore always asks for a minimum quantity, defaulted to 1.
 *
 * **Rows are upserted, never replaced wholesale.** The endpoint takes a set of rows and merges
 * them, so a list of nine thousand prices cannot be blanked by a screen that sends a short array.
 * That also means adding twenty prices is one request, which is what a spreadsheet paste needs.
 *
 * **The resolver is beside the editor on purpose.** "What does this listing actually sell for" is
 * not answerable from this list alone: another list may have a lower priority, and a promotion may
 * take more off afterwards. `resolve` asks the engine, and the answer names the list that won —
 * which is usually the moment somebody discovers their sale price is being beaten by a base list
 * with a lower number in it.
 */
@Component({
  selector: 'kh-price-list-detail-page',
  imports: [
    Alert,
    Button,
    CellTemplate,
    Control,
    DataTable,
    EntityPicker,
    Field,
    HasPermission,
    Icon,
    PageHeader,
    Skeleton,
  ],
  template: `
    <kh-page-header
      [heading]="priceList()?.name ?? 'Price list'"
      [description]="subtitle()"
      [crumbs]="[{ label: 'Price lists', path: '/price-lists' }]"
    />

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="This price list could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="16rem" />
    } @else {
      <div class="layout">
        <section>
          @if (items.error(); as message) {
            <kh-alert tone="danger" heading="The prices could not be loaded">{{ message }}</kh-alert>
          }

          @if (actionError(); as message) {
            <kh-alert tone="danger" heading="That did not take">{{ message }}</kh-alert>
          }

          <kh-data-table
            label="Prices"
            [columns]="columns"
            [rows]="items.rows()"
            [rowKey]="rowKey"
            [loading]="items.loading()"
            [page]="page()"
            exportMode="page"
            emptyMessage="This price list has no prices in it yet."
            (nextPage)="items.next()"
            (previousPage)="items.previous()"
          >
            <ng-template khCell="actions" let-row>
              <ng-container *khHasPermission="'pricing.price-list.manage'">
                <button khButton type="button" size="sm" variant="tertiary" (click)="edit(row)">Edit</button>
                <button
                  khButton
                  type="button"
                  size="sm"
                  variant="tertiary"
                  [disabled]="busyId() === row.id"
                  (click)="remove(row)"
                >
                  Remove
                </button>
              </ng-container>
            </ng-template>
          </kh-data-table>
        </section>

        <aside>
          <section class="panel" *khHasPermission="'pricing.price-list.manage'">
            <h2>Add or change prices</h2>
            <p class="hint">
              Rows are merged on listing and minimum quantity. Sending an existing pair changes it; a new pair
              adds it.
            </p>

            @for (draft of drafts(); track $index) {
              <div class="row">
                <kh-entity-picker
                  [label]="'Listing'"
                  [inputId]="'draft-listing-' + $index"
                  [search]="listingSearch"
                  (chose)="setDraft($index, 'listingId', $event?.id ?? '')"
                />
                <kh-field [label]="'Price'" [for]="'draft-price-' + $index">
                  <input
                    khControl
                    [id]="'draft-price-' + $index"
                    type="number"
                    min="0"
                    step="0.01"
                    [value]="draft.price"
                    (input)="setDraft($index, 'price', $any($event.target).value)"
                  />
                </kh-field>
                <kh-field [label]="'From units'" [for]="'draft-qty-' + $index">
                  <input
                    khControl
                    [id]="'draft-qty-' + $index"
                    type="number"
                    min="1"
                    [value]="draft.minQuantity"
                    (input)="setDraft($index, 'minQuantity', $any($event.target).value)"
                  />
                </kh-field>
                <button khButton type="button" size="sm" variant="tertiary" (click)="removeDraft($index)">
                  Remove
                </button>
              </div>
            }

            <div class="actions">
              <button khButton type="button" size="sm" (click)="addDraft()">
                <kh-icon name="plus" size="sm" />
                Another row
              </button>
              <button khButton type="button" variant="primary" [disabled]="saving()" (click)="saveDrafts()">
                {{ saving() ? 'Saving…' : 'Save these prices' }}
              </button>
            </div>
          </section>

          <section class="panel">
            <h2>What does a listing actually sell for?</h2>
            <p class="hint">
              Asks the pricing engine, across every list. The answer names the list that won, which is not
              always this one.
            </p>

            <div class="row">
              <kh-entity-picker
                label="Listing"
                inputId="resolve-listing"
                hint="Search by SKU or product name."
                [search]="listingSearch"
                (chose)="resolveListingId.set($event?.id ?? '')"
              />
              <kh-field label="Units" for="resolve-qty">
                <input
                  khControl
                  id="resolve-qty"
                  type="number"
                  min="1"
                  [value]="resolveQuantity()"
                  (input)="resolveQuantity.set($any($event.target).value)"
                />
              </kh-field>
            </div>

            <button khButton type="button" [disabled]="resolving()" (click)="resolve()">
              {{ resolving() ? 'Asking…' : 'Resolve' }}
            </button>

            @if (resolveError(); as message) {
              <kh-alert tone="danger" heading="It could not be resolved">{{ message }}</kh-alert>
            }

            @if (resolved(); as answer) {
              <dl class="answer">
                <dt>Sells for</dt>
                <dd>{{ money(answer.unitPrice, answer.currencyCode) }}</dd>
                <dt>MRP</dt>
                <dd>{{ money(answer.mrp, answer.currencyCode) }}</dd>
                <dt>From the list</dt>
                <dd>{{ answer.priceListName ?? "the offer's own price" }}</dd>
                <dt>At the tier</dt>
                <dd>from {{ answer.minQuantity }} unit{{ answer.minQuantity === 1 ? '' : 's' }}</dd>
              </dl>
              @if (answer.priceListId && answer.priceListId !== id) {
                <kh-alert tone="warning" heading="Another list is winning">
                  {{ answer.priceListName }} has a lower priority number than this one, so its price is the
                  one a shopper sees.
                </kh-alert>
              }
            }
          </section>
        </aside>
      </div>
    }
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
        grid-template-columns: minmax(0, 3fr) minmax(20rem, 2fr);
        align-items: start;
      }
    }

    aside {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
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

    .row {
      display: flex;
      gap: var(--space-2);
      align-items: flex-end;
      flex-wrap: wrap;
      margin-block-end: var(--space-2);
    }

    .row > kh-field {
      flex: 1 1 7rem;
    }

    .actions {
      display: flex;
      gap: var(--space-2);
      justify-content: space-between;
      margin-block-start: var(--space-3);
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
export class PriceListDetailPage {
  private readonly pricing = inject(PricingAdminService);
  private readonly catalog = inject(CatalogAdminService);

  /** Finds listings for the resolver's picker (Step 28B, deliverable 15). */
  protected readonly listingSearch = (term: string): Observable<readonly EntityOption[]> =>
    this.catalog.searchListings(term).pipe(
      map((listings) =>
        listings.map((listing) => ({
          id: listing.id,
          label: listing.productName,
          hint: `${listing.sku} · ${listing.status}`,
        })),
      ),
    );
  private readonly route = inject(ActivatedRoute);
  private readonly toasts = inject(ToastService);

  protected readonly id = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly priceList = signal<PriceListResponse | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly busyId = signal<string | null>(null);
  protected readonly saving = signal(false);

  protected readonly items = this.pricing.priceListItems(this.id);

  protected readonly drafts = signal<readonly DraftItem[]>([{ listingId: '', price: '', minQuantity: '1' }]);

  protected readonly resolveListingId = signal('');
  protected readonly resolveQuantity = signal('1');
  protected readonly resolving = signal(false);
  protected readonly resolveError = signal<string | null>(null);
  protected readonly resolved = signal<EffectivePrice | null>(null);

  protected readonly page = computed(() => ({
    nextCursor: this.items.nextCursor(),
    hasPrevious: this.items.hasPrevious(),
    size: this.items.size(),
    total: this.items.total(),
  }));

  protected readonly subtitle = computed(() => {
    const current = this.priceList();
    if (!current) return null;
    const window =
      current.startsAt || current.endsAt
        ? ` · ${current.startsAt ? tableDateTime(current.startsAt) : 'always'} → ${current.endsAt ? tableDateTime(current.endsAt) : 'no end'}`
        : '';
    return `${current.code} · ${current.type} · priority ${current.priority}${window}`;
  });

  protected readonly rowKey = (row: PriceListItemResponse) => row.id;

  protected readonly columns: readonly DataTableColumn<PriceListItemResponse>[] = [
    { key: 'listingId', label: 'Listing', value: (row) => row.listingId },
    { key: 'price', label: 'Price', kind: 'number', value: (row) => tableMoney(row.price), width: '9rem' },
    {
      key: 'minQuantity',
      label: 'From units',
      kind: 'number',
      value: (row) => row.minQuantity,
      width: '7rem',
    },
    { key: 'actions', label: '', kind: 'custom', width: '10rem' },
  ];

  constructor() {
    this.load();
    this.items.load();
  }

  protected money(amount: number, currency: string): string {
    return tableMoney(amount, currency);
  }

  // ---- Drafts -----------------------------------------------------------------------------------

  protected addDraft(): void {
    this.drafts.update((current) => [...current, { listingId: '', price: '', minQuantity: '1' }]);
  }

  protected removeDraft(index: number): void {
    this.drafts.update((current) => current.filter((_draft, position) => position !== index));
  }

  protected setDraft(index: number, field: keyof DraftItem, value: string): void {
    this.drafts.update((current) =>
      current.map((draft, position) => (position === index ? { ...draft, [field]: value } : draft)),
    );
  }

  /** Loads an existing row into the drafts panel, so changing a price is the same act as adding one. */
  protected edit(row: PriceListItemResponse): void {
    this.drafts.update((current) => [
      { listingId: row.listingId, price: String(row.price), minQuantity: String(row.minQuantity) },
      ...current.filter((draft) => draft.listingId.trim().length > 0),
    ]);
  }

  protected saveDrafts(): void {
    const items: PriceListItemPayload[] = this.drafts()
      .filter((draft) => draft.listingId.trim().length > 0 && draft.price.trim().length > 0)
      .map((draft) => ({
        listingId: draft.listingId.trim(),
        price: Number(draft.price) || 0,
        minQuantity: Math.max(1, Number(draft.minQuantity) || 1),
      }));

    if (items.length === 0) {
      this.actionError.set('Fill in a listing and a price first.');
      return;
    }

    this.saving.set(true);
    this.actionError.set(null);

    this.pricing.upsertPriceListItems(this.id, { items }).subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.drafts.set([{ listingId: '', price: '', minQuantity: '1' }]);
        this.toasts.success(`${saved.length} price${saved.length === 1 ? '' : 's'} saved.`);
        this.items.refresh();
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.actionError.set(describeError(error, 'Those prices could not be saved.'));
      },
    });
  }

  protected remove(row: PriceListItemResponse): void {
    this.busyId.set(row.id);
    this.actionError.set(null);

    this.pricing.deletePriceListItem(this.id, row.id).subscribe({
      next: () => {
        this.busyId.set(null);
        this.items.refresh();
        this.load();
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.actionError.set(describeError(error, 'That price could not be removed.'));
      },
    });
  }

  // ---- The resolver -----------------------------------------------------------------------------

  protected resolve(): void {
    const listingId = this.resolveListingId().trim();
    if (!listingId) {
      this.resolveError.set('Give a listing id to resolve.');
      return;
    }

    this.resolving.set(true);
    this.resolveError.set(null);

    this.pricing.resolvePrice(listingId, Math.max(1, Number(this.resolveQuantity()) || 1)).subscribe({
      next: (answer) => {
        this.resolving.set(false);
        this.resolved.set(answer);
      },
      error: (error: unknown) => {
        this.resolving.set(false);
        this.resolved.set(null);
        this.resolveError.set(describeError(error, 'No price could be resolved for that listing.'));
      },
    });
  }

  private load(): void {
    this.loading.set(this.priceList() === null);
    this.pricing.priceList(this.id).subscribe({
      next: (list) => {
        this.loading.set(false);
        this.priceList.set(list);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'That price list could not be loaded.'));
      },
    });
  }
}
