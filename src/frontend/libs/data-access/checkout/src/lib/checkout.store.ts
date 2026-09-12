import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import {
  CheckoutApiClient,
  CheckoutResponse,
  PaymentMethodResponse,
  PlaceOrderResponse,
  VendorShippingOptionsResponse,
} from '@klarahome/data-access-api';
import { newUuid } from '@klarahome/util';
import { Observable, catchError, finalize, of, tap, throwError } from 'rxjs';

/** The four screens of the checkout, in the order they are walked. */
export type CheckoutStep = 'address' | 'delivery' | 'payment' | 'review';

export const CHECKOUT_STEPS: readonly CheckoutStep[] = ['address', 'delivery', 'payment', 'review'];

/**
 * The checkout session — one server-side object, walked in four steps.
 *
 * The session is the API's (Step 13), not this store's: it holds the chosen address, the chosen
 * courier per seller, the payment method and the quote those three produce, and every step here is
 * a write to it that answers with the whole session back. That is the property the whole flow rests
 * on — **the session survives a reload, a browser back and a closed tab**, because it lives on the
 * server and its id is all the client needs to find it again.
 *
 * Which is why the id is remembered in `sessionStorage`. A shopper who is bounced out to a payment
 * gateway and comes back — or who reloads on the payment step because the network stalled — must
 * land back in the checkout they were in, not at the start of a new one. `sessionStorage` rather
 * than `localStorage`: an abandoned checkout should not be waiting a week later, and the API expires
 * the session anyway.
 *
 * **Nothing about the price is decided here.** Every amount on every step comes from
 * `session.cart.quote`, recomputed by the server after each write, because the shipping choice
 * changes the tax and the payment method changes the fee — and a client that added a courier charge
 * to a subtotal would be quoting a number the order will not be placed at.
 */
@Injectable({ providedIn: 'root' })
export class CheckoutStore {
  private readonly api = inject(CheckoutApiClient);

  private readonly session = signal<CheckoutResponse | null>(null);
  private readonly methods = signal<readonly PaymentMethodResponse[]>([]);
  private readonly shipping = signal<readonly VendorShippingOptionsResponse[]>([]);
  private readonly busy = signal(false);
  private readonly starting = signal(false);
  private readonly failure = signal<string | null>(null);

  /**
   * The `Idempotency-Key` for placing this session's order, minted on the first attempt and reused
   * on every retry. The API refuses a placement without one and treats the same key twice as the
   * same order once — which is only true if a double tap, a timeout and a "try again" all send the
   * key the first attempt did. Keyed by session so a new checkout never inherits an old key.
   */
  private placementKey: { readonly sessionId: string; readonly key: string } | null = null;

  readonly current: Signal<CheckoutResponse | null> = this.session.asReadonly();
  readonly paymentMethods: Signal<readonly PaymentMethodResponse[]> = this.methods.asReadonly();
  readonly shippingOptions: Signal<readonly VendorShippingOptionsResponse[]> = this.shipping.asReadonly();
  readonly isBusy: Signal<boolean> = this.busy.asReadonly();
  readonly isStarting: Signal<boolean> = this.starting.asReadonly();
  /** The last refusal, in the API's words. Rendered on the step it belongs to, not as a toast. */
  readonly error: Signal<string | null> = this.failure.asReadonly();

  readonly quote = computed(() => this.session()?.cart?.quote ?? null);
  readonly currencyCode = computed(() => this.session()?.currencyCode ?? 'INR');
  readonly shippingAddress = computed(() => this.session()?.shippingAddress ?? null);
  readonly paymentMethod = computed(() => this.session()?.paymentMethod ?? null);

  /**
   * The furthest step the session's own state justifies.
   *
   * Derived from what the session has, never from where the shopper has been: reloading on the
   * payment step of a session with no address must land on the address step, and a component that
   * kept its own idea of progress would happily show a payment form for a session the server would
   * refuse to place.
   */
  readonly furthestStep = computed<CheckoutStep>(() => {
    const session = this.session();
    if (!session?.shippingAddress) return 'address';
    if (session.shipments.some((shipment) => !shipment.selectedCode)) return 'delivery';
    if (!session.paymentMethod) return 'payment';
    return 'review';
  });

  /**
   * Starts or resumes a checkout.
   *
   * Resume first: an id in `sessionStorage` is looked up, and only a session that is gone or no
   * longer open produces a new one. Starting unconditionally would abandon a session the shopper
   * is halfway through every time they reloaded.
   */
  start(rememberedId: string | null): Observable<CheckoutResponse> {
    this.starting.set(true);
    this.failure.set(null);

    const request = rememberedId
      ? this.api
          .storeGetCheckout(rememberedId, { silentErrors: true })
          .pipe(catchError(() => this.api.storeStartCheckout({ silentErrors: true })))
      : this.api.storeStartCheckout({ silentErrors: true });

    return request.pipe(
      tap((session) => this.adopt(session)),
      catchError((error: unknown) => this.fail(error)),
      finalize(() => this.starting.set(false)),
    );
  }

