import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';

import { THEME_MARKER_TOKEN, THEME_PRESETS, presetIdFromTokens } from './theme-presets';

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
    const resolved = resolvePreset(tokens);

    for (const key of this.applied) root.style.removeProperty(key);

    const next = new Set<string>();
    for (const [key, value] of Object.entries(resolved ?? {})) {
      if (!THEME_TOKEN_KEY_PATTERN.test(key)) continue;
      root.style.setProperty(key, value);
      next.add(key);
    }
    this.applied = next;

    // The browser chrome follows the primary: on a phone the address bar is tinted with
    // `theme-color`, and a store in indigo with a coffee-coloured toolbar looks like two sites.
    // The markup's own value is kept as the fallback for a theme that names no primary.
    const meta = this.document.querySelector<HTMLMetaElement>('meta[name="theme-color"]');
    if (meta) {
      const fallback = meta.getAttribute('data-default') ?? meta.getAttribute('content') ?? '';
      if (!meta.getAttribute('data-default')) meta.setAttribute('data-default', fallback);
      meta.setAttribute('content', resolved?.['--color-primary'] ?? fallback);
    }
  }
}

/**
 * Expands a stored token set that is really a *pointer* to one of the built-in presets.
 *
 * A preset is code — nine palettes in `theme-presets.ts`, each checked against the contrast floor.
 * The settings document records which one a store picked, in `--kh-theme`. It used to record a
 * *copy* of the preset's two dozen colours as well, and that copy was frozen at the moment an
 * operator clicked the card: improving a palette, adding a token role, or fixing a contrast
 * failure then reached no store that had already chosen it, and every tenant needed somebody to
 * open the admin and re-pick the same theme before a fix shipped. The storefront looked
 * monotonous long after the palettes were not.
 *
 * So a set that is *only* the marker is expanded here, from whatever that preset is today. A set
 * with colours of its own in it is a hand-edited theme and is applied exactly as stored — that is
 * the difference the admin's picker already draws between a selected card and "Custom", and the
 * reason the marker alone is what the picker now saves.
 */
function resolvePreset(
  tokens: Readonly<Record<string, string>> | undefined,
): Readonly<Record<string, string>> | undefined {
  const keys = Object.keys(tokens ?? {});
  if (keys.length !== 1 || keys[0] !== THEME_MARKER_TOKEN) return tokens;

  const id = presetIdFromTokens(tokens);
  return THEME_PRESETS.find((preset) => preset.id === id)?.tokens ?? tokens;
}
