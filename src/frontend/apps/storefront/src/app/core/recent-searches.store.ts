import { Injectable, Signal, inject, signal } from '@angular/core';
import { SuggestionView } from '@klarahome/ui-patterns';
import { BrowserStorage } from '@klarahome/util';

const STORAGE_KEY = 'kh.recent-searches';
const LIMIT = 6;

/**
 * What this visitor searched for before.
 *
 * Offered by the header's search box when the field is empty — which is the only moment recent
 * searches are more useful than the server's suggestions. Kept in `localStorage`, in this browser,
 * and **never sent anywhere**: the API already logs queries for ranking (Step 19), and a second
 * copy attributed to a person is a profile nobody asked for.
 *
 * Six entries, because the panel has to be scannable on a 360px screen without scrolling, and
 * because a list long enough to search through has stopped being a shortcut.
 */
@Injectable({ providedIn: 'root' })
export class RecentSearchesStore {
  private readonly storage = inject(BrowserStorage);
  private readonly terms = signal<readonly string[]>([]);

  readonly items: Signal<readonly string[]> = this.terms.asReadonly();

  /**
   * Read once, in the constructor.
   *
   * Not lazily on first use: the header's suggestion list is a `computed`, and a lazy read that
   * wrote a signal on first access would be a write inside a computed — which Angular refuses,
   * correctly. `BrowserStorage` answers the fallback during server rendering, so the server sees
   * an empty history and the browser sees the real one.
   */
  constructor() {
    this.terms.set(this.storage.getJson<string[]>(STORAGE_KEY, []));
  }

  /** The remembered searches as the suggestion panel renders them. */
  suggestions(): SuggestionView[] {
    return this.terms().map((text) => ({
      kind: 'query',
      text,
      href: `/search?q=${encodeURIComponent(text)}`,
      image: null,
      price: null,
    }));
  }

  /**
   * Records a search, most recent first and case-insensitively de-duplicated.
   *
   * "sofa" typed after "Sofa" is the same search to a shopper, and a list showing both is a list
   * that looks broken.
   */
  record(term: string): void {
    const trimmed = term.trim();
    if (!trimmed) return;

    const next = [
      trimmed,
      ...this.terms().filter((entry) => entry.toLowerCase() !== trimmed.toLowerCase()),
    ].slice(0, LIMIT);

    this.terms.set(next);
    this.storage.setJson(STORAGE_KEY, next);
  }

  clear(): void {
    this.terms.set([]);
    this.storage.remove(STORAGE_KEY);
  }
}
