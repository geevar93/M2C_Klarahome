import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Button, EmptyState } from '@klarahome/ui-primitives';
import { NetworkStatus, SeoService } from '@klarahome/util';

/**
 * The page a service worker serves when there is no connection at all.
 *
 * It exists now, before the service worker does: the worker is registered at Step 31 with the
 * rest of the operational work, and it needs a page to point at that is already built, already
 * routed and already part of the shell's styling. A page created at the same time as the worker
 * is a page nobody has ever seen render.
 *
 * It reads the live connection state, so a customer who comes back into signal is told to try
 * again rather than left staring at a static apology. Given the same two-action structure as
 * `NotFoundPage` and `ServerErrorPage` — a primary action that reloads and a secondary way home —
 * and a title kept equal to whichever heading is showing, updated live as the connection changes
 * rather than fixed at whatever it was when the page first rendered.
 */
@Component({
  selector: 'kh-offline-page',
  imports: [Button, EmptyState, RouterLink],
  template: `
    <h1 class="kh-visually-hidden">{{ online() ? 'You are back online' : 'You are offline' }}</h1>
    @if (online()) {
      <kh-empty-state
        heading="You are back online"
        message="Your connection has returned. Reload to carry on."
      >
        <div class="actions">
          <button khButton variant="primary" type="button" (click)="reload()">Try again</button>
          <a khButton variant="secondary" routerLink="/">Go to the home page</a>
        </div>
      </kh-empty-state>
    } @else {
      <kh-empty-state
        heading="You are offline"
        message="We cannot reach the shop from here. Your cart is safe — it is kept on our side, not in this tab."
      >
        <div class="actions">
          <button khButton variant="primary" type="button" (click)="reload()">Try again</button>
          <a khButton variant="secondary" routerLink="/">Go to the home page</a>
        </div>
      </kh-empty-state>
    }
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
export class OfflinePage {
  private readonly seo = inject(SeoService);
  protected readonly online = inject(NetworkStatus).online;

  constructor() {
    // Kept in step with `online()` rather than set once: a page whose tab still says "Offline"
    // after the connection has come back is exactly the stale-state feeling this page exists to
    // avoid on the heading itself.
    effect(() =>
      this.seo.apply({
        title: this.online() ? 'You are back online' : 'You are offline',
        noIndex: true,
        robots: 'noindex, nofollow',
      }),
    );
  }

  protected reload(): void {
    // A genuine document reload: the point of this page is that the application's own requests
    // are failing, so asking the router to try again would go through the same broken transport.
    globalThis.location?.reload();
  }
}
