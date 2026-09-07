import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Button, EmptyState } from '@klarahome/ui-primitives';

/** 404 — inside the shell, so the navigation is still there to leave by. */
@Component({
  selector: 'kh-not-found-page',
  imports: [Button, EmptyState, RouterLink],
  template: `
    <h1 class="kh-visually-hidden">Page not found</h1>
    <kh-empty-state
      heading="That page does not exist"
      message="The link may be out of date, or the screen may have moved. The navigation on the left is current."
    >
      <a khButton variant="primary" routerLink="/dashboard">Back to the dashboard</a>
    </kh-empty-state>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class NotFoundPage {}
