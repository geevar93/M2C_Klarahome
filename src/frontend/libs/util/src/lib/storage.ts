import { Injectable, PLATFORM_ID, inject } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';

/**
 * Browser storage that is safe to call during server-side rendering.
 *
 * The storefront renders on Node, where `localStorage` does not exist, and in a browser whose
 * user may have disabled site data — in which case merely *reading* the property throws. Every
 * call here answers `null` in both cases rather than taking the page down, so a remembered PIN
 * code or a saved filter is a convenience that degrades and never a dependency.
 *
 * Nothing secret goes in here. The access token lives in memory only (docs/07-security-compliance).
 */
@Injectable({ providedIn: 'root' })
export class BrowserStorage {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  get(key: string, scope: StorageScope = 'local'): string | null {
    return this.withStore(scope, (store) => store.getItem(key), null);
  }

  set(key: string, value: string, scope: StorageScope = 'local'): void {
    this.withStore(
      scope,
      (store) => {
        store.setItem(key, value);
        return undefined;
      },
      undefined,
    );
  }

  remove(key: string, scope: StorageScope = 'local'): void {
    this.withStore(
      scope,
      (store) => {
        store.removeItem(key);
        return undefined;
      },
      undefined,
    );
  }

  /** Reads and parses JSON, answering the fallback for absent, unreadable or corrupt values. */
  getJson<T>(key: string, fallback: T, scope: StorageScope = 'local'): T {
    const raw = this.get(key, scope);
    if (raw === null) return fallback;
    try {
      return JSON.parse(raw) as T;
    } catch {
      // A value written by an older version of the app. Discard it rather than crash on it.
      this.remove(key, scope);
      return fallback;
    }
  }

  setJson(key: string, value: unknown, scope: StorageScope = 'local'): void {
    try {
      this.set(key, JSON.stringify(value), scope);
    } catch {
      // Circular value, or the quota is full. Neither is worth an exception to the caller.
    }
  }

  private withStore<T>(scope: StorageScope, read: (store: Storage) => T, fallback: T): T {
    if (!this.isBrowser) return fallback;
    try {
      const store = scope === 'local' ? globalThis.localStorage : globalThis.sessionStorage;
      return store ? read(store) : fallback;
    } catch {
      return fallback;
    }
  }
}

export type StorageScope = 'local' | 'session';
