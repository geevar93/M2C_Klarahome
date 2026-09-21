import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { AddressBookStore, ProfileStore } from '@klarahome/data-access-account';
import { CartStore } from '@klarahome/data-access-cart';
import { CheckoutStore, PaymentHandoff } from '@klarahome/data-access-checkout';
import { ReferenceDataService } from '@klarahome/data-access-content';
import {
  Alert,
  Button,
  Disclosure,
  Drawer,
  ErrorState,
  PageHeader,
  Skeleton,
} from '@klarahome/ui-primitives';
import {
  AddressCard,
  AddressForm,
  AddressFormValue,
  CartLine,
  OrderSummary,
  PaymentMethodSelector,
  PincodePlaceView,
  ShippingChoice,
  ShippingOptionSelector,
  StickyAction,
} from '@klarahome/ui-patterns';
import { MoneyPipe } from '@klarahome/i18n';
import {
  AnalyticsEvents,
  AnalyticsService,
  BrowserStorage,
  LiveAnnouncer,
  ToastService,
} from '@klarahome/util';
import { switchMap } from 'rxjs';

import { CommerceMapper } from '../../core/commerce.mapper';

/** Where the open checkout session is remembered, so a reload or a gateway bounce comes back to it. */
const SESSION_KEY = 'kh.checkout.session';

/**
 * Checkout — `/checkout`.
 *
 * **One page, not four steps.** Address, delivery, payment and the order summary are sections of
 * one scrolling page with "Place order" at the bottom. Each section is still a write to the same
 * **server-side session**, which answers with itself and a fresh quote every time — nothing about
 * the price is decided here. What changed is only that the shopper no longer taps "Continue" three
 * times to reach the button that matters: a returning customer with a saved address and one courier
 * per seller goes from the cart to an order in a single tap.
 *
 * **Defaults are chosen for the shopper, and shown, never hidden.** The default address is
 * preselected; the cheapest delivery service per seller is preselected as soon as the address is
 * known; the first available payment method is preselected once delivery is priced. Every one of
 * those is a visible control the shopper can change, so a default is a saved tap and not a decision
 * made behind their back.
 *
 * **Sections reveal in order.** Delivery appears once an address is set, payment once every seller
 * has a service — because the API cannot price either without the step before it, and an empty
 * "no delivery options" panel above an address form is a page that looks broken.
 *
 * **The session outlives the page.** Its id is kept in `sessionStorage`, so being bounced to the
 * payment gateway and back — or reloading on a stalled network — resumes the same checkout rather
 * than starting a new one. A remembered session that is no longer open is not resumed
 * (`CheckoutStore.start`).
 *
 * **Sign-in is required, and that is the API's rule** (`RequireAuthorization` on the whole
 * `/store/checkout` group). The route's `authenticatedGuard` carries the shopper to sign-in and
 * back with their basket merged. Guest checkout is on the roadmap; when the API opens a session
 * against an anonymous cart, this page gains a "Contact" section above the address and nothing
 * else about it changes.
 */
