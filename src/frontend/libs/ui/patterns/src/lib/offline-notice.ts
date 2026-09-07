import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NetworkStatus } from '@klarahome/util';
import { Icon } from '@klarahome/ui-primitives';

/**
 * The bar that appears when the browser loses its connection.
 *
 * It is worth its few lines because of where this storefront is used: a shopper on a train or in
 * a lift gets a page that does nothing when they tap, and without this the only available reading
 * is "the site is broken". `role="status"` rather than `alert` — it is announced at the next
 * pause rather than cutting across whatever is being read.
 *
 * It never renders during SSR: the server is not the one that is offline, and putting this in the
 * HTML a crawler receives would be a claim about the shop rather than about the connection.
 */
@Component({
  selector: 'kh-offline-notice',
  imports: [Icon],
  template: `
    @if (!online()) {
      <div class="bar" role="status">
        <kh-icon name="offline" size="sm" />
        <span>You are offline. We will keep trying — anything you were doing is still here.</span>
      </div>
    }
  `,
  styles: `
    .bar {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--space-2);
      padding: var(--space-2) var(--space-4);
      background: var(--color-warning);
      color: var(--color-text-inverse);
      font-size: var(--text-sm);
      text-align: center;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OfflineNotice {
  protected readonly online = inject(NetworkStatus).online;
}
