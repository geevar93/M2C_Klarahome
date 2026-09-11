import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

/** A CSS custom property name: two leading hyphens, then lower-kebab-case. */
const THEME_TOKEN_KEY_PATTERN = /^--[a-z][a-z0-9-]*$/;

/**
 * The white-label mechanism itself (docs/10-design-system.md §6).
 *
 * Every design token in `_tokens.scss` is a CSS custom property on `:root`. This service is the
 * one place that ever calls `style.setProperty` on the document element, and it does so with
 * whatever `BrandingSettings.ThemeTokens` the tenant's `/store/config` answered with — so a
 * re-theme is a settings document changing, never a rebuild of the Angular bundle or the SCSS it
 * compiled from.
 *
 * **Runs during SSR as well as in the browser.** `apply` writes onto the same `Document` Angular
 * is rendering, the way `SeoService` writes meta tags — so the very first response a shopper (or a
 * crawler) receives already carries the tenant's palette, rather than a flash of Klara Home's
 * defaults that gets corrected after hydration.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private applied: ReadonlySet<string> = new Set();

  /**
   * Applies a tenant's token overrides to `:root`, replacing whatever this service applied last.
   *
   * Keys are re-validated here even though the API already rejects a malformed one on save: a
   * cached or hand-edited settings document is data this app did not just validate, and
   * `style.setProperty` with an untrusted property name is otherwise a way to write anything onto
   * the root element.
   */
  apply(tokens: Readonly<Record<string, string>> | undefined): void {
    const root = this.document.documentElement;

    for (const key of this.applied) root.style.removeProperty(key);

    const next = new Set<string>();
    for (const [key, value] of Object.entries(tokens ?? {})) {
      if (!THEME_TOKEN_KEY_PATTERN.test(key)) continue;
      root.style.setProperty(key, value);
      next.add(key);
    }
    this.applied = next;
  }
}
