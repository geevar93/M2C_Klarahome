import { formatDate } from '@angular/common';
import { LOCALE_ID, Pipe, PipeTransform, inject } from '@angular/core';

import { DEFAULT_TIME_ZONE } from './locale';

/**
 * Renders an API timestamp in the store's time zone.
 *
 * The API sends UTC, always (docs/04-api-specification.md §1). Angular's own `date` pipe would
 * render it in the *browser's* zone, which means a customer travelling shows an order placed at
 * 22:00 IST as having been placed the next day — and a vendor's dispatch cut-off, which is an
 * IST clock time, would move. Everything here is rendered in `Asia/Kolkata` unless a caller
 * explicitly asks otherwise.
 */
@Pipe({ name: 'khDate' })
export class KhDatePipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(
    value: string | number | Date | null | undefined,
    format = 'd MMM y, h:mm a',
    timeZone: string = DEFAULT_TIME_ZONE,
  ): string {
    if (value === null || value === undefined || value === '') return '';
    try {
      return formatDate(value, format, this.locale, timeZone);
    } catch {
      // An unparseable timestamp is a server bug, not a reason to blank the whole screen.
      return '';
    }
  }
}

/**
 * "2 hours ago", for timelines where the exact minute is not the point.
 *
 * Falls back to an absolute date past a week: "37 days ago" is a number the reader has to convert
 * back into a date anyway.
 */
@Pipe({ name: 'khRelativeTime' })
export class RelativeTimePipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(value: string | number | Date | null | undefined, now: number = Date.now()): string {
    if (value === null || value === undefined || value === '') return '';

    const time = value instanceof Date ? value.getTime() : Date.parse(String(value));
    if (Number.isNaN(time)) return '';

    const seconds = Math.round((now - time) / 1000);
    const formatter = new Intl.RelativeTimeFormat(this.locale, { numeric: 'auto' });

    if (Math.abs(seconds) < 60) return formatter.format(-seconds, 'second');
    if (Math.abs(seconds) < 3600) return formatter.format(-Math.round(seconds / 60), 'minute');
    if (Math.abs(seconds) < 86_400) return formatter.format(-Math.round(seconds / 3600), 'hour');
    if (Math.abs(seconds) < 604_800) return formatter.format(-Math.round(seconds / 86_400), 'day');

    return formatDate(time, 'd MMM y', this.locale, DEFAULT_TIME_ZONE);
  }
}
