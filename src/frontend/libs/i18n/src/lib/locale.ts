import {
  DEFAULT_CURRENCY_CODE,
  EnvironmentProviders,
  LOCALE_ID,
  makeEnvironmentProviders,
} from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localeEnIn from '@angular/common/locales/en-IN';
import localeEnInExtra from '@angular/common/locales/extra/en-IN';

/**
 * Locale, decided once.
 *
 * `en-IN` is the only locale built today. Hindi and the regional languages are a Phase-2
 * decision, but everything user-visible goes through `$localize` and the pipes here so that
 * adding one is a translation file and a build configuration — never a hunt through templates
 * for hard-coded English (docs/05-frontend-architecture.md §2).
 *
 * `en-IN` is what makes ₹12,34,567 render with the Indian digit grouping — lakhs and crores, not
 * thousands. Registering `en-US` here would silently produce ₹1,234,567 for every price in the
 * catalogue, which is wrong in a way nobody files a bug about and every customer notices.
 */
export const DEFAULT_LOCALE = 'en-IN';
export const DEFAULT_CURRENCY = 'INR';
export const DEFAULT_TIME_ZONE = 'Asia/Kolkata';

let registered = false;

/** Registers the locale data. Idempotent, because both apps and every test may call it. */
export function registerDefaultLocale(): void {
  if (registered) return;
  registerLocaleData(localeEnIn, DEFAULT_LOCALE, localeEnInExtra);
  registered = true;
}

/** Locale providers for an app's `ApplicationConfig`. */
export function provideKlaraHomeI18n(locale: string = DEFAULT_LOCALE): EnvironmentProviders {
  registerDefaultLocale();
  return makeEnvironmentProviders([
    { provide: LOCALE_ID, useValue: locale },
    { provide: DEFAULT_CURRENCY_CODE, useValue: DEFAULT_CURRENCY },
  ]);
}
