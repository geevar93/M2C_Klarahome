import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, inject } from '@angular/core';
import {
  MyPaymentResponse,
  PaymentInstructionResponse,
  PaymentResponse,
  PaymentsApiClient,
} from '@klarahome/data-access-api';
import { Observable, from, switchMap } from 'rxjs';

/** Where the gateway's own script is served from. Its own origin; never bundled, never proxied. */
const RAZORPAY_CHECKOUT_SRC = 'https://checkout.razorpay.com/v1/checkout.js';

/** How the handoff ended. The page decides what each one means for the customer. */
export type HandoffOutcome =
  | { readonly kind: 'paid'; readonly payment: MyPaymentResponse }
  | { readonly kind: 'dismissed' }
  | { readonly kind: 'failed'; readonly reason: string };

/** What the customer's own details fill into the gateway's form, so they retype nothing. */
export interface HandoffContact {
  readonly name: string | null;
  readonly email: string | null;
  readonly mobile: string | null;
}

/**
 * Handing the customer to the payment gateway, and taking the answer back.
 *
 * **No card details ever touch this application.** Razorpay's own script renders its own form on
 * its own origin; what this service holds is a public key and an order id, and what it gets back is
 * a signature (Step 15). That is the whole reason the flow is shaped this way — a storefront that
 * collected a card number would be in PCI-DSS scope, and this one is deliberately not.
 *
 * **The gateway's answer is not the truth; the server's verification is.** A `handler` callback in
 * the browser can be replayed, tampered with or simply not fire, so the signature is posted to
 * `POST /store/payments/{orderId}/verify` and the *API's* verdict is what the confirmation page
 * shows. The webhook (Step 15) says the same thing independently, which is what makes a customer
 * who closed the tab mid-payment still end up with a paid order.
 *
 * **A dismissal is not a failure.** Closing the gateway leaves a placed, unpaid order — not a lost
 * one (docs/05-frontend-architecture.md §3.6). The order exists, the customer is taken to it, and
 * `retry` re-opens the gateway against the same order rather than making a second one.
 *
 * The script is loaded on demand and once. It is ~40 kB from a third-party origin, and no shopper
 * browsing a category page should pay for it.
 */
@Injectable({ providedIn: 'root' })
export class PaymentHandoff {
  private readonly api = inject(PaymentsApiClient);
  private readonly document = inject(DOCUMENT);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  private loader: Promise<boolean> | null = null;

  /**
   * Opens the gateway for a freshly placed order, then verifies whatever it answers.
   *
   * `orderId` is ours; `instruction.providerOrderId` is theirs. Both travel, because the
   * verification has to be attributed to an order in our own ledger — the gateway's id alone would
   * make the endpoint trust the caller's word about which order was paid for.
   */
  pay(
    orderId: string,
    instruction: PaymentInstructionResponse | PaymentResponse,
    contact: HandoffContact,
  ): Observable<HandoffOutcome> {
    return from(this.open(orderId, instruction, contact));
  }

  /**
   * Re-opens the gateway for an order that was placed but not paid.
   *
   * The API mints a fresh gateway order for the same one of ours — a provider order has a short
   * life and cannot be reopened once the customer has walked away from it — which is what makes
   * "the order is never lost, only its payment retried" true rather than a slogan.
   */
  retry(orderId: string, contact: HandoffContact): Observable<HandoffOutcome> {
    return this.api
      .storeRetryPayment(orderId, { silentErrors: true })
      .pipe(switchMap((instruction) => from(this.open(orderId, instruction, contact))));
  }

  /** What the API says about an order's payment. What the confirmation page reads on arrival. */
  status(orderId: string): Observable<MyPaymentResponse> {
    return this.api.storeGetOrderPayment(orderId, { silentErrors: true });
  }

