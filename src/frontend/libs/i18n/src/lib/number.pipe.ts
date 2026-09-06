import { formatNumber } from '@angular/common';
import { LOCALE_ID, Pipe, PipeTransform, inject } from '@angular/core';

/**
 * A count, grouped the Indian way — `12,34,567`, not `1,234,567`.
 *
 * Used for review counts, stock figures and report totals. Prices go through `khMoney`.
 */
@Pipe({ name: 'khNumber' })
export class KhNumberPipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(value: number | null | undefined, digits = '1.0-0'): string {
    if (value === null || value === undefined || Number.isNaN(value)) return '';
    return formatNumber(value, this.locale, digits);
  }
}

/**
 * Large counts abbreviated for a badge: `1.2K`, `3.4L`, `2.1Cr`.
 *
 * Lakh and crore rather than M and B, because the reader is in India and 34,00,000 reads as
 * "34 lakh" long before it reads as "3.4 million".
 */
@Pipe({ name: 'khCompactNumber' })
export class CompactNumberPipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(value: number | null | undefined): string {
    if (value === null || value === undefined || Number.isNaN(value)) return '';

    const abs = Math.abs(value);
    if (abs >= 10_000_000) return `${trim(value / 10_000_000, this.locale)}Cr`;
    if (abs >= 100_000) return `${trim(value / 100_000, this.locale)}L`;
    if (abs >= 1_000) return `${trim(value / 1_000, this.locale)}K`;
    return formatNumber(value, this.locale, '1.0-0');
  }
}

/** One decimal, and no trailing `.0` — `1.2K` but `3K`. */
function trim(value: number, locale: string): string {
  const rounded = Math.round(value * 10) / 10;
  return formatNumber(rounded, locale, Number.isInteger(rounded) ? '1.0-0' : '1.1-1');
}
