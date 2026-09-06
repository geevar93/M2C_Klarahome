import { Injectable, Signal, computed, signal } from '@angular/core';

/** What the access token says about the person holding it. */
export interface Session {
  readonly userId: string;
  readonly displayName: string;
  /** Present only for a vendor user; platform staff and customers have none. */
  readonly vendorId?: string;
  /** Granted permissions, as the API spells them: `orders.suborder.cancel`. */
  readonly permissions: readonly string[];
  readonly roles: readonly string[];
  /** When the access token stops being accepted. */
  readonly expiresAt: number;
}

/**
 * The signed-in session, and the access token that proves it.
 *
 * **The access token is held in memory and nowhere else.** Not `localStorage`, not a readable
 * cookie, not `sessionStorage` (docs/05-frontend-architecture.md §5): anything a script on the
 * page can read, an injected script can exfiltrate. What survives a reload is the refresh token,
 * which the server sets as an HttpOnly cookie the page cannot see — so a new tab proves itself by
 * calling `/auth/refresh`, and a stolen bundle carries nothing.
 */
@Injectable({ providedIn: 'root' })
export class SessionStore {
  private readonly token = signal<string | null>(null);
  private readonly current = signal<Session | null>(null);

  /** Null until the first refresh or sign-in completes. */
  readonly session: Signal<Session | null> = this.current.asReadonly();
  readonly isAuthenticated = computed(() => this.current() !== null);
  readonly vendorId = computed(() => this.current()?.vendorId ?? null);

  /** The bearer token, or null. Read by the auth interceptor and by nothing else. */
  accessToken(): string | null {
    return this.token();
  }

  /**
   * True when the token is within its last thirty seconds.
   *
   * Refreshing on expiry rather than before it means the request that discovers the expiry is the
   * one that fails; the margin covers the round trip and a clock that is slightly out.
   */
  isExpiring(marginMs = 30_000): boolean {
    const session = this.current();
    return session !== null && session.expiresAt - Date.now() <= marginMs;
  }

  /** Whether this session carries a permission. The API enforces the real check regardless. */
  hasPermission(permission: string): boolean {
    return this.current()?.permissions.includes(permission) ?? false;
  }

  hasAnyPermission(permissions: readonly string[]): boolean {
    const granted = this.current()?.permissions;
    return granted ? permissions.some((permission) => granted.includes(permission)) : false;
  }

  signIn(accessToken: string, session: Session): void {
    this.token.set(accessToken);
    this.current.set(session);
  }

  signOut(): void {
    this.token.set(null);
    this.current.set(null);
  }
}
