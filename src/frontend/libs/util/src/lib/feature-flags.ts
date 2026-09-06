import { Injectable, Signal, computed, inject, signal } from '@angular/core';

import { RUNTIME_CONFIG } from './runtime-config';

/**
 * Feature flags, read the same way everywhere.
 *
 * The backend owns them — a flag is a row an operator edits without a deploy, and the API is the
 * only thing that enforces one. What this holds is the client's copy, seeded from `config.json`
 * and refreshed from `GET /store/config` once the app is running. **A hidden button is a
 * courtesy, not a control**: every flag that gates a capability is checked server-side too.
 */
@Injectable({ providedIn: 'root' })
export class FeatureFlags {
  private readonly flags = signal<Readonly<Record<string, boolean>>>(inject(RUNTIME_CONFIG).features);

  /** All known flags, for the diagnostics screen. */
  readonly all: Signal<Readonly<Record<string, boolean>>> = this.flags.asReadonly();

  /** Whether a flag is on. An unknown flag is off — new capabilities are opt-in. */
  isEnabled(key: string): boolean {
    return this.flags()[key] === true;
  }

  /** A signal for a single flag, so a template re-renders when an operator flips it. */
  flag(key: string): Signal<boolean> {
    return computed(() => this.flags()[key] === true);
  }

  /**
   * Replaces the set from the server's answer.
   *
   * A whole-set replace rather than a merge: a flag the server no longer publishes has been
   * removed, and merging would leave it switched on forever in a long-lived tab.
   */
  replaceAll(flags: Readonly<Record<string, boolean>>): void {
    this.flags.set({ ...flags });
  }
}
