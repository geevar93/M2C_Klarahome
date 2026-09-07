import { Money } from '@klarahome/domain';
import { ImageSource } from '@klarahome/util';

/**
 * The buying and account surfaces' view models.
 *
 * The same seam as `catalog.model.ts`, and the same two conventions: **money is `Money`**, never a
 * number and never a formatted string, and **an image is a resolved `ImageSource`**. The app maps
 * the API's responses into these on the way in (`core/commerce.mapper.ts`); nothing under `ui/`
 * knows that a cart line and an order line are two different contracts that happen to render the
 * same way.
 *
 * One rule matters more here than on the discovery pages. **Every amount below was computed by the
 * pricing engine** (docs/04-api-specification.md §1). A summary row exists because the quote has
 * that field, not because a component could add the lines up — two systems that both total a
 * basket will eventually disagree, and the one without the GST rules in it will be the wrong one.
 */

/** A line in the basket, in the cart page and in the order review. */
export interface CartLineView {
  readonly id: string;
  readonly listingId: string;
  readonly name: string;
  /** The PDP path, so a shopper can go back and look again before deciding. */
  readonly href: string | null;
  readonly sku: string;
  readonly sellerName: string;
  readonly image: ImageSource | null;
  readonly quantity: number;
  readonly unitPrice: Money;
  readonly unitMrp: Money | null;
  readonly lineTotal: Money;
  /** Stock on hand for this offer — the ceiling the quantity stepper clamps to. */
  readonly quantityAvailable: number;
  readonly isCodAllowed: boolean;
  readonly savedForLater: boolean;
  /** Anything the API said about this line: out of stock, price changed, quantity capped. */
  readonly issues: readonly CartIssueView[];
}

export interface CartIssueView {
  readonly code: string;
  readonly message: string;
  /** A blocking issue stops checkout; a non-blocking one is a notice the shopper can accept. */
  readonly isBlocking: boolean;
}

/** One seller's part of a basket — the sub-order it will become. */
export interface CartGroupView {
  readonly vendorId: string;
  readonly sellerName: string;
  readonly lineIds: readonly string[];
  readonly total: Money;
  /** The seller's dispatch promise in hours, as the cart shows it. */
  readonly dispatchHours: number;
}

/** One row of the itemised price panel. */
export interface SummaryRowView {
  readonly label: string;
  readonly amount: Money;
  /** A saving, drawn as a negative and in the success colour. */
  readonly isDiscount?: boolean;
  /** A note under the row — "inclusive of GST", "free above ₹999". */
  readonly note?: string | null;
}

/**
 * The price panel shared by the cart, the checkout review, the confirmation and an order.
 *
 * `rows` is whatever the quote had, in the order the API listed it; `total` is the one figure the
 * customer is asked to agree to. Keeping them separate is what stops a component deciding that a
 * blank tax line means zero tax rather than an unquoted basket.
 */
export interface OrderSummaryView {
  readonly rows: readonly SummaryRowView[];
  readonly total: Money;
  readonly totalLabel: string;
  /** Store credit already taken off, when the wallet was applied. */
  readonly walletApplied: Money | null;
  /** What the gateway will be asked for: the total less store credit. */
  readonly amountPayable: Money | null;
  /** "You saved ₹410" — the API's discount total, not a subtraction done here. */
  readonly savings: Money | null;
}

/**
 * A saved address, in the address book and in the checkout's address step.
 *
 * It carries the address twice, and deliberately. `lines` is the assembled block a card renders —
 * the order an Indian address is written in is a decision made once, in the mapper, rather than six
 * times over in six templates. The individual fields beneath it are what the *form* edits, because
 * splitting a rendered block back into its parts is a parser nobody should have to write.
 */
export interface AddressView {
  readonly id: string;
  readonly label: string | null;
  readonly recipientName: string;
  readonly mobile: string;
  /** The address as one block of lines, already assembled in the order India writes them. */
  readonly lines: readonly string[];
  readonly line1: string;
  readonly line2: string | null;
  readonly landmark: string | null;
  readonly city: string;
  readonly stateId: string;
  readonly pincode: string;
  readonly gstin: string | null;
  readonly type: 'Home' | 'Office';
  readonly isDefaultShipping: boolean;
  readonly isDefaultBilling: boolean;
}

/** What a payment method offers, and why it might not be available. */
export interface PaymentMethodView {
  /** The API's code — `Razorpay`, `COD`. Sent back verbatim. */
  readonly method: string;
  readonly name: string;
  readonly isAvailable: boolean;
  /** The handling fee, when there is one. Cash on delivery usually has one. */
  readonly fee: Money | null;
  /** The API's own sentence for why it is unavailable, shown verbatim rather than reworded. */
  readonly reason: string | null;
}

/** One courier service a seller offers, in the checkout's delivery step. */
export interface ShippingOptionView {
  readonly code: string;
  readonly name: string;
  readonly carrier: string | null;
  readonly amount: Money;
  readonly promisedMinDays: number;
  readonly promisedMaxDays: number;
  readonly isCodAvailable: boolean;
}

/** A seller's shipping choices — one group per sub-order. */
export interface ShippingGroupView {
  readonly vendorId: string;
  readonly sellerName: string;
  readonly selectedCode: string | null;
  readonly options: readonly ShippingOptionView[];
}

/** An order as the orders list shows it. */
export interface OrderCardView {
  readonly id: string;
  readonly orderNumber: string;
  readonly placedAt: string;
  readonly status: string;
  readonly statusLabel: string;
  readonly statusTone: StatusTone;
  readonly paymentLabel: string;
  readonly total: Money;
  readonly itemCount: number;
  readonly sellerCount: number;
  /** Up to a few thumbnails, so the row is recognisable without opening it. */
  readonly images: readonly ImageSource[];
}

/** How a status is drawn. Derived from the status by the app, never guessed by a component. */
export type StatusTone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

/** One entry of an order's or a return's history. */
export interface TimelineEntryView {
  readonly id: string;
  readonly label: string;
  readonly detail: string | null;
  readonly occurredAt: string;
  /** True for the entry the order is at now — the one the timeline marks. */
  readonly isCurrent: boolean;
}

/** A line the customer may return, in the return request form. */
export interface ReturnableLineView {
  readonly orderLineId: string;
  readonly name: string;
  readonly sku: string;
  readonly image: ImageSource | null;
  readonly quantityReturnable: number;
  readonly estimatedRefund: Money;
  readonly isReturnable: boolean;
  /** Why it cannot be returned, when it cannot. */
  readonly reason: string | null;
}

/** A reason a return may be raised for, and what choosing it implies. */
export interface ReturnReasonView {
  readonly code: string;
  readonly label: string;
  readonly description: string | null;
  readonly requiresEvidence: boolean;
  readonly allowsReplacement: boolean;
}

/** A movement on the store-credit wallet. */
export interface WalletEntryView {
  readonly id: string;
  readonly reason: string;
  readonly note: string | null;
  readonly amount: Money;
  /** True for a credit; a debit is drawn as a negative. */
  readonly isCredit: boolean;
  readonly balanceAfter: Money;
  readonly occurredAt: string;
  readonly expiresAt: string | null;
}
