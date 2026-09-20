import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Button, EmptyState } from '@klarahome/ui-primitives';
import { SeoService } from '@klarahome/util';

/**
 * 403 — signed in, but not permitted.
 *
 * Distinct from the sign-in redirect on purpose: sending somebody who is already signed in to a
 * login form tells them their session is broken when the truth is that their account does not
 * have the permission. `permissionGuard` sends them here instead.
 *
 * Given the same two-action structure as {@link NotFoundPage} and {@link ServerErrorPage} — "Try
 * again" (a fresh navigation past the guard, in case a permission just changed) and "Go to the
 * home page" — rather than the single action it had before, so all four of the states this shell
 * can land on read the same way. It reuses the 500's artwork rather than going without: `error-500.svg`
 * is generic enough ("something stopped this page"), and a bare heading with no illustration is
 * the odd one out next to 404 and 500 rather than a deliberate choice for this one.
 */
@Component({
  selector: 'kh-forbidden-page',
  imports: [Button, EmptyState, RouterLink],
  template: `
    <h1 class="kh-visually-hidden">You do not have access to this page</h1>
    <img class="art" src="brand/error-500.svg" alt="" width="240" height="160" />
    <kh-empty-state
      heading="You do not have access to this page"
      message="Your account is signed in, but it does not have permission for this. If you think it should, ask whoever administers the store."
    >
      <div class="actions">
        <button khButton variant="primary" type="button" (click)="retry()">Try again</button>
        <a khButton variant="secondary" routerLink="/">Go to the home page</a>
      </div>
    </kh-empty-state>
  `,
  styles: `
    .art {
      display: block;
      margin-inline: auto;
      margin-block-start: var(--space-6);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      justify-content: center;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForbiddenPage {
  private readonly router = inject(Router);

  /** Captured while this navigation is still in flight, the same way `ServerErrorPage` does. */
  private readonly cameFrom = this.router.getCurrentNavigation()?.previousNavigation?.finalUrl ?? null;

  constructor() {
    inject(SeoService).apply({
      title: 'You do not have access to this page',
      noIndex: true,
      robots: 'noindex, nofollow',
    });
  }

  protected retry(): void {
    void this.router.navigateByUrl(this.cameFrom ?? '/');
  }
}