  setAddress(shippingAddressId: string, gstin: string | null): Observable<CheckoutResponse> {
    return this.mutate((id) =>
      this.api.storeSetCheckoutAddress(
        id,
        // Billing follows shipping. There is nowhere on this storefront to ask for a different one,
        // and sending null is what tells the API to use the shipping address for the invoice.
        { shippingAddressId, billingAddressId: null, gstin },
        { silentErrors: true },
      ),
    );
  }

  setShipping(perVendor: readonly { vendorId: string; optionCode: string }[]): Observable<CheckoutResponse> {
    return this.mutate((id) =>
      this.api.storeSetCheckoutShipping(id, { perVendor: [...perVendor] }, { silentErrors: true }),
    );
  }

  setPaymentMethod(method: string): Observable<CheckoutResponse> {
    return this.mutate((id) =>
      this.api.storeSetCheckoutPaymentMethod(id, { method }, { silentErrors: true }),
    );
  }

  /** Re-quotes and re-validates the session. What the review step reads before it shows a total. */
  review(): Observable<CheckoutResponse> {
    return this.mutate((id) => this.api.storeReviewCheckout(id, { silentErrors: true }));
  }

  /** The courier services each seller offers to the chosen address. Read when the address is set. */
  loadShippingOptions(): void {
    const id = this.session()?.id;
    if (!id) return;

    this.api
      .storeCheckoutShippingOptions(id, { silentErrors: true })
      .pipe(catchError(() => of<VendorShippingOptionsResponse[]>([])))
      .subscribe((options) => this.shipping.set(options));
  }

  /** What may be paid with, and why not. Read after the courier is chosen: the fee depends on it. */
  loadPaymentMethods(): void {
    const id = this.session()?.id;
    if (!id) return;

    this.api
      .storeCheckoutPaymentMethods(id, { silentErrors: true })
      .pipe(catchError(() => of<PaymentMethodResponse[]>([])))
      .subscribe((methods) => this.methods.set(methods));
  }

  /**
   * Places the order.
   *
   * **Idempotent at the API** (Step 13): placement is guarded by a unique index, so the double tap
   * that a slow connection produces creates one order rather than two. The busy flag here is a
   * courtesy to the customer, not the guarantee.
   *
   * The response carries the order and, for a prepaid order, the gateway instruction that
   * `PaymentHandoff` needs. The session is *not* cleared here — the confirmation page still has to
   * be reached, and a payment that has to be retried needs the order it belongs to.
   */
  placeOrder(): Observable<PlaceOrderResponse> {
    const id = this.session()?.id;
    if (!id) return throwError(() => new Error('There is no checkout to place.'));

    this.busy.set(true);
    this.failure.set(null);

    if (this.placementKey?.sessionId !== id) {
      this.placementKey = { sessionId: id, key: newUuid() };
    }

    return this.api.storePlaceOrder(id, { silentErrors: true, idempotencyKey: this.placementKey.key }).pipe(
      catchError((error: unknown) => this.fail(error)),
      finalize(() => this.busy.set(false)),
    );
  }

  /** Abandons the session on the server. Called when the shopper goes back to the cart to edit it. */
  abandon(): void {
    const id = this.session()?.id;
    this.placementKey = null;
    this.session.set(null);
    this.methods.set([]);
    this.shipping.set([]);
    if (id)
      this.api
        .storeAbandonCheckout(id, { silentErrors: true, showLoading: false })
        .subscribe({ error: () => undefined });
  }

  /** Forgets the local session without touching the server's. Called once an order is placed. */
  finish(): void {
    this.placementKey = null;
    this.session.set(null);
    this.methods.set([]);
    this.shipping.set([]);
    this.failure.set(null);
  }

  private mutate(request: (id: string) => Observable<CheckoutResponse>): Observable<CheckoutResponse> {
    const id = this.session()?.id;
    if (!id) return throwError(() => new Error('There is no checkout to change.'));

    this.busy.set(true);
    this.failure.set(null);

    return request(id).pipe(
      tap((session) => this.adopt(session)),
      catchError((error: unknown) => this.fail(error)),
      finalize(() => this.busy.set(false)),
    );
  }

  private adopt(session: CheckoutResponse): void {
    this.session.set(session);
    // The shipment groups come back on every session, already carrying what was chosen. Keeping the
    // options list in step with them means the delivery step never renders a selection the session
    // does not have.
    this.shipping.set(session.shipments ?? []);
  }

  private fail(error: unknown): Observable<never> {
    const message =
      typeof error === 'object' && error !== null && 'message' in error
        ? String((error as { message: unknown }).message)
        : 'Something went wrong. Please try again.';
    this.failure.set(message);
    return throwError(() => error);
  }
}
