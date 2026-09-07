import { Injectable, inject } from '@angular/core';
import {
  IdentityApiClient,
  MeResponse,
  RevokedSessionsResponse,
  SessionResponse,
  SignInResponse,
  TwoFactorSetupResponse,
} from '@klarahome/data-access-api';
import { Observable } from 'rxjs';

/**
 * The signed-in staff or vendor user's own account.
 *
 * Sign-in itself belongs to `AuthService` in `@klarahome/data-access-auth`, which both apps share.
 * What is here is everything that comes *after* it and is admin-only: the profile, the enrolled
 * second factor, and the list of sessions the account has open.
 *
 * **The sessions list is a security control, not a convenience.** It is the only way a user finds
 * out that their account is signed in somewhere they are not, and the only way they can end it
 * without an administrator. `07-security-compliance.md` §2 requires it; `revokeAll` exists so the
 * answer to "I think someone has my password" is one button rather than a support ticket.
 */
@Injectable({ providedIn: 'root' })
export class AdminSessionService {
  private readonly identity = inject(IdentityApiClient);

  /** The full profile behind the access token's claims. */
  me(): Observable<MeResponse> {
    return this.identity.adminMeGet();
  }

  sessions(): Observable<SessionResponse[]> {
    return this.identity.adminMeSessionsGet();
  }

  revokeSession(id: string): Observable<void> {
    return this.identity.adminMeSessionRevoke(id, { silentErrors: true });
  }

  /**
   * Ends every other session.
   *
   * `exceptCurrent` is what makes this usable: revoking the current one too would sign the user
   * out at the moment they were securing their account, which reads as the attack succeeding.
   */
  revokeAllSessions(exceptCurrent = true): Observable<RevokedSessionsResponse> {
    return this.identity.adminMeSessionsRevokeAll({ exceptCurrent }, { silentErrors: true });
  }

  /**
   * Starts TOTP enrolment: a secret and the `otpauth://` URI an authenticator app reads.
   *
   * The secret is shown as text as well as encoded in the URI, because a user setting this up on
   * the same device that shows the QR code cannot scan their own screen.
   */
  startTwoFactorSetup(): Observable<TwoFactorSetupResponse> {
    return this.identity.adminMeTwoFactorSetup({ silentErrors: true });
  }

  /** Confirms enrolment with a code from the app, which proves the secret was actually stored. */
  enableTwoFactor(code: string): Observable<void> {
    return this.identity.adminMeTwoFactorEnable({ code }, { silentErrors: true });
  }

  /**
   * Turns it off, which takes the password **and** a current code.
   *
   * Both, deliberately: the password alone would let a borrowed unlocked laptop remove the second
   * factor, and the code alone would let anyone holding the phone do it. Removing a control needs
   * at least what passing it needs.
   */
  disableTwoFactor(password: string, code: string): Observable<void> {
    return this.identity.adminMeTwoFactorDisable({ password, code }, { silentErrors: true });
  }

  /**
   * Changes the password.
   *
   * The API answers a fresh `SignInResponse` because changing a password rotates the token family
   * — every other session is invalidated, and this one needs a token that is not. The caller
   * adopts it through `AuthService`, or the user is signed out by their own password change.
   */
  changePassword(currentPassword: string, newPassword: string): Observable<SignInResponse> {
    return this.identity.adminAuthPasswordChange(
      // `challengeToken` is for the other caller of this endpoint: a user signing in with a
      // temporary password, who must change it before they have a session at all. Here there is
      // already one, so it is null.
      { challengeToken: null, currentPassword, newPassword },
      { silentErrors: true },
    );
  }
}
