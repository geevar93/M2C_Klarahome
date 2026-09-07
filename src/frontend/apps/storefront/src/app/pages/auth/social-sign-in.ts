import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AuthService } from '@klarahome/data-access-auth';

/**
 * "Continue with Google" — the provider buttons, above the email form.
 *
 * **The list comes from the API, never from a constant here.** `GET /store/auth/external/providers`
 * answers with the providers that are both switched on and actually configured, so a deployment
 * that has not been given a Google client id renders nothing at all and the email form below is
 * simply the whole page. That is the ordinary state of a fresh environment (ADR-014), and it is why
 * this component has no visible empty state and no error state: a shopper does not need to be told
 * that a sign-in method they have never seen is unavailable.
 *
 * Anchors rather than buttons, and that is not a style choice. Starting an external sign-in is a
 * top-level browser navigation to the API, which sets a `SameSite=Lax` state cookie and then
 * redirects on to the provider. A `fetch` would follow that 302 as an XHR, drop the cookie, and
 * return a consent page nobody can render.
 *
 * The provider marks are inline SVG with `aria-hidden`, because the accessible name is the button's
 * own text. Google's own brand guidance requires its four-colour mark be drawn in its own colours,
 * so these two paths are the one deliberate exception to the theme's "everything is a token" rule —
 * and they are marked as such rather than left for somebody to "fix" later.
 */
@Component({
  selector: 'kh-social-sign-in',
  template: `
    @if (providers().length > 0) {
      <div class="providers">
        @for (provider of providers(); track provider.provider) {
          <a
            class="provider"
            [href]="urlFor(provider.provider)"
            (click)="starting.set(provider.provider)"
            [attr.aria-busy]="starting() === provider.provider ? 'true' : null"
          >
            @switch (provider.provider) {
              @case ('google') {
                <svg class="mark" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                  <path
                    fill="#4285F4"
                    d="M23.5 12.27c0-.85-.08-1.67-.22-2.45H12v4.64h6.44a5.5 5.5 0 0 1-2.39 3.61v3h3.86c2.26-2.08 3.56-5.15 3.56-8.8Z"
                  />
                  <path
                    fill="#34A853"
                    d="M12 24c3.24 0 5.96-1.08 7.94-2.91l-3.87-3a7.2 7.2 0 0 1-10.72-3.78h-4v3.09A12 12 0 0 0 12 24Z"
                  />
                  <path
                    fill="#FBBC05"
                    d="M5.35 14.3a7.12 7.12 0 0 1 0-4.6V6.62h-4a12 12 0 0 0 0 10.77l4-3.09Z"
                  />
                  <path
                    fill="#EA4335"
                    d="M12 4.75c1.77 0 3.35.61 4.6 1.8l3.42-3.42C17.95 1.19 15.24 0 12 0A12 12 0 0 0 1.35 6.62l4 3.09A7.16 7.16 0 0 1 12 4.75Z"
                  />
                </svg>
              }
              @case ('facebook') {
                <svg class="mark" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
                  <path
                    fill="#1877F2"
                    d="M24 12.07C24 5.4 18.63 0 12 0S0 5.4 0 12.07C0 18.1 4.39 23.09 10.13 24v-8.44H7.08v-3.49h3.05V9.41c-3.01 0-5.46 2.46-5.46 5.5v.65H7.7l-.49 3.49h2.6V24C19.61 23.09 24 18.1 24 12.07Z"
                  />
                </svg>
              }
            }
            <span>Continue with {{ label(provider) }}</span>
          </a>
        }
      </div>

      <!--
        A separator, not a heading. The rule is drawn with borders on the pseudo-elements so the
        word sits in a gap rather than on top of a line, and the word itself is hidden from assistive
        technology: "or" announced between two groups of controls is noise, and the groups are
        already distinguishable by their own labels.
      -->
      <p class="divider"><span aria-hidden="true">or</span></p>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .providers {
      display: grid;
      gap: var(--space-3);
    }

    .provider {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--space-3);
      min-block-size: var(--touch-target-min);
      padding-inline: var(--space-4);
      border: 1px solid var(--color-border-strong);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      color: var(--color-text);
      font-weight: var(--weight-semibold);
      text-decoration: none;
      transition:
        background-color var(--duration-fast) var(--ease-standard),
        border-color var(--duration-fast) var(--ease-standard);
    }

    .provider:hover {
      background: var(--color-primary-subtle);
      border-color: var(--color-primary);
    }

    /* The one place a provider's own colours override the theme — see the class comment. */
    .mark {
      inline-size: 20px;
      block-size: 20px;
      flex: none;
    }

    .divider {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      margin-block: var(--space-5) var(--space-4);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .divider::before,
    .divider::after {
      content: '';
      flex: 1;
      border-block-start: 1px solid var(--color-border);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SocialSignIn {
  private readonly auth = inject(AuthService);

  /** Carried through the whole redirect round trip, so a shopper lands back where they started. */
  readonly returnUrl = input<string | undefined>(undefined);

  /**
   * Which provider the browser is currently leaving for, if any. Marks the anchor `aria-busy` — the
   * page is about to be replaced, and on a slow connection that is several seconds of a screen that
   * would otherwise look like it ignored the tap.
   */
  protected readonly starting = signal<string | null>(null);

  /**
   * Resolved once, when the component is created. The list does not change while somebody is
   * looking at a sign-in form, and re-asking on every render would be a request per change
   * detection pass.
   */
  protected readonly providers = toSignal(this.auth.externalProviders(), { initialValue: [] });

  protected urlFor(provider: string): string {
    return this.auth.externalSignInUrl(provider, this.returnUrl());
  }

  /**
   * What the button says. The API sends the provider enum's own name — "Facebook" — and the brand
   * it belongs to is now Meta, which is what a shopper is looking for on the button.
   */
  protected label(provider: { provider: string; displayName: string }): string {
    return provider.provider === 'facebook' ? 'Meta' : provider.displayName;
  }
}
