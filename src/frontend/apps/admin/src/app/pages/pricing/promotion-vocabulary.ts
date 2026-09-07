/**
 * The words the pricing endpoints accept.
 *
 * `PromotionBody.type`, `.appliesTo` and `.stacking` are declared as plain `string` in the OpenAPI
 * document, so the generated client cannot type them and nothing here is checked by a compiler.
 * The values are read off the module's own enums (`PromotionType`, `PromotionApplication`,
 * `StackingMode` in `KlaraHome.Modules.Pricing`), and the labels are what an operator reads.
 *
 * This is the same gap the Step 24, 26 and 27 parking-lot rows describe, and it is recorded again
 * for this step: **a value renamed on the server silently produces a refused request rather than a
 * compile error.** Collected in one file per module rather than scattered through the screens, so
 * that when the contract does type them there is one place to delete.
 */

export interface Choice {
  readonly value: string;
  readonly label: string;
  readonly hint?: string;
}

/** How a promotion computes its discount. */
export const PROMOTION_TYPES: readonly Choice[] = [
  { value: 'Percentage', label: 'Percentage off', hint: 'A share of what it applies to.' },
  { value: 'Fixed', label: 'Fixed amount off', hint: 'A rupee amount, capped at what is due.' },
  { value: 'FreeShipping', label: 'Free delivery', hint: 'Takes the shipping charge off.' },
  { value: 'Bogo', label: 'Buy some, get some', hint: 'Buy N, get M at a discount.' },
  { value: 'Bundle', label: 'Bundle price', hint: 'A set of listings for one price.' },
  { value: 'Tiered', label: 'Tiered by basket value', hint: 'More off as the basket grows.' },
];

/** What the discount is computed against. */
export const PROMOTION_APPLICATIONS: readonly Choice[] = [
  { value: 'Line', label: 'The matching lines', hint: 'Only the items the scope selects.' },
  { value: 'Order', label: 'The whole basket', hint: 'Allocated back across the lines pro rata.' },
  { value: 'Shipping', label: 'The delivery charge', hint: 'Leaves the goods at full price.' },
];

/** Whether this promotion tolerates another one on the same basket. */
export const STACKING_MODES: readonly Choice[] = [
  { value: 'Exclusive', label: 'On its own', hint: 'Nothing else applies alongside it.' },
  { value: 'Stackable', label: 'Alongside others', hint: 'Applied in priority order.' },
];

/** What a price list is for. `Scheduled` is the one whose window is load-bearing. */
export const PRICE_LIST_TYPES: readonly Choice[] = [
  { value: 'Base', label: 'Base', hint: 'The standing price. No window.' },
  { value: 'Sale', label: 'Sale', hint: 'Beats the base list while it is active.' },
  { value: 'Scheduled', label: 'Scheduled', hint: 'Applies only inside its window.' },
];

/**
 * How a customer paid, for the payment-method condition.
 *
 * Lower case, because the evaluator compares against the literals `prepaid` and `cod`
 * (`PromotionEvaluator`) rather than against an enum name — a condition written with any other
 * spelling matches nothing and refuses nothing, which is the quietest of the failures this file
 * exists to make less likely.
 */
export const PAYMENT_METHODS: readonly Choice[] = [
  { value: 'prepaid', label: 'Prepaid (gateway)' },
  { value: 'cod', label: 'Cash on delivery' },
];
