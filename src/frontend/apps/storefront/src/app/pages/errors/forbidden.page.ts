import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Button, EmptyState } from '@klarahome/ui-primitives';
import { SeoService } from '@klarahome/util';

/**
 * 403 — signed in, but not permitted.
 *
 * Distinct from the sign-in redirect on purpose: sending somebody who is already signed in to a
 * login form tells them their session is broken when the truth is that their account does not
 * have the permission. `permissionGuard` sends them here instead.
 */
@Component({
  selector: 'kh-forbidden-page',
  imports: [Button, EmptyState, RouterLink],
  template: `
    <h1 class="kh-visually-hidden">You do not have access to this page</h1>
    <kh-empty-state
      heading="You do not have access to this page"
      message="Your account is signed in, but it does not have permission for this. If you think it should, ask whoever administers the store."
    >
      <a khButton variant="primary" routerLink="/">Go to the home page</a>
    </kh-empty-state>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForbiddenPage {
  constructor() {
    inject(SeoService).apply({ title: 'No access', noIndex: true, robots: 'noindex, nofollow' });
  }
}
