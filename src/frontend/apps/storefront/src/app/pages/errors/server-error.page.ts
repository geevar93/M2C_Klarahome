import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Button, EmptyState } from '@klarahome/ui-primitives';
import { SeoService } from '@klarahome/util';

/**
 * 500 — the page the shell falls back to when something failed that was not the customer's doing.
 *
 * "Try again" reloads the route rather than the document: on a phone a full reload throws away
 * the hydrated application and every cached response with it, and the failure is usually one
 * request rather than the whole page.
 */
@Component({
  selector: 'kh-server-error-page',
  imports: [Button, EmptyState, RouterLink],
  template: `
    <h1 class="kh-visually-hidden">Something went wrong</h1>
    <kh-empty-state
      heading="Something went wrong at our end"
      message="This is not your fault, and nothing you were doing has been lost. Try again in a moment."
    >
      <div class="actions">
        <button khButton variant="primary" type="button" (click)="retry()">Try again</button>
        <a khButton variant="secondary" routerLink="/">Go to the home page</a>
      </div>
    </kh-empty-state>
  `,
  styles: `
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      justify-content: center;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServerErrorPage {
  private readonly router = inject(Router);

  /**
   * Captured while the navigation to this page is still in flight — `getCurrentNavigation()`
   * answers null once it has finished, and by the time the button is pressed it has.
   */
  private readonly cameFrom = this.router.getCurrentNavigation()?.previousNavigation?.finalUrl ?? null;

  constructor() {
    inject(SeoService).apply({ title: 'Something went wrong', noIndex: true, robots: 'noindex, nofollow' });
  }

  protected retry(): void {
    void this.router.navigateByUrl(this.cameFrom ?? '/');
  }
}
