import { Directive, TemplateRef, ViewContainerRef, effect, inject, input } from '@angular/core';

import { SessionStore } from './session.store';

/**
 * Renders its content only when the session holds one of the named permissions.
 *
 * ```html
 * <button *khHasPermission="'orders.suborder.cancel'" khButton>Cancel</button>
 * <div *khHasPermission="['returns.qc.manage', 'returns.refund.manage']">…</div>
 * ```
 *
 * **This hides UI. It does not authorise anything** (`docs/05-frontend-architecture.md` §4.3, and
 * §2 of the security document). The API enforces the real check on every endpoint and answers 403
 * to a caller who reaches one another way — through the console, a bookmark, or a build where
 * this directive was removed. A back office that relied on hidden buttons for its access control
 * would be one `display: none` away from having none.
 *
 * What it *is* for is the other half of the same problem: a screen offering forty controls of
 * which the user may use six is a screen where every action is a coin toss, and a 403 toast is a
 * poor way to learn what your role is.
 *
 * **Structural rather than a class or an `@if`.** The content is not in the DOM at all, which
 * matters because a hidden-with-CSS button is still focusable by Tab and still read by a screen
 * reader — a keyboard user would tab onto a control they cannot use and be told nothing.
 *
 * It reacts to the session changing, because it can: a token refresh that comes back with fewer
 * permissions (a role edited while somebody was signed in) takes the controls away rather than
 * leaving them until the next full page load.
 */
@Directive({ selector: '[khHasPermission]' })
export class HasPermission {
  private readonly store = inject(SessionStore);
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);

  /** One permission code, or several of which any will do. */
  readonly khHasPermission = input.required<string | readonly string[]>();

  private rendered = false;

  constructor() {
    effect(() => {
      const required = this.khHasPermission();
      const codes = typeof required === 'string' ? [required] : required;

      // Read through the store's signal, so a session change re-runs this effect.
      const session = this.store.session();
      const granted = session !== null && codes.some((code) => session.permissions.includes(code));

      if (granted === this.rendered) return;

      if (granted) this.container.createEmbeddedView(this.template);
      else this.container.clear();
      this.rendered = granted;
    });
  }
}

/**
 * The scope equivalent: content only platform staff see.
 *
 * A vendor user's token carries a `vendorId` and the API confines every query to that seller. Some
 * controls are not scoped-down versions of a platform control but platform-only outright — the
 * commission plan a seller is on, another seller's record — and no permission distinguishes them,
 * because a vendor owner legitimately holds `vendors.vendor.read` for their own account.
 */
@Directive({ selector: '[khPlatformOnly]' })
export class PlatformOnly {
  private readonly store = inject(SessionStore);
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);

  private rendered = false;

  constructor() {
    effect(() => {
      const isPlatform = this.store.session() !== null && this.store.vendorId() === null;
      if (isPlatform === this.rendered) return;

      if (isPlatform) this.container.createEmbeddedView(this.template);
      else this.container.clear();
      this.rendered = isPlatform;
    });
  }
}