  private async open(
    orderId: string,
    instruction: PaymentInstructionResponse | PaymentResponse,
    contact: HandoffContact,
  ): Promise<HandoffOutcome> {
    if (!this.isBrowser) return { kind: 'failed', reason: 'Payment can only be started in a browser.' };

    if (!(await this.loadScript())) {
      return {
        kind: 'failed',
        reason: 'We could not reach the payment provider. Check your connection and try again.',
      };
    }

    const razorpay = (globalThis as unknown as { Razorpay?: RazorpayConstructor }).Razorpay;
    if (!razorpay) {
      return { kind: 'failed', reason: 'The payment provider did not load. Please try again.' };
    }

    return new Promise<HandoffOutcome>((resolve) => {
      // Resolved exactly once. Razorpay can fire `ondismiss` after `handler` on some flows, and a
      // promise that settled twice would leave the page acting on the second answer.
      let settled = false;
      const settle = (outcome: HandoffOutcome): void => {
        if (settled) return;
        settled = true;
        resolve(outcome);
      };

      const checkout = new razorpay({
        key: instruction.publicKey,
        order_id: instruction.providerOrderId,
        amount: Math.round(instruction.amount * 100),
        currency: instruction.currencyCode,
        prefill: {
          name: contact.name ?? undefined,
          email: contact.email ?? undefined,
          contact: contact.mobile ?? undefined,
        },
        // The gateway's own retry loop is switched off: a failed attempt should come back to our
        // page, which knows about the order, rather than being retried inside a modal that does not.
        retry: { enabled: false },
        modal: { ondismiss: () => settle({ kind: 'dismissed' }) },
        handler: (response) => {
          this.api
            .storeVerifyCheckout(
              orderId,
              {
                providerOrderId: response.razorpay_order_id,
                providerPaymentId: response.razorpay_payment_id,
                signature: response.razorpay_signature,
              },
              { silentErrors: true },
            )
            .subscribe({
              next: (payment) => settle({ kind: 'paid', payment }),
              // The money may well have been taken. The webhook will settle it, so the customer is
              // told the truth — that it is being confirmed — rather than that it failed.
              error: () =>
                settle({
                  kind: 'failed',
                  reason:
                    'We could not confirm the payment straight away. We will update your order shortly.',
                }),
            });
        },
      });

      checkout.on('payment.failed', (event) => {
        settle({
          kind: 'failed',
          reason: event?.error?.description || 'The payment did not go through. You can try again.',
        });
      });

      checkout.open();
    });
  }

  /**
   * Loads the gateway's script, once.
   *
   * Cached as a promise rather than a boolean so that two calls arriving together wait on one
   * `<script>` rather than appending two. A failure clears the cache, because the next attempt may
   * be on a working connection.
   */
  private loadScript(): Promise<boolean> {
    if (!this.isBrowser) return Promise.resolve(false);

    this.loader ??= new Promise<boolean>((resolve) => {
      if ((globalThis as unknown as { Razorpay?: unknown }).Razorpay) {
        resolve(true);
        return;
      }

      const script = this.document.createElement('script');
      script.src = RAZORPAY_CHECKOUT_SRC;
      script.async = true;
      script.onload = () => resolve(true);
      script.onerror = () => {
        this.loader = null;
        resolve(false);
      };
      this.document.head.appendChild(script);
    });

    return this.loader;
  }
}

/**
 * The slice of Razorpay's global that this application uses.
 *
 * Declared here rather than pulled from `@types/razorpay`: the surface used is five fields wide,
 * the package is unofficial, and a dependency whose only job is to describe a global is a
 * dependency to keep patched for no benefit.
 */
interface RazorpayConstructor {
  new (options: RazorpayOptions): RazorpayInstance;
}

interface RazorpayInstance {
  open(): void;
  on(event: 'payment.failed', handler: (event: { error?: { description?: string } }) => void): void;
}

interface RazorpayOptions {
  key: string;
  order_id: string;
  amount: number;
  currency: string;
  prefill?: { name?: string; email?: string; contact?: string };
  retry?: { enabled: boolean };
  modal?: { ondismiss?: () => void };
  handler: (response: {
    razorpay_order_id: string;
    razorpay_payment_id: string;
    razorpay_signature: string;
  }) => void;
}
