import { formatCurrency, formatNumber, getCurrencySymbol } from '@angular/common';
import { LOCALE_ID, Pipe, PipeTransform, inject } from '@angular/core';
import { Money } from '@klarahome/domain';

import { DEFAULT_CURRENCY } from './locale';

/**
 * Renders a `Money` as a price.
 *
 * Whole rupees by default — `₹1,499`, not `₹1,499.00`. Indian retail prices are quoted in whole
 * rupees and the trailing `.00` is noise on a product grid; a price with real paise in it keeps
 * them, so a ₹1,499.50 shipping charge is not rounded away in front of the customer.
 *
 * It takes a `Money` rather than a number so a price can never be rendered without its currency,
 * which is what would let a multi-currency Phase 2 quietly display the wrong symbol.
 */
@Pipe({ name: 'khMoney' })
export class MoneyPipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(value: Money | null | undefined, display: 'symbol' | 'code' | 'none' = 'symbol'): string {
    if (value === null || value === undefined) return '';

    const currency = value.currency || DEFAULT_CURRENCY;
    const hasPaise = Math.round(value.amount * 100) % 100 !== 0;
    const digits = hasPaise ? '1.2-2' : '1.0-0';

    if (display === 'none') return formatNumber(value.amount, this.locale, digits);

    const symbol = display === 'code' ? currency : getCurrencySymbol(currency, 'narrow', this.locale);
    return formatCurrency(value.amount, this.locale, symbol, currency, digits);
  }
}
