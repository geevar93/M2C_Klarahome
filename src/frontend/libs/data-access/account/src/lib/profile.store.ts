import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { IdentityApiClient, MeResponse, OtpChannel, UpdateMeBody } from '@klarahome/data-access-api';
import { Observable, finalize, tap } from 'rxjs';

/**
 * Who the customer is: `GET /store/me`, and the one PATCH that changes it.
 *
 * Read once and shared, because four screens want a piece of it — the account dashboard's greeting,
 * the profile form, the checkout's prefilled contact details, and the header. Four independent
 * requests for the same document on one navigation is the shape this avoids.
 *
 * The **user** and the **profile** are two objects and stay two here. The user is identity — the
 * mobile number, the email, whether each is verified — and the profile is what the customer told us
 * about themselves. They have different lifecycles and different endpoints behind them, and a
 * merged view model would hide that changing a name and changing an email address are not the same
 * kind of act: one is a PATCH, the other is a verification.
 */
@Injectable({ providedIn: 'root' })
export class ProfileStore {
  private readonly api = inject(IdentityApiClient);

  private readonly me = signal<MeResponse | null>(null);
  private readonly loading = signal(false);
  private readonly loaded = signal(false);
  private readonly saving = signal(false);

  readonly current: Signal<MeResponse | null> = this.me.asReadonly();
  readonly isLoading: Signal<boolean> = this.loading.asReadonly();
  readonly isSaving: Signal<boolean> = this.saving.asReadonly();
  readonly hasLoaded: Signal<boolean> = this.loaded.asReadonly();

  readonly user = computed(() => this.me()?.user ?? null);
  readonly profile = computed(() => this.me()?.profile ?? null);

  /**
   * What to call the customer.
   *
   * A first name if we have one, otherwise the mobile number they signed up with — never "there".
   * A greeting addressed to nobody is worse than no greeting, and this storefront's most common
   * customer signs in with an OTP and has never typed a name.
   */
  readonly displayName = computed(() => {
    const profile = this.profile();
    const user = this.user();
    const name = [profile?.firstName, profile?.lastName].filter(Boolean).join(' ').trim();
    return name || user?.mobile || user?.email || '';
  });

  load(): Observable<MeResponse> {
    this.loading.set(true);
    return this.api.storeMeGet({ silentErrors: true }).pipe(
      tap((me) => this.me.set(me)),
      finalize(() => {
        this.loading.set(false);
        this.loaded.set(true);
      }),
    );
  }

  loadOnce(): void {
    if (this.loaded() || this.loading()) return;
    this.load().subscribe({ error: () => this.loaded.set(true) });
  }

  update(body: UpdateMeBody): Observable<MeResponse> {
    this.saving.set(true);
    return this.api.storeMePatch(body, { silentErrors: true }).pipe(
      tap((me) => this.me.set(me)),
      finalize(() => this.saving.set(false)),
    );
  }

  /**
   * Asks for a verification code for an email address or a mobile number.
   *
   * `Sms`, `Email` or `WhatsApp` — the API's own channel names. Any of them can be turned off by
   * an operator (Step 7A's degraded-delivery flags), in which case the API refuses and the page
   * says so. It is not this store's business to know which.
   */
  requestVerification(channel: OtpChannel): Observable<void> {
    return this.api.storeMeVerifyRequest({ channel }, { silentErrors: true });
  }

  confirmVerification(channel: OtpChannel, code: string): Observable<void> {
    return this.api.storeMeVerifyConfirm({ channel, code }, { silentErrors: true }).pipe(
      // The badge beside the field is driven by `mobileVerified` / `emailVerified`, so the document
      // is re-read rather than patched locally: which of the two moved is the server's answer.
      tap(() => this.load().subscribe({ error: () => undefined })),
    );
  }

  /** Drops the document. Called on sign-out. */
  clear(): void {
    this.me.set(null);
    this.loaded.set(false);
  }
}
