/**
 * Money.
 *
 * The API sends `{ amount, currency }` with the amount as a JSON number carrying four decimal
 * places (docs/04-api-specification.md §1). **The frontend does not do money arithmetic.** Every
 * total, tax figure and discount the user sees was computed by the pricing engine and is
 * displayed, not derived: two systems that both add up a basket will eventually disagree, and the
 * one that is wrong will be the one without the tax rules in it.
 *
 * The helpers here exist for the two things a client legitimately does — compare and display —
 * and for the one sum it may compute locally, which is a line's `unitPrice × quantity` shown
 * while an optimistic cart update is in flight.
 */

export interface Money {
  readonly amount: number;
  readonly currency: string;
}

export const INR = 'INR';

/** Rupees to paise, as an integer. The unit every comparison here works in. */
const toPaise = (amount: number): number => Math.round(amount * 100);

export const money = (amount: number, currency: string = INR): Money => ({ amount, currency });

export const zero = (currency: string = INR): Money => money(0, currency);

export const isZero = (value: Money): boolean => toPaise(value.amount) === 0;

/** Compares two amounts. Throws on mixed currencies rather than silently comparing numbers. */
export function compareMoney(left: Money, right: Money): number {
  assertSameCurrency(left, right);
  return toPaise(left.amount) - toPaise(right.amount);
}

export const isGreater = (left: Money, right: Money): boolean => compareMoney(left, right) > 0;
export const isLess = (left: Money, right: Money): boolean => compareMoney(left, right) < 0;
export const equals = (left: Money, right: Money): boolean => compareMoney(left, right) === 0;

/**
 * Adds two amounts.
 *
 * In paise, then back — because `0.1 + 0.2` is not `0.3` in binary floating point, and a rupee
 * total assembled from a hundred such sums drifts visibly. This is display arithmetic only; the
 * authoritative total is the one the API sent.
 */
export function addMoney(left: Money, right: Money): Money {
  assertSameCurrency(left, right);
  return money((toPaise(left.amount) + toPaise(right.amount)) / 100, left.currency);
}

export function subtractMoney(left: Money, right: Money): Money {
  assertSameCurrency(left, right);
  return money((toPaise(left.amount) - toPaise(right.amount)) / 100, left.currency);
}

/** A line total. The only multiplication the client is allowed to do. */
export function multiplyMoney(value: Money, quantity: number): Money {
  return money((toPaise(value.amount) * quantity) / 100, value.currency);
}

/**
 * The percentage off, rounded to a whole number for the "20% off" badge.
 *
 * Rounded down, so a badge never claims more than the customer actually saves.
 */
export function discountPercent(mrp: Money, sellingPrice: Money): number {
  assertSameCurrency(mrp, sellingPrice);
  const listed = toPaise(mrp.amount);
  if (listed <= 0) return 0;
  const saved = listed - toPaise(sellingPrice.amount);
  return saved <= 0 ? 0 : Math.floor((saved * 100) / listed);
}

function assertSameCurrency(left: Money, right: Money): void {
  if (left.currency !== right.currency) {
    throw new Error(`Cannot combine ${left.currency} with ${right.currency}.`);
  }
}
