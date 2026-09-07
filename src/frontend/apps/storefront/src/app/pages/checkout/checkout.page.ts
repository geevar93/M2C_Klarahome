import { ChangeDetectionStrategy, Component, computed, effect, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { AddressBookStore, ProfileStore } from '@klarahome/data-access-account';
import { CartStore } from '@klarahome/data-access-cart';
import { CHECKOUT_STEPS, CheckoutStep, CheckoutStore, PaymentHandoff } from '@klarahome/data-access-checkout';
import { ReferenceDataService } from '@klarahome/data-access-content';
import { Alert, Button, Drawer, EmptyState, Skeleton, Stepper } from '@klarahome/ui-primitives';
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
import { map } from 'rxjs';

import { CommerceMapper } from '../../core/commerce.mapper';

/** Where the open checkout session is remembered, so a reload or a gateway bounce comes back to it. */
const SESSION_KEY = 'kh.checkout.session';

/**
 * Checkout — `/checkout`.
 *
 * Four steps over **one server-side session**: address, delivery, payment, review. Each step is a
 * write to that session, and the session answers with itself and a fresh quote every time — which
 * is why the total on the review step is a figure the server produced and not one this page
 * assembled from a subtotal and a courier charge.
 *
 * **The step is in the URL** (`?step=payment`). A four-screen flow whose position lives in a
 * component field is a flow where the browser's back button leaves the shop, and on a phone the
 * back gesture is how people correct a mistake. It also means a reload lands where the shopper was.
 *
 * **The session outlives the page.** Its id is kept in `sessionStorage`, so being bounced to the
 * payment gateway and back — or reloading on a stalled network — resumes the same checkout rather
 * than starting a new one. `sessionStorage` and not `localStorage`: an abandoned checkout should not
 * be waiting a week later.
 *
 * **The furthest reachable step is derived from the session, never from history.** A session with no
 * address cannot show a payment form, whatever the URL says — a shopper who bookmarked
 * `?step=payment` is put back on the address step rather than shown a form the server would refuse.
 *
 * **Sign-in is required, and that is the API's rule** (`RequireAuthorization` on the whole
 * `/store/checkout` group: a checkout session is opened against a customer id and the address step
 * chooses from that customer's address book). The route's `authenticatedGuard` carries the shopper
 * to sign-in and back with their basket merged, which is the closest this storefront can get until
 * guest checkout exists as a feature rather than a setting.
 */
@Component({
  selector: 'kh-checkout-page',
  imports: [
    AddressCard,
    AddressForm,
    Alert,
    Button,
    CartLine,
    Drawer,
    EmptyState,
    MoneyPipe,
    OrderSummary,
    PaymentMethodSelector,
    RouterLink,
    ShippingOptionSelector,
    Skeleton,
    Stepper,
    StickyAction,
  ],
  template: `
    <h1>Checkout</h1>

    @if (store.isStarting()) {
      <kh-skeleton height="20rem" />
    } @else if (!store.current()) {
      <kh-empty-state
        heading="We could not open a checkout"
        message="Your basket may have changed since you last looked. Open your cart and try again."
      >
        <a khButton variant="primary" routerLink="/cart">Back to cart</a>
      </kh-empty-state>
    } @else {
      <kh-stepper [steps]="stepLabels" [active]="step()" (stepSelected)="goTo($any($event))" />

      @if (store.error(); as message) {
        <kh-alert tone="danger" heading="That did not work">{{ message }}</kh-alert>
      }

      <div class="layout">
        <div class="step">
          @switch (step()) {
            @case ('address') {
              <h2>Where should we deliver?</h2>

              @if (addresses().length > 0) {
                <div class="addresses">
                  @for (address of addresses(); track address.id) {
                    <kh-address-card
                      [address]="address"
                      [selectable]="true"
                      group="checkout-address"
                      [selected]="address.id === selectedAddressId()"
                      (chosen)="selectedAddressId.set($event)"
                    />
                  }
                </div>

                <button khButton variant="secondary" type="button" (click)="openAddressForm()">
                  Add a new address
                </button>
              } @else if (addressBook.hasLoaded()) {
                <kh-alert tone="info">
                  You have no saved addresses yet. Add the one this order should go to.
                </kh-alert>
                <button khButton variant="primary" type="button" (click)="openAddressForm()">
                  Add an address
                </button>
              } @else {
                <kh-skeleton height="8rem" />
              }
            }

            @case ('delivery') {
              <h2>How should it get to you?</h2>
              <p class="lead">Each seller ships separately, so each one has its own delivery choice.</p>

              <kh-shipping-option-selector [groups]="shippingGroups()" (chosen)="chooseShipping($event)" />
            }

            @case ('payment') {
              <h2>How would you like to pay?</h2>

              <kh-payment-method-selector
                [methods]="paymentMethods()"
                [selected]="store.paymentMethod()"
                legend="Payment method"
                (chosen)="choosePayment($event)"
              />

              @if (codRefusal(); as reason) {
                <kh-alert tone="warning" heading="Cash on delivery is not available">{{ reason }}</kh-alert>
              }
            }

            @case ('review') {
              <h2>Check everything over</h2>

              @if (deliverTo(); as address) {
                <section class="review-block">
                  <h3>Delivering to</h3>
                  <kh-address-card [address]="address" />
                  <button khButton variant="tertiary" size="sm" type="button" (click)="goTo('address')">
                    Change
                  </button>
                </section>
              }

              <section class="review-block">
                <h3>Paying by</h3>
                <p>{{ paymentLabel() }}</p>
                <button khButton variant="tertiary" size="sm" type="button" (click)="goTo('payment')">
                  Change
                </button>
              </section>

              <section class="review-block">
                <h3>Your items</h3>
                @for (line of reviewLines(); track line.id) {
                  <kh-cart-line [line]="line" [readOnly]="true" />
                }
              </section>
            }
          }
        </div>

        <aside class="summary">
          @if (summary(); as details) {
            <kh-order-summary [summary]="details" heading="Order summary" />
          } @else {
            <kh-skeleton height="12rem" />
          }
        </aside>
      </div>

      <ng-template khStickyAction>
        <div class="bar">
          <span class="bar-total">
            @if (summary(); as details) {
              <strong>{{ details.total | khMoney }}</strong>
              <span>{{ stepHint() }}</span>
            }
          </span>
          <button
            khButton
            variant="primary"
            type="button"
            [disabled]="!canAdvance() || store.isBusy() || placing()"
            (click)="advance()"
          >
            {{ advanceLabel() }}
          </button>
        </div>
      </ng-template>
    }

    <!-- The address form is a bottom sheet rather than a route: it is a detour inside one step, and
         a shopper who adds an address must come straight back to the checkout they were in. -->
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

    h1 {
      font-size: var(--text-2xl);
    }

    h2 {
      font-size: var(--text-lg);
    }

    h3 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .lead {
      margin-block-start: calc(var(--space-2) * -1);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
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

    .addresses {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      margin-block-end: var(--space-4);
    }

    .review-block {
      padding-block: var(--space-4);
      border-block-end: 1px solid var(--color-border);
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
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly stepLabels = [
    { id: 'address', label: 'Address' },
    { id: 'delivery', label: 'Delivery' },
    { id: 'payment', label: 'Payment' },
    { id: 'review', label: 'Review' },
  ];

  protected readonly selectedAddressId = signal<string | null>(null);
  protected readonly addressFormOpen = signal(false);
  protected readonly place = signal<PincodePlaceView | null>(null);
  protected readonly placing = signal(false);

  private readonly stateRows = toSignal(this.reference.states(), { initialValue: [] });

  protected readonly states = computed(() =>
    this.stateRows().map((state) => ({ id: state.id, name: state.name })),
  );

  private readonly stateNames = computed(
    () => new Map(this.stateRows().map((state) => [state.id, state.name])),
  );

  /**
   * The step the URL asks for, clamped to what the session actually supports.
   *
   * Clamped rather than redirected, so the address bar and the rendered step never disagree — and a
   * shopper who typed `?step=review` into a fresh session sees the address form, not an error.
   */
  private readonly requestedStep = toSignal(
    this.route.queryParamMap.pipe(map((params) => params.get('step') as CheckoutStep | null)),
    { initialValue: null },
  );

  protected readonly step = computed<CheckoutStep>(() => {
    const requested = this.requestedStep();
    const furthest = this.store.furthestStep();
    if (!requested || !CHECKOUT_STEPS.includes(requested)) return furthest;
    return CHECKOUT_STEPS.indexOf(requested) <= CHECKOUT_STEPS.indexOf(furthest) ? requested : furthest;
  });

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

  protected readonly deliverTo = computed(() => {
    const address = this.store.shippingAddress();
    return address ? this.mapper.checkoutAddress(address, this.stateNames().get(address.stateId)) : null;
  });

  protected readonly reviewLines = computed(() => {
    const session = this.store.current();
    if (!session?.cart) return [];
    return session.cart.lines.map((line) => this.mapper.cartLine(line, session.currencyCode));
  });

  protected readonly paymentLabel = computed(() => {
    const chosen = this.store.paymentMethod();
    return this.paymentMethods().find((method) => method.method === chosen)?.name ?? 'Not chosen yet';
  });

  protected readonly advanceLabel = computed(() => {
    switch (this.step()) {
      case 'review':
        return this.placing() ? 'Placing…' : 'Place order';
      case 'payment':
        return 'Review order';
      default:
        return 'Continue';
    }
  });

  protected readonly stepHint = computed(() => {
    switch (this.step()) {
      case 'address':
        return 'Choose a delivery address';
      case 'delivery':
        return 'Choose a delivery service';
      case 'payment':
        return 'Choose how to pay';
      default:
        return 'You will not be charged until you confirm';
    }
  });

  /** Whether the current step has what it needs. Never a judgement the server has already made. */
  protected readonly canAdvance = computed(() => {
    switch (this.step()) {
      case 'address':
        return this.selectedAddressId() !== null;
      case 'delivery':
        return this.shippingGroups().every((group) => group.selectedCode !== null);
      case 'payment':
        return this.store.paymentMethod() !== null && this.store.paymentMethod() !== '';
      case 'review':
        return true;
    }
  });

  constructor() {
    this.addressBook.loadOnce();
    this.profile.loadOnce();
    this.analytics.track(AnalyticsEvents.beginCheckout);

    this.store.start(this.storage.get(SESSION_KEY, 'session')).subscribe({
      next: (session) => {
        this.storage.set(SESSION_KEY, session.id, 'session');
        this.store.loadShippingOptions();
        this.store.loadPaymentMethods();
      },
      error: () => this.storage.remove(SESSION_KEY, 'session'),
    });

    // Preselect the address the session already has, else the customer's default. An effect rather
    // than a one-off because the address book and the session both arrive asynchronously, and
    // whichever lands second is the one that should decide.
    effect(() => {
      if (this.selectedAddressId() !== null) return;
      const chosen =
        this.store.shippingAddress()?.sourceAddressId ?? this.addressBook.preferred()?.id ?? null;
      if (chosen) this.selectedAddressId.set(chosen);
    });
  }

  protected goTo(step: CheckoutStep): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { step },
      queryParamsHandling: 'merge',
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
        this.selectedAddressId.set(created.id);
        this.announcer.announce('Address saved.');
      },
      error: () => this.toasts.danger('We could not save that address. Please check it and try again.'),
    });
  }

  protected chooseShipping(choice: ShippingChoice): void {
    const perVendor = this.shippingGroups().map((group) => ({
      vendorId: group.vendorId,
      optionCode: group.vendorId === choice.vendorId ? choice.optionCode : (group.selectedCode ?? ''),
    }));

    // Only sent once every seller has a service. A partial choice would be refused, and being told
    // off for a decision still in progress is not useful.
    if (perVendor.some((entry) => !entry.optionCode)) return;

    this.store.setShipping(perVendor).subscribe({
      next: () => {
        // The courier charge changes the tax and the COD ceiling, so what may be paid with is
        // re-read rather than assumed to be what it was on the previous step.
        this.store.loadPaymentMethods();
        this.analytics.track(AnalyticsEvents.addShippingInfo);
      },
      error: () => undefined,
    });
  }

  protected choosePayment(method: string): void {
    this.store.setPaymentMethod(method).subscribe({
      next: () => this.analytics.track(AnalyticsEvents.addPaymentInfo, { payment_type: method }),
      error: () => undefined,
    });
  }

  protected advance(): void {
    switch (this.step()) {
      case 'address':
        this.submitAddress();
        return;
      case 'delivery':
        this.goTo('payment');
        return;
      case 'payment':
        this.store.review().subscribe({ next: () => this.goTo('review'), error: () => undefined });
        return;
      case 'review':
        this.placeOrder();
    }
  }

  private submitAddress(): void {
    const addressId = this.selectedAddressId();
    if (!addressId) return;

    const gstin = this.addresses().find((address) => address.id === addressId)?.gstin ?? null;

    this.store.setAddress(addressId, gstin).subscribe({
      next: () => {
        // The address decides which couriers serve the order and at what price, so the options are
        // read after it is set rather than before.
        this.store.loadShippingOptions();
        this.goTo('delivery');
      },
      error: () => undefined,
    });
  }

  /**
   * Places the order, then pays for it.
   *
   * Two separate things, and in that order, which is what makes the flow survive a customer closing
   * the gateway: the order exists before any money is asked for, so a dismissal leaves an unpaid
   * order to retry rather than a purchase that never happened
   * (docs/05-frontend-architecture.md §3.6).
   *
   * The confirmation page is reached in every outcome — paid, dismissed or failed — because in all
   * three there is an order to show. It is the page's job to say which happened.
   */
  private placeOrder(): void {
    if (this.placing()) return;
    this.placing.set(true);

    this.store.placeOrder().subscribe({
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
