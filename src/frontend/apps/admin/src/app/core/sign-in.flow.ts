import { Injectable, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { NotificationCentreService } from '@klarahome/data-access-admin';
import { AuthService, SessionStore } from '@klarahome/data-access-auth';
import { firstValueFrom } from 'rxjs';

/** Where a user with no `returnUrl` lands. */
const HOME = '/dashboard';

/**
 * What happens around a sign-in and a sign-out.
 *
 * Two things, and both are the kind that are written once or written wrong:
 *
 *  - **`returnUrl` is validated before it is navigated to.** It arrives in the query string, which
 *    means it arrives from whoever wrote the link. An absolute URL there turns the sign-in page
 *    into an open redirect — `?returnUrl=https://evil.example` on a page a user has been told to
 *    trust is the classic phishing set-up — and a protocol-relative `//evil.example` is the same
 *    attack past a naive `startsWith('/')` check. Only a single-slash internal path is honoured;
 *    everything else falls back to the dashboard.
 *  - **Sign-out clears the per-user state as well as the session.** The back office is used on
 *    shared machines in warehouses and offices, and a notification count or a cached page left
 *    behind by the last user is data leaking between accounts.
 */
@Injectable({ providedIn: 'root' })
export class SignInFlow {
  private readonly auth = inject(AuthService);
  private readonly session = inject(SessionStore);
  private readonly notifications = inject(NotificationCentreService);
  private readonly router = inject(Router);

  /**
   * The path a `returnUrl` may be honoured for.
   *
   * Rejects an absolute URL, a protocol-relative one, and `/login` itself — returning somebody to
   * the sign-in page they have just passed is a loop, not a destination.
   */
  safeReturnUrl(candidate: string | null | undefined): string {
    if (!candidate) return HOME;
    if (!candidate.startsWith('/') || candidate.startsWith('//')) return HOME;
    if (candidate.startsWith('/login') || candidate.startsWith('/forgot-password')) return HOME;
    return candidate;
  }

  /** After a successful sign-in. */
  async completeSignIn(returnUrl: string | null | undefined): Promise<void> {
    await this.router.navigateByUrl(this.safeReturnUrl(returnUrl));
  }

  /**
   * True from the moment the user asks to sign out until the sign-in page is up.
   *
   * The shell watches the session and treats it going away as an expiry; this is how it tells a
   * chosen sign-out from one the server forced, so it does not say "your session has ended" to
   * somebody who just ended it.
   */
  readonly signingOut = signal(false);

  /**
   * When the session went away without the user asking.
   *
   * The refresh cookie has been tried and refused (`AuthService.refresh`), the store has been
   * cleared, and the user is on a page every request from which will now fail. Left there, the
   * top bar reads "Signed out" and every panel shows its own load error, which looks like an
   * outage. The honest move is to the sign-in page, with the reason and the way back both in the
   * URL so the sign-in page can say why they are there and return them afterwards.
   */
  async sessionEnded(currentUrl: string): Promise<void> {
    this.notifications.clear();
    await this.router.navigate(['/login'], {
      queryParams: { returnUrl: this.safeReturnUrl(currentUrl), reason: 'expired' },
    });
  }

  /**
   * Ends the session, clears what belonged to it, and returns to sign-in.
   *
   * The order matters: the server first, so the refresh cookie is really gone, then the local
   * state, then the navigation. Reversed, a failed sign-out would leave a user looking at a login
   * screen while still holding a live session.
   */
  async signOut(): Promise<void> {
    this.signingOut.set(true);
    try {
      await firstValueFrom(this.auth.signOut());
      this.notifications.clear();
      this.session.signOut();
      await this.router.navigate(['/login']);
    } finally {
      this.signingOut.set(false);
    }
  }
}
