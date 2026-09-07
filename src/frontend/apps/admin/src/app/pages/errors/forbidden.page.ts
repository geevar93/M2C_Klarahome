import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Button, EmptyState } from '@klarahome/ui-primitives';

/**
 * 403 — signed in, and not permitted.
 *
 * Deliberately distinct from the sign-in redirect: sending somebody who is already signed in to a
 * login form tells them their session is broken, when the truth is that their role does not carry
 * the permission. The guards send them here instead, and the wording says which of the two it is
 * so that the next action — asking an administrator — is obvious.
 */
@Component({
  selector: 'kh-forbidden-page',
  imports: [Button, EmptyState, RouterLink],
  template: `
    <h1 class="kh-visually-hidden">You do not have access to this screen</h1>
    <kh-empty-state
      heading="You do not have access to this screen"
      message="Your account is signed in, but its roles do not include the permission this screen needs. If it should, ask whoever administers the platform."
    >
      <a khButton variant="primary" routerLink="/dashboard">Back to the dashboard</a>
    </kh-empty-state>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForbiddenPage {}
