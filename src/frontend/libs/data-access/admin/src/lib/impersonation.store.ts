import { Injectable, Signal, computed, signal } from '@angular/core';
import { ImpersonationResponse } from '@klarahome/data-access-api';

/** A support session in progress, as the shell shows it. */
export interface Impersonation {
  /** The impersonated session, which the exit control ends by id. */
  readonly sessionId: string;
  /** Who is being acted as. */
  readonly userId: string;
  readonly displayName: string;
  /** Why, as the operator typed it. It is on the audit entry and on the banner. */
  readonly reason: string;
  /** When it stops being honoured, as epoch milliseconds. */
  readonly expiresAt: number;
  /**
   * The access token minted for the customer.
   *
   * Held and deliberately **not** sent with anything. See the store's own remarks.
   */
  readonly accessToken: string;
}

/**
 * The support impersonation this operator is in, if any.
 *
 * **In memory only, like every access token on this platform.** A reload ends it as far as this app
 * is concerned, and the server's own clock ends it regardless — the token carries the whole window
 * and cannot be refreshed (docs/07-security-compliance.md §2).
 *
 * **The impersonated token is held and not spent.** Every screen in this back office is an admin
 * screen, and sending a customer's token to one would 403 it — so wiring the token into the
 * transport would break the application in exchange for nothing. What the token is *for* is a
 * customer-facing surface to open with it, and this app has none; handing it to the storefront is
 * a cross-origin flow the card does not ask for and the parking lot records.
 *
 * What is real here is everything the security specification requires: the session exists, it is
 * audited at both ends, it is time-boxed, and it is visible the whole time it is running with a
 * control that ends it. That is the difference between impersonation somebody can start and
 * impersonation somebody can be held to.
 */
@Injectable({ providedIn: 'root' })
export class ImpersonationStore {
  private readonly current = signal<Impersonation | null>(null);

  /** The impersonation in progress, or null. */
  readonly actingAs: Signal<Impersonation | null> = this.current.asReadonly();

  readonly isActing = computed(() => this.current() !== null);

  /** Records a started impersonation from the API's own answer. */
  start(response: ImpersonationResponse): void {
    this.current.set({
      sessionId: response.sessionId,
      userId: response.user.id,
      displayName: response.user.email ?? response.user.mobile ?? response.user.id,
      reason: response.reason,
      expiresAt: new Date(response.expiresAt).getTime(),
      accessToken: response.accessToken,
    });
  }

  /** Forgets it. Called after the server has been told, and on a sign-out. */
  end(): void {
    this.current.set(null);
  }
}
