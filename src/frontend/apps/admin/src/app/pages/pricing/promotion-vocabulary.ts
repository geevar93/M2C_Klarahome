import {
  PriceListType,
  PromotionApplication,
  PromotionType,
  QuotePaymentMethod,
  StackingMode,
} from '@klarahome/data-access-admin';

/**
 * The labels the pricing screens put on the words the pricing endpoints accept.
 *
 * **Every value below is a generated enum.** `PromotionBody.type`, `.appliesTo`, `.stacking`, the
 * price-list type and the payment-method condition were all declared as plain `string` in the
 * OpenAPI document until Step 28B, so nothing here was checked by a compiler and a value renamed on
 * the server produced a refused request rather than a build failure (deliverable 11).
 *
 * What is left is the wording and the hints, which is the half a compiler cannot check.
 */

export interface Choice<T extends string = string> {
  readonly value: T;
  readonly label: string;
  readonly hint?: string;
}

/** How a promotion computes its discount. */
export const PROMOTION_TYPES: readonly Choice<PromotionType>[] = [
  { value: 'Percentage', label: 'Percentage off', hint: 'A share of what it applies to.' },
  { value: 'Fixed', label: 'Fixed amount off', hint: 'A rupee amount, capped at what is due.' },
  { value: 'FreeShipping', label: 'Free delivery', hint: 'Takes the shipping charge off.' },
  { value: 'Bogo', label: 'Buy some, get some', hint: 'Buy N, get M at a discount.' },
  { value: 'Bundle', label: 'Bundle price', hint: 'A set of listings for one price.' },
  { value: 'Tiered', label: 'Tiered by basket value', hint: 'More off as the basket grows.' },
];

/** What the discount is computed against. */
export const PROMOTION_APPLICATIONS: readonly Choice<PromotionApplication>[] = [
  { value: 'Line', label: 'The matching lines', hint: 'Only the items the scope selects.' },
  { value: 'Order', label: 'The whole basket', hint: 'Allocated back across the lines pro rata.' },
  { value: 'Shipping', label: 'The delivery charge', hint: 'Leaves the goods at full price.' },
];

/** Whether this promotion tolerates another one on the same basket. */
export const STACKING_MODES: readonly Choice<StackingMode>[] = [
  { value: 'Exclusive', label: 'On its own', hint: 'Nothing else applies alongside it.' },
  { value: 'Stackable', label: 'Alongside others', hint: 'Applied in priority order.' },
];

/** What a price list is for. `Scheduled` is the one whose window is load-bearing. */
export const PRICE_LIST_TYPES: readonly Choice<PriceListType>[] = [
  { value: 'Base', label: 'Base', hint: 'The standing price. No window.' },
  { value: 'Sale', label: 'Sale', hint: 'Beats the base list while it is active.' },
  { value: 'Scheduled', label: 'Scheduled', hint: 'Applies only inside its window.' },
];

/**
 * How a customer paid, for the payment-method condition.
 *
 * The enum's own names now, not the `prepaid`/`cod` literals the evaluator compares against: the
 * API takes a `QuotePaymentMethod` and maps it to the stored condition value in one place beside
 * that comparison. A condition written with a spelling the evaluator will never match — which
 * matched nothing and refused nothing — is no longer expressible (Step 28B, deliverable 11).
 */
export const PAYMENT_METHODS: readonly Choice<QuotePaymentMethod>[] = [
  { value: 'Prepaid', label: 'Prepaid (gateway)' },
  { value: 'CashOnDelivery', label: 'Cash on delivery' },
];
