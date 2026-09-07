import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
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
 * again rather than left staring at a static apology.
 */
@Component({
  selector: 'kh-offline-page',
  imports: [Button, EmptyState],
  template: `
    <h1 class="kh-visually-hidden">You are offline</h1>
    @if (online()) {
      <kh-empty-state
        heading="You are back online"
        message="Your connection has returned. Reload to carry on."
      >
        <button khButton variant="primary" type="button" (click)="reload()">Reload the page</button>
      </kh-empty-state>
    } @else {
      <kh-empty-state
        heading="You are offline"
        message="We cannot reach the shop from here. Your basket is safe — it is kept on our side, not in this tab."
      >
        <button khButton variant="secondary" type="button" (click)="reload()">Try again</button>
      </kh-empty-state>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OfflinePage {
  protected readonly online = inject(NetworkStatus).online;

  constructor() {
    inject(SeoService).apply({ title: 'Offline', noIndex: true, robots: 'noindex, nofollow' });
  }

  protected reload(): void {
    // A genuine document reload: the point of this page is that the application's own requests
    // are failing, so asking the router to try again would go through the same broken transport.
    globalThis.location?.reload();
  }
}
