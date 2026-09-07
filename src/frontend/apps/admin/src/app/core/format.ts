import { formatCurrency, formatDate, getCurrencySymbol } from '@angular/common';
import { DEFAULT_CURRENCY, DEFAULT_LOCALE, DEFAULT_TIME_ZONE } from '@klarahome/i18n';

/**
 * Formatting for the places a pipe cannot reach.
 *
 * A `DataTableColumn.value` is a function, not a template expression, so `| khDate` and `| khMoney`
 * are unavailable in the one place forty admin tables need them. Written by hand at each column
 * that would be `new Date(x).toLocaleString()` — which renders in the *browser's* time zone, and a
 * dispatch cut-off that is an IST clock time then moves for anybody travelling.
 *
 * So these are the pipes' rules, called directly: `Asia/Kolkata` always, `en-IN` always, whole
 * rupees unless there really are paise. They agree with `@klarahome/i18n` because they are the
 * same two lines, and they are here rather than in that library because a library of pipes should
 * not also be a library of functions that duplicate them — the pipes remain what templates use.
 */

/** A date alone — for a column where the minute is not the point. Empty for a missing value. */
export function tableDate(value: string | null | undefined): string {
  return format(value, 'd MMM y');
}

/** A date and a time. What a timeline, an audit row or a dispatch deadline needs. */
export function tableDateTime(value: string | null | undefined): string {
  return format(value, 'd MMM y, h:mm a');
}

/** An amount with its own currency's symbol. Paise are kept only when there are any. */
export function tableMoney(amount: number | null | undefined, currencyCode?: string | null): string {
  if (amount === null || amount === undefined) return '';

  const currency = currencyCode || DEFAULT_CURRENCY;
  const hasPaise = Math.round(amount * 100) % 100 !== 0;
  const symbol = getCurrencySymbol(currency, 'narrow', DEFAULT_LOCALE);
  return formatCurrency(amount, DEFAULT_LOCALE, symbol, currency, hasPaise ? '1.2-2' : '1.0-0');
}

function format(value: string | null | undefined, pattern: string): string {
  if (!value) return '';
  try {
    return formatDate(value, pattern, DEFAULT_LOCALE, DEFAULT_TIME_ZONE);
  } catch {
    // An unparseable timestamp is a server bug, not a reason to blank the row around it.
    return '';
  }
}
