import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MoneyPipe } from '@klarahome/i18n';
import { Money } from '@klarahome/domain';
import { Button } from '@klarahome/ui-primitives';

/**
 * The basket, kept in the corner of the eye.
 *
 * Once something is in the cart, a shopper who keeps browsing should not have to find the header
 * icon and open a drawer to check out — the count, the subtotal and a "Checkout" button sit in a
 * small fixed panel at the bottom corner, on every page that is not already the cart or the
 * checkout. It is a panel, not a hover: it has to be reachable by touch and by keyboard.
 *
 * **It can be hidden, and stays hidden until the basket changes.** A panel that comes back on the
 * next page after being dismissed is a pop-up; one that comes back when a new item is added is a
 * reminder. The shell owns that rule; this component only reports the tap.
 *
 * On a phone it is a strip across the bottom, stacked above the page's sticky action bar when
 * there is one, so the basket is one tap away without opening anything. From the `md` breakpoint it
 * becomes a card in the corner.
 */
@Component({
  selector: 'kh-cart-dock',
  imports: [Button, MoneyPipe, RouterLink],
  template: `
    @if (open()) {
      <aside class="dock" aria-label="Your cart">
        <div class="head">
          <p class="summary">
            <strong>{{ count() }} {{ count() === 1 ? 'item' : 'items' }}</strong>
            @if (subtotal(); as amount) {
              <span class="amount">{{ amount | khMoney }}</span>
            }
          </p>
          <button class="hide" type="button" (click)="hidden.emit()">Hide</button>
        </div>
        <div class="actions">
          <a khButton variant="primary" size="sm" routerLink="/checkout">Checkout</a>
          <a khButton variant="tertiary" size="sm" routerLink="/cart">View cart</a>
        </div>
      </aside>
    }
  `,
  styles: `
    /* On a phone the panel is a strip across the bottom of the screen, above the sticky action
       bar when a page has one — the bar is the page's action, the strip is the basket's. From 'md'
       it becomes a card in the corner, out of the way of a wider page. */
    :host {
      display: block;
      position: fixed;
      inset-inline: 0;
      inset-block-end: 0;
      z-index: var(--z-header);
    }

    :host([data-raised='true']) {
      inset-block-end: calc(var(--bottom-bar-height) + env(safe-area-inset-bottom));
    }

    .dock {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
      padding: var(--space-2) var(--space-4);
      padding-block-end: max(var(--space-2), env(safe-area-inset-bottom));
      border-block-start: 1px solid var(--color-border);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-lg);
    }

    :host([data-raised='true']) .dock {
      padding-block-end: var(--space-2);
    }

    .head {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      min-width: 0;
    }

    .summary {
      display: flex;
      flex-direction: column;
      margin: 0;
      font-size: var(--text-sm);
      white-space: nowrap;
    }

    .amount {
      color: var(--color-text-muted);
      font-variant-numeric: tabular-nums;
    }

    .hide {
      flex: none;
      margin: 0;
      padding: 0 var(--space-1);
      border: 0;
      background: none;
      color: var(--color-text-muted);
      font: inherit;
      font-size: var(--text-sm);
      text-decoration: underline;
      cursor: pointer;
    }

    .hide:hover,
    .hide:focus-visible {
      color: var(--color-text);
    }

    .actions {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      flex: none;
    }

    @media (min-width: 768px) {
      :host,
      :host([data-raised='true']) {
        inset-inline: auto var(--space-4);
        inset-block-end: var(--space-4);
      }

      .dock,
      :host([data-raised='true']) .dock {
        flex-direction: column;
        align-items: stretch;
        inline-size: 17rem;
        padding: var(--space-3) var(--space-4);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-md);
      }

      .head {
        justify-content: space-between;
        align-items: flex-start;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-raised]': 'raised()', '[style.display]': "open() ? null : 'none'" },
})
export class CartDock {
  readonly open = input(false);
  /** Whether a sticky action bar is on screen beneath, so the strip sits above it on a phone. */
  readonly raised = input(false);
  readonly count = input(0);
  readonly subtotal = input<Money | null>(null);
  /** The shopper tapped hide. What "hidden" means from here is the shell's rule. */
  readonly hidden = output<void>();
}
