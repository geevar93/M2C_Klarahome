import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Money } from '@klarahome/domain';
import { MoneyPipe } from '@klarahome/i18n';
import { Button, Drawer, EmptyState, Icon, Skeleton } from '@klarahome/ui-primitives';

import { MiniCartLine } from './navigation.model';

/**
 * The cart, as a drawer, read-only.
 *
 * It shows what is in the basket and takes the customer to the cart page to change it. That is a
 * deliberate limit for now: quantity controls in a mini-cart need the optimistic update and the
 * rollback banner that Step 25 builds, and a stepper that silently fails is worse than a stepper
 * that is not there.
 *
 * Every amount arrives as the API computed it and is rendered by `khMoney`. A cart summary that
 * did its own arithmetic would be a second implementation of pricing in the browser, and the one
 * on the server is the one that charges.
 */
@Component({
  selector: 'kh-mini-cart',
  imports: [Button, Drawer, EmptyState, Icon, MoneyPipe, RouterLink, Skeleton],
  template: `
    <kh-drawer [open]="open()" side="end" label="Your cart" (closed)="closed.emit()">
      <div class="head">
        <h2 class="title">Your cart</h2>
        <button
          khButton
          variant="tertiary"
          [iconOnly]="true"
          type="button"
          aria-label="Close cart"
          (click)="closed.emit()"
        >
          <kh-icon name="close" />
        </button>
      </div>

      @if (loading()) {
        <div class="lines">
          @for (placeholder of [1, 2, 3]; track placeholder) {
            <kh-skeleton height="3rem" />
          }
        </div>
      } @else if (lines().length === 0) {
        <kh-empty-state heading="Your cart is empty" message="Items you add will appear here.">
          <a khButton variant="primary" routerLink="/" (click)="closed.emit()">Start shopping</a>
        </kh-empty-state>
      } @else {
        <ul class="lines">
          @for (line of lines(); track line.id) {
            <li class="line">
              <span class="kh-placeholder-media thumb" aria-hidden="true">{{ line.reference }}</span>
              <span class="detail">
                <span class="name">{{ line.name }}</span>
                <span class="meta">Qty {{ line.quantity }} · {{ line.lineTotal | khMoney }}</span>
              </span>
            </li>
          }
        </ul>

        <div class="foot">
          @if (total(); as amount) {
            <p class="total">
              <span>Subtotal</span><span>{{ amount | khMoney }}</span>
            </p>
            <p class="note">Delivery and taxes are calculated at checkout.</p>
          }
          <a khButton variant="primary" [block]="true" routerLink="/cart" (click)="closed.emit()"
            >View cart</a
          >
        </div>
      }
    </kh-drawer>
  `,
  styles: `
    .head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
      padding: var(--space-2) var(--space-2) var(--space-2) var(--space-4);
      border-block-end: 1px solid var(--color-border);
    }

    .title {
      margin: 0;
      font-size: var(--text-lg);
    }

    .lines {
      list-style: none;
      margin: 0;
      padding: var(--space-4);
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    .line {
      display: flex;
      gap: var(--space-3);
      align-items: center;
    }

    .thumb {
      width: var(--space-16);
      height: var(--space-16);
      flex: none;
      font-size: var(--text-xs);
      overflow: hidden;
    }

    .detail {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      min-width: 0;
    }

    .name {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .meta {
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .foot {
      margin-block-start: auto;
      padding: var(--space-4);
      border-block-start: 1px solid var(--color-border);
    }

    .total {
      display: flex;
      justify-content: space-between;
      margin: 0 0 var(--space-1);
      font-weight: var(--weight-medium);
    }

    .note {
      margin: 0 0 var(--space-3);
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MiniCart {
  readonly open = input(false);
  readonly loading = input(false);
  readonly lines = input<readonly MiniCartLine[]>([]);
  /** The subtotal the API computed. Absent while unknown; never derived here. */
  readonly total = input<Money | null>(null);
  readonly closed = output<void>();
}