@Component({
  selector: 'kh-checkout-page',
  imports: [
    AddressCard,
    AddressForm,
    Alert,
    Button,
    CartLine,
    Disclosure,
    Drawer,
    ErrorState,
    MoneyPipe,
    OrderSummary,
    PageHeader,
    PaymentMethodSelector,
    RouterLink,
    ShippingOptionSelector,
    Skeleton,
    StickyAction,
  ],
  template: `
    <kh-page-header title="Checkout" backHref="/cart" backLabel="Back to cart" />

    @if (store.isStarting()) {
      <kh-skeleton height="20rem" />
    } @else if (!store.current()) {
      <kh-error-state
        heading="We could not open a checkout"
        message="Your cart may have changed since you last looked. Try again, or go back to your cart."
        (retry)="start()"
      />
      <p class="after-error">
        <a khButton variant="secondary" routerLink="/cart">Back to cart</a>
      </p>
    } @else {
      <div class="layout">
        <div class="sections">
          <!-- 1. Address -->
          <section class="block" aria-labelledby="checkout-address">
            <h2 id="checkout-address">Delivery address</h2>

            @if (addresses().length > 0) {
              <div class="addresses">
                @for (address of addresses(); track address.id) {
                  <kh-address-card
                    [address]="address"
                    [selectable]="true"
                    group="checkout-address"
                    [selected]="address.id === selectedAddressId()"
                    (chosen)="chooseAddress($event)"
                  />
                }
              </div>
              <button khButton variant="tertiary" size="sm" type="button" (click)="openAddressForm()">
                Add a new address
              </button>
            } @else if (addressBook.hasLoaded()) {
              <p class="muted">You have no saved addresses yet. Add the one this order should go to.</p>
              <button khButton variant="primary" type="button" (click)="openAddressForm()">
                Add an address
              </button>
            } @else {
              <kh-skeleton height="8rem" />
            }
          </section>

          <!-- 2. Delivery: only once the address is known, because the couriers depend on it. -->
          @if (store.shippingAddress()) {
            <section class="block" aria-labelledby="checkout-delivery">
              <h2 id="checkout-delivery">Delivery</h2>

              @if (shippingGroups().length === 0) {
                <kh-skeleton height="6rem" />
              } @else {
                @if (shippingGroups().length > 1) {
                  <p class="muted">Each seller ships separately, so each one has its own delivery service.</p>
                }
                <kh-shipping-option-selector [groups]="shippingGroups()" (chosen)="chooseShipping($event)" />
              }
            </section>
          }

          <!-- 3. Payment: only once delivery is priced, because the fee and the COD ceiling depend on it. -->
          @if (store.hasShipping()) {
            <section class="block" aria-labelledby="checkout-payment">
              <h2 id="checkout-payment">Payment</h2>

              @if (paymentMethods().length === 0 && !paymentMethodsLoaded()) {
                <kh-skeleton height="6rem" />
              } @else {
                <kh-payment-method-selector
                  [methods]="paymentMethods()"
                  [selected]="store.paymentMethod()"
                  legend="Payment method"
                  (chosen)="choosePayment($event)"
                />
                @if (codRefusal(); as reason) {
                  <kh-alert tone="info">{{ reason }}</kh-alert>
                }
              }
            </section>
          }

          <!-- 4. Items: collapsed, because the shopper has just seen them on the cart. -->
          <section class="block" aria-labelledby="checkout-items">
            <h2 id="checkout-items" class="items-head">
              <span>Your items</span>
              <a class="edit" routerLink="/cart">Edit cart</a>
            </h2>
            <kh-disclosure [heading]="itemsSummary()">
              @for (line of reviewLines(); track line.id) {
                <kh-cart-line [line]="line" [readOnly]="true" />
              }
            </kh-disclosure>
          </section>
        </div>

        <aside class="summary">
          @if (summary(); as details) {
            <kh-order-summary [summary]="details" heading="Order summary">
              @if (!store.hasShipping()) {
                <p class="note">Delivery is added once you choose an address and service.</p>
              }
              <div class="place">
                @if (store.error(); as message) {
                  <kh-alert #failure tone="danger" heading="That did not work" tabindex="-1">
                    {{ message }}
                  </kh-alert>
                }
                <button
                  khButton
                  variant="primary"
                  [block]="true"
                  class="place-desktop"
                  type="button"
                  [disabled]="!canPlace() || placing()"
                  (click)="placeOrder()"
                >
                  {{ placeLabel() }}
                </button>
                <p class="note">{{ placeHint() }}</p>
              </div>
            </kh-order-summary>
          } @else {
            <kh-skeleton height="12rem" />
          }
        </aside>
      </div>

      <!-- On a phone the action stays in the thumb zone while the sections scroll; from 'lg' the
           summary panel is beside the form and carries the same button, so the bar steps aside. -->
      <ng-template khStickyAction mobileOnly>
        <div class="bar">
          <span class="bar-total">
            @if (summary(); as details) {
              <strong>{{ details.total | khMoney }}</strong>
              <span>{{ store.hasShipping() ? 'Order total' : 'Excludes delivery' }}</span>
            }
          </span>
          <button
            khButton
            variant="primary"
            type="button"
            [disabled]="!canPlace() || placing()"
            (click)="placeOrder()"
          >
            {{ placeLabel() }}
          </button>
        </div>
      </ng-template>
    }

    <!-- The address form is a bottom sheet rather than a route: it is a detour inside one section,
         and a shopper who adds an address must come straight back to the checkout they were in. -->
    <kh-drawer
      [open]="addressFormOpen()"
      side="bottom"
      label="Add an address"
      (closed)="addressFormOpen.set(false)"
    >
      <div class="sheet">
        <h2>Add an address</h2>
        <kh-address-form
          [states]="states()"
          [place]="place()"
          [saving]="addressBook.isSaving()"
          (pincodeEntered)="lookUpPincode($event)"
          (saved)="saveAddress($event)"
          (cancelled)="addressFormOpen.set(false)"
        />
      </div>
    </kh-drawer>
  `,
  styles: `
    :host {
      display: block;
      padding-block: var(--space-4) var(--space-10);
    }

    h2 {
      margin: 0 0 var(--space-3);
      font-size: var(--text-lg);
    }

    .muted,
    .note {
      margin: 0 0 var(--space-3);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .after-error {
      margin-block-start: var(--space-4);
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

    .block {
      padding-block-end: var(--space-6);
      margin-block-end: var(--space-6);
      border-block-end: 1px solid var(--color-border);
    }

    .block:last-child {
      border-block-end: 0;
    }

    .addresses {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      margin-block-end: var(--space-3);
    }

    .block kh-alert {
      display: block;
      margin-block-start: var(--space-3);
    }

    .items-head {
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: var(--space-3);
    }

    .edit {
      font-size: var(--text-sm);
      font-weight: var(--weight-regular);
    }

    @media (min-width: 1024px) {
      .summary {
        position: sticky;
        top: var(--space-4);
      }
    }

    .place {
      margin-block-start: var(--space-4);
    }

    .place kh-alert {
      display: block;
      margin-block-end: var(--space-3);
    }

    .place .note {
      margin: var(--space-2) 0 0;
      text-align: center;
    }

    /* Mobile-first: below 'lg' the sticky bar carries the action; the panel's own button is
       restored from 'lg' up, exactly as the cart does it. */
    .place-desktop {
      display: none;
    }

    @media (min-width: 1024px) {
      .place-desktop {
        display: flex;
      }
    }

    .sheet {
      padding: var(--space-4);
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
export class CheckoutPage {
  protected readonly store = inject(CheckoutStore);
  protected readonly addressBook = inject(AddressBookStore);
  private readonly cart = inject(CartStore);
  private readonly profile = inject(ProfileStore);
  private readonly reference = inject(ReferenceDataService);
  private readonly payments = inject(PaymentHandoff);
  private readonly mapper = inject(CommerceMapper);
  private readonly storage = inject(BrowserStorage);
  private readonly toasts = inject(ToastService);
  private readonly announcer = inject(LiveAnnouncer);
  private readonly analytics = inject(AnalyticsService);
  private readonly router = inject(Router);

  private readonly failure = viewChild<ElementRef<HTMLElement>>('failure');

  protected readonly selectedAddressId = signal<string | null>(null);
  protected readonly addressFormOpen = signal(false);
  protected readonly place = signal<PincodePlaceView | null>(null);
  protected readonly placing = signal(false);
  protected readonly paymentMethodsLoaded = signal(false);

  private readonly stateRows = toSignal(this.reference.states(), { initialValue: [] });

  protected readonly states = computed(() =>
    this.stateRows().map((state) => ({ id: state.id, name: state.name })),
  );

  private readonly stateNames = computed(
    () => new Map(this.stateRows().map((state) => [state.id, state.name])),
  );

  protected readonly addresses = computed(() =>
    this.addressBook
      .all()
      .map((address) => this.mapper.address(address, this.stateNames().get(address.stateId))),
  );

  protected readonly shippingGroups = computed(() =>
    this.store.shippingOptions().map((group) => this.mapper.shippingGroup(group, this.store.currencyCode())),
  );

  protected readonly paymentMethods = computed(() =>
    this.store.paymentMethods().map((method) => this.mapper.paymentMethod(method, this.store.currencyCode())),
  );

  /** The API's own sentence for why cash on delivery is off, when it is. */
  protected readonly codRefusal = computed(
    () =>
      this.paymentMethods().find((method) => method.method === 'COD' && !method.isAvailable)?.reason ?? null,
  );

  protected readonly summary = computed(() => {
    const quote = this.store.quote();
    return quote ? this.mapper.summaryFromQuote(quote, { totalLabel: 'Order total' }) : null;
  });

  protected readonly reviewLines = computed(() => {
    const session = this.store.current();
    if (!session?.cart) return [];
    return session.cart.lines.map((line) => this.mapper.cartLine(line, session.currencyCode));
  });

  /** "3 items from 2 sellers" — the disclosure's heading, so a closed list still says what is in it. */
  protected readonly itemsSummary = computed(() => {
    const lines = this.reviewLines();
    const items = lines.reduce((sum, line) => sum + line.quantity, 0);
    const sellers = new Set(lines.map((line) => line.sellerName)).size;
    const itemWord = items === 1 ? 'item' : 'items';
    const sellerWord = sellers === 1 ? 'seller' : 'sellers';
    return `${items} ${itemWord} from ${sellers} ${sellerWord}`;
  });

  /**
   * Whether the session has everything the API needs. The server makes the final judgement on
   * "Place order"; this only keeps the button quiet until there is something to judge.
   */
  protected readonly canPlace = computed(() => {
    const method = this.store.paymentMethod();
    return this.store.hasShipping() && method !== null && method !== '';
  });

  protected readonly placeLabel = computed(() => (this.placing() ? 'Placing…' : 'Place order'));

  protected readonly placeHint = computed(() => {
    if (!this.store.shippingAddress()) return 'Choose a delivery address to continue';
    if (!this.store.hasShipping()) return 'Choose a delivery service for each seller';
    if (!this.canPlace()) return 'Choose how to pay';
    return 'You will not be charged until you confirm';
  });

  constructor() {
    this.addressBook.loadOnce();
    this.profile.loadOnce();
    this.analytics.track(AnalyticsEvents.beginCheckout);
    this.start();

    // Preselect the address the session already has, else the customer's default — and write it to
    // the session straight away, so the delivery section can appear without a tap. An effect rather
    // than a one-off because the address book and the session both arrive asynchronously, and
    // whichever lands second is the one that should decide.
    effect(() => {
      if (this.selectedAddressId() !== null || this.store.isStarting() || !this.store.current()) return;
      const fromSession = this.store.shippingAddress()?.sourceAddressId ?? null;
      const chosen = fromSession ?? this.addressBook.preferred()?.id ?? null;
      if (!chosen) return;
      this.selectedAddressId.set(chosen);
      if (fromSession) {
        // The session already knows the address. Only what follows from it needs reading.
        this.loadDelivery();
      } else {
        this.chooseAddress(chosen);
      }
    });

    // A refusal is read out and brought into view. The button that caused it is at the bottom of
    // a scrolled page on a phone; a message that stays where the eye is not is no message.
    effect(() => {
      const element = this.failure()?.nativeElement;
      if (!element) return;
      element.focus({ preventScroll: false });
    });
  }

  protected start(): void {
    this.store.start(this.storage.get(SESSION_KEY, 'session')).subscribe({
      next: (session) => this.storage.set(SESSION_KEY, session.id, 'session'),
      error: () => this.storage.remove(SESSION_KEY, 'session'),
    });
  }

  protected openAddressForm(): void {
    this.place.set(null);
    this.addressFormOpen.set(true);
  }

  protected lookUpPincode(pincode: string): void {
    this.reference.pincode(pincode).subscribe((response) => {
      if (!response) return;
      this.place.set({
        pincode: response.pincode,
        city: response.city,
        // The lookup answers a state *code*; the address wants its id. Matched here against the
        // reference list the form is already showing, so the two cannot name different states.
        stateId: this.stateRows().find((state) => state.code === response.stateCode)?.id ?? null,
        stateName: response.stateName,
      });
    });
  }

  protected saveAddress(value: AddressFormValue): void {
    this.addressBook.create(value).subscribe({
      next: (created) => {
        this.addressFormOpen.set(false);
        this.announcer.announce('Address saved.');
        this.chooseAddress(created.id);
      },
      error: () => this.toasts.danger('We could not save that address. Please check it and try again.'),
    });
  }

  /** An address was picked. Written to the session at once, so delivery can be priced. */
  protected chooseAddress(addressId: string): void {
    this.selectedAddressId.set(addressId);
    const gstin = this.addresses().find((address) => address.id === addressId)?.gstin ?? null;

    this.store.setAddress(addressId, gstin).subscribe({
      next: () => this.loadDelivery(),
      error: () => undefined,
    });
  }

  /**
   * Reads the couriers for the session's address and picks the cheapest for any seller without one.
   *
   * The default is written to the session like a choice the shopper made, because to the API it
   * is one: the quote and the payment ceiling both depend on it. It stays visible as a checked
   * radio the shopper can change.
   */
  private loadDelivery(): void {
    // What was saved, read before the fresh options replace it: the saved price is what the order
    // total is built from, and a courier's live price can have moved since it was saved.
    const saved = new Map(
      (this.store.current()?.shipments ?? []).map((group) => [group.vendorId, group.options[0]?.amount]),
    );

    this.store.loadShippingOptions().subscribe((groups) => {
      // The saved choice where it is still offered, the cheapest where it is not (or none was made).
      const perVendor = groups.map((group) => ({
        vendorId: group.vendorId,
        optionCode:
          (group.options.some((option) => option.code === group.selectedCode) ? group.selectedCode : null) ??
          this.cheapestOption(group.options) ??
          '',
      }));

      if (perVendor.some((entry) => !entry.optionCode)) return;

      // Re-saved when the price of the saved choice has changed, so the total the shopper pays is
      // today's quote rather than the one from whenever they last reached this step.
      const upToDate = groups.every((group) => {
        if (!group.selectedCode) return false;
        const current = group.options.find((option) => option.code === group.selectedCode);
        return current !== undefined && current.amount === saved.get(group.vendorId);
      });

      if (upToDate) {
        this.loadPayment();
        return;
      }
      this.submitShipping(perVendor);
    });
  }

  private cheapestOption(options: readonly { code: string; amount: number }[]): string | null {
    if (options.length === 0) return null;
    return [...options].sort((a, b) => a.amount - b.amount)[0].code;
  }

  protected chooseShipping(choice: ShippingChoice): void {
    const perVendor = this.shippingGroups().map((group) => ({
      vendorId: group.vendorId,
      optionCode: group.vendorId === choice.vendorId ? choice.optionCode : (group.selectedCode ?? ''),
    }));

    // Only sent once every seller has a service. A partial choice would be refused, and being told
    // off for a decision still in progress is not useful.
    if (perVendor.some((entry) => !entry.optionCode)) return;
    this.submitShipping(perVendor);
  }

  private submitShipping(perVendor: readonly { vendorId: string; optionCode: string }[]): void {
    this.store.setShipping(perVendor).subscribe({
      next: () => {
        this.analytics.track(AnalyticsEvents.addShippingInfo);
        // The courier charge changes the tax and the COD ceiling, so what may be paid with is
        // re-read rather than assumed to be what it was before.
        this.loadPayment();
      },
      error: () => undefined,
    });
  }

  /** Reads the payment methods and preselects the first available one when none is chosen. */
  private loadPayment(): void {
    this.store.loadPaymentMethods().subscribe((methods) => {
      this.paymentMethodsLoaded.set(true);
      const chosen = this.store.paymentMethod();
      const stillAvailable = methods.some((method) => method.method === chosen && method.isAvailable);
      if (chosen && stillAvailable) return;

      const first = methods.find((method) => method.isAvailable);
      if (first) this.choosePayment(first.method);
    });
  }

  protected choosePayment(method: string): void {
    this.store.setPaymentMethod(method).subscribe({
      next: () => this.analytics.track(AnalyticsEvents.addPaymentInfo, { payment_type: method }),
      error: () => undefined,
    });
  }

  /**
   * Re-validates the session, then places the order, then pays for it.
   *
   * Three separate things, in that order. The re-validation is the API's last word before any
   * gateway is involved — "something in your cart changed" belongs on this page, not after a
   * payment. The order exists before any money is asked for, so a dismissed gateway leaves an unpaid
   * order to retry rather than a purchase that never happened (docs/05-frontend-architecture.md
   * §3.6).
   *
   * The confirmation page is reached in every outcome — paid, dismissed or failed — because in all
   * three there is an order to show. It is the page's job to say which happened.
   */
  protected placeOrder(): void {
    if (this.placing() || !this.canPlace()) return;
    this.placing.set(true);

    this.store
      .review()
      .pipe(switchMap(() => this.store.placeOrder()))
      .subscribe({
        next: (placed) => {
          this.storage.remove(SESSION_KEY, 'session');
          this.analytics.track(AnalyticsEvents.purchase, {
            transaction_id: placed.orderNumber,
            value: this.summary()?.total.amount,
          });

          if (!placed.payment) {
            // Cash on delivery: there is nothing to hand off, and the order is already placed.
            this.finish(placed.orderNumber);
            return;
          }

          const user = this.profile.user();
          this.payments
            .pay(placed.orderId, placed.payment, {
              name: this.profile.displayName(),
              email: user?.email ?? null,
              mobile: user?.mobile ?? null,
            })
            .subscribe({
              next: (outcome) => {
                if (outcome.kind === 'dismissed') {
                  this.toasts.info(
                    'Your order is placed but not paid for yet. You can pay from the order page.',
                  );
                } else if (outcome.kind === 'failed') {
                  this.toasts.warning(outcome.reason);
                }
                this.finish(placed.orderNumber);
              },
              error: () => this.finish(placed.orderNumber),
            });
        },
        error: () => this.placing.set(false),
      });
  }

  private finish(orderNumber: string): void {
    this.placing.set(false);
    this.store.finish();
    this.cart.clear();
    void this.router.navigate(['/checkout/confirmation', orderNumber]);
  }
}
