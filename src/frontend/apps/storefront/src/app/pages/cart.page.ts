import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CartStore } from '@klarahome/data-access-cart';
import { MoneyPipe } from '@klarahome/i18n';
import { Alert, Button, Control, EmptyState, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { CartLine, CartLineChange, CartLineView, OrderSummary, StickyAction } from '@klarahome/ui-patterns';
import { AnalyticsEvents, AnalyticsService, LiveAnnouncer, ToastService } from '@klarahome/util';

import { describeError } from '../core/describe-error';
import { CommerceMapper } from '../core/commerce.mapper';

/**
 * The cart — `/cart`.
 *
 * Client-rendered, like everything personal: there is nothing to index, the content is per-customer
 * and rendering it on the server would put a basket into a document the edge may cache
 * (`app.routes.server.ts`).
 *
 * Three things carry the page:
 *
 *  - **The basket is grouped by seller**, because that is what it becomes. A cart with three
 *    sellers is three sub-orders, three parcels and three dispatch promises (Step 14); a flat list
 *    would make the three delivery charges on the summary look like an error.
 *  - **Every change is optimistic, with a reason on refusal.** `CartStore` moves the line, the
 *    request goes out, and a rejection puts back exactly what was there along with a message —
 *    `PRICE_CHANGED` and `CART_ITEM_OUT_OF_STOCK` are things the shopper has to be told, in words,
 *    on the line they concern (docs/05-frontend-architecture.md §3.6).
 *  - **Checkout is gated by the API's verdict**, `isReadyForCheckout`, not by a rule reimplemented
 *    here. It knows about stock, serviceability and minimum order values, and a client that decided
 *    for itself would either block a valid basket or send one that is refused a screen later.
 *
 * Saved-for-later is a second list rather than a filter on the first: it is where a shopper puts
 * something they are not buying today, and a "saved" flag inside the basket total is a total nobody
 * can reconcile.
 */
@Component({
  selector: 'kh-cart-page',
  imports: [
    Alert,
    Button,
    CartLine,
    Control,
    EmptyState,
    Field,
    Icon,
    MoneyPipe,
    OrderSummary,
    RouterLink,
    Skeleton,
    StickyAction,
  ],
  template: `
    <h1>Your cart</h1>

    @if (store.isLoading() && !store.hasLoaded()) {
      <div class="loading">
        <kh-skeleton height="6rem" />
        <kh-skeleton height="6rem" />
        <kh-skeleton height="12rem" />
      </div>
    } @else if (store.isEmpty()) {
      <kh-empty-state
        heading="Your cart is empty"
        message="Everything you add will be kept here, on every device you sign in on."
      >
        <a khButton variant="primary" routerLink="/">Start shopping</a>
      </kh-empty-state>
    } @else {
      <div class="layout">
        <div class="lines">
          @for (issue of blockingIssues(); track issue.code) {
            <kh-alert tone="danger" [heading]="'We cannot check out yet'">{{ issue.message }}</kh-alert>
          }

          @for (group of groups(); track group.vendorId) {
            <section class="group">
              <h2>
                {{ group.sellerName }}
                <span class="dispatch">Dispatched in {{ dispatchLabel(group.dispatchHours) }}</span>
              </h2>

              @for (line of linesFor(group.lineIds); track line.id) {
                <kh-cart-line
                  [line]="line"
                  [busy]="store.isPending(line.id)"
                  (quantityChanged)="changeQuantity($event)"
                  (removed)="remove($event)"
                  (savedToggled)="toggleSaved($event, true)"
                />
              }
            </section>
          }

          @if (savedForLater().length > 0) {
            <section class="group saved">
              <h2>Saved for later</h2>
              @for (line of savedForLater(); track line.id) {
                <kh-cart-line
                  [line]="line"
                  [busy]="store.isPending(line.id)"
                  (quantityChanged)="changeQuantity($event)"
                  (removed)="remove($event)"
                  (savedToggled)="toggleSaved($event, false)"
                />
              }
            </section>
          }
        </div>

        <aside class="summary">
          <form class="coupon" (submit)="applyCoupon($event)">
            @if (store.couponCode(); as code) {
              <p class="applied">
                <kh-icon name="check" size="sm" />
                <span
                  ><strong>{{ code }}</strong> applied</span
                >
                <button khButton variant="tertiary" size="sm" type="button" (click)="removeCoupon()">
                  Remove
                </button>
              </p>
            } @else {
              <kh-field
                label="Have a coupon?"
                for="coupon-code"
                [error]="couponError()"
                hint="Enter the code exactly as it was given to you."
              >
                <input
                  khControl
                  id="coupon-code"
                  type="text"
                  autocomplete="off"
                  maxlength="32"
                  [khInvalid]="!!couponError()"
                  [value]="couponInput()"
                  (input)="couponInput.set($any($event.target).value.toUpperCase())"
                />
              </kh-field>
              <button
                khButton
                variant="secondary"
                type="submit"
                [disabled]="store.isCouponBusy() || couponInput().trim().length === 0"
              >
                {{ store.isCouponBusy() ? 'Checking…' : 'Apply' }}
              </button>
            }
          </form>

          @if (summary(); as details) {
            <kh-order-summary [summary]="details">
              <a khButton variant="primary" [block]="true" routerLink="/checkout" class="checkout-desktop">
                Proceed to checkout
              </a>
            </kh-order-summary>
          } @else {
            <kh-skeleton height="12rem" />
          }
        </aside>
      </div>

      <!-- The primary action stays in the thumb zone while the basket scrolls
           (docs/05-frontend-architecture.md §3.3). The same button is repeated inside the summary
           panel above, where it is the natural place for it from the 'lg' breakpoint. -->
      <ng-template khStickyAction>
        <div class="bar">
          <span class="bar-total">
            @if (grandTotal(); as amount) {
              <strong>{{ amount | khMoney }}</strong>
              <span>{{ store.itemCount() }} {{ store.itemCount() === 1 ? 'item' : 'items' }}</span>
            }
          </span>
          <a
            khButton
            variant="primary"
            routerLink="/checkout"
            [attr.aria-disabled]="store.isReadyForCheckout() ? null : 'true'"
            (click)="guardCheckout($event)"
          >
            Checkout
          </a>
        </div>
      </ng-template>
    }
  `,
  styles: `
    :host {
      display: block;
      padding-block: var(--space-4) var(--space-10);
    }

    h1 {
      font-size: var(--text-2xl);
    }

    .loading {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    .layout {
      display: grid;
      gap: var(--space-6);
    }

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: minmax(0, 1fr) 22rem;
        align-items: start;
      }
    }

    .group {
      margin-block-end: var(--space-6);
    }

    .group h2 {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: var(--space-2);
      margin: 0 0 var(--space-2);
      font-size: var(--text-base);
    }

    .dispatch {
      font-size: var(--text-xs);
      font-weight: var(--weight-regular);
      color: var(--color-text-muted);
    }

    .saved h2 {
      padding-block-start: var(--space-4);
      border-block-start: 1px solid var(--color-border);
    }

    .summary {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    .coupon {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
    }

    .applied {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      margin: 0;
      font-size: var(--text-sm);
      color: var(--color-success);
    }

    .checkout-desktop {
      margin-block-start: var(--space-4);
    }

    /* Below 'lg' the sticky bar carries the action, so the one in the panel would be a second
       identical button a few centimetres above it. */
    @media (max-width: 1023px) {
      .checkout-desktop {
        display: none;
      }
    }

    .bar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      inline-size: 100%;
    }

    .bar-total {
      display: flex;
      flex-direction: column;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .bar-total strong {
      font-size: var(--text-base);
      color: var(--color-text);
      font-variant-numeric: tabular-nums;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CartPage {
  protected readonly store = inject(CartStore);
  private readonly mapper = inject(CommerceMapper);
  private readonly toasts = inject(ToastService);
  private readonly announcer = inject(LiveAnnouncer);
  private readonly analytics = inject(AnalyticsService);

  protected readonly couponInput = signal('');
  protected readonly couponError = computed(() => this.store.couponRejection());

  protected readonly groups = computed(() =>
    this.store.groups().map((group) => this.mapper.cartGroup(group, this.store.currencyCode())),
  );

  private readonly lineViews = computed(() =>
    this.store.lines().map((line) => this.mapper.cartLine(line, this.store.currencyCode())),
  );

  protected readonly savedForLater = computed(() =>
    this.store.savedForLater().map((line) => this.mapper.cartLine(line, this.store.currencyCode())),
  );

  /** Basket-level problems that stop a checkout. Line-level ones are drawn on their own line. */
  protected readonly blockingIssues = computed(() => this.store.issues().filter((issue) => issue.isBlocking));

  protected readonly summary = computed(() => {
    const quote = this.store.quote();
    return quote ? this.mapper.summaryFromQuote(quote) : null;
  });

  protected readonly grandTotal = computed(() => this.summary()?.total ?? null);

  constructor() {
    this.store.load();
    this.analytics.track(AnalyticsEvents.viewCart);
  }

  /** The lines of one seller's group, in the order the group named them. */
  protected linesFor(lineIds: readonly string[]): readonly CartLineView[] {
    const byId = new Map(this.lineViews().map((line) => [line.id, line]));
    return lineIds.map((id) => byId.get(id)).filter((line): line is CartLineView => Boolean(line));
  }

  protected dispatchLabel(hours: number): string {
    if (hours <= 0) return 'the same day';
    if (hours < 24) return `${hours} hours`;
    const days = Math.round(hours / 24);
    return `${days} ${days === 1 ? 'day' : 'days'}`;
  }

  protected changeQuantity(change: CartLineChange): void {
    this.store.updateQuantity(change.lineId, change.quantity).subscribe({
      next: () => this.announcer.announce(`Quantity updated to ${change.quantity}.`),
      error: (error: unknown) =>
        this.toasts.warning(describeError(error, 'We could not change that quantity.')),
    });
  }

  protected remove(lineId: string): void {
    const line = this.lineViews().find((candidate) => candidate.id === lineId);

    this.store.remove(lineId).subscribe({
      next: () => {
        this.announcer.announce(`${line?.name ?? 'Item'} removed from your cart.`);
        this.analytics.track(AnalyticsEvents.removeFromCart, { item_name: line?.name });
      },
      error: (error: unknown) => this.toasts.warning(describeError(error, 'We could not remove that item.')),
    });
  }

  protected toggleSaved(lineId: string, save: boolean): void {
    this.store.setSavedForLater(lineId, save).subscribe({
      next: () => this.announcer.announce(save ? 'Saved for later.' : 'Moved back to your cart.'),
      error: (error: unknown) => this.toasts.warning(describeError(error, 'We could not move that item.')),
    });
  }

  protected applyCoupon(event: Event): void {
    event.preventDefault();
    const code = this.couponInput().trim();
    if (!code) return;

    this.store.applyCoupon(code).subscribe({
      next: (cart) => {
        // A code that did not apply is a 200 with a reason on the quote, so success here does not
        // mean the discount happened — the panel says which, and the box is only cleared when it did.
        if (cart.couponCode) {
          this.couponInput.set('');
          this.toasts.success(`${code} applied.`);
        }
      },
      error: () => undefined,
    });
  }

  protected removeCoupon(): void {
    this.store.removeCoupon().subscribe({ error: () => undefined });
  }

  /**
   * Stops a checkout the API would refuse.
   *
   * The link is `aria-disabled` rather than removed, so a shopper can see the action and read why
   * it is unavailable — a missing button explains nothing. The click is intercepted rather than the
   * `routerLink` being conditional, which keeps the markup one element.
   */
  protected guardCheckout(event: Event): void {
    if (this.store.isReadyForCheckout()) {
      this.analytics.track(AnalyticsEvents.beginCheckout, { value: this.grandTotal()?.amount });
      return;
    }

    event.preventDefault();
    const issue = this.blockingIssues()[0];
    this.toasts.warning(issue?.message ?? 'Something in your cart needs attention before you can check out.');
  }
}
