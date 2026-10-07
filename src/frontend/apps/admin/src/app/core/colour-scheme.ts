import { DOCUMENT } from '@angular/common';
import { Injectable, effect, inject, signal } from '@angular/core';

export type ColourScheme = 'light' | 'dark';

const STORAGE_KEY = 'kh-admin-scheme';

/**
 * The back office's light/dark scheme.
 *
 * Toggles the `dark` class on `<html>`, which is what `_admin-theme.scss` keys its dark palette
 * off, and remembers the choice in `localStorage`. With no stored choice it follows the operating
 * system. `index.html` runs the same decision before first paint so a dark-mode user is not shown
 * a white flash while the bundle loads; this service only has to agree with it.
 *
 * Storage is wrapped in try/catch because it throws in a private window and when site data is
 * blocked; the scheme still works for the session in that case, it just is not remembered.
 */
@Injectable({ providedIn: 'root' })
export class ColourSchemeService {
  private readonly document = inject(DOCUMENT);

  readonly scheme = signal<ColourScheme>(this.initial());

  constructor() {
    effect(() => {
      const scheme = this.scheme();
      const root = this.document.documentElement;
      root.classList.toggle('dark', scheme === 'dark');
      // The browser chrome on a phone follows the page rather than the brand.
      this.document
        .querySelector('meta[name="theme-color"]')
        ?.setAttribute('content', scheme === 'dark' ? '#121826' : '#a84f33');
    });
  }

  toggle(): void {
    const next: ColourScheme = this.scheme() === 'dark' ? 'light' : 'dark';
    this.scheme.set(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      /* not remembered; still applied */
    }
  }

  private initial(): ColourScheme {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored === 'light' || stored === 'dark') return stored;
    } catch {
      /* fall through to the OS preference */
    }
    return typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches
      ? 'dark'
      : 'light';
  }
}
