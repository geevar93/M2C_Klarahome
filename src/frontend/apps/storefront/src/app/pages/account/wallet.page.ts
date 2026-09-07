import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { WalletResponse, WalletService } from '@klarahome/data-access-account';
import { KhDatePipe, MoneyPipe } from '@klarahome/i18n';
import { Button, EmptyState, Skeleton } from '@klarahome/ui-primitives';
import { WalletEntryView } from '@klarahome/ui-patterns';
import { INR, money } from '@klarahome/domain';

import { CommerceMapper } from '../../core/commerce.mapper';

/**
 * Store credit — `/account/wallet`.
 *
 * A balance, and the ledger it is the sum of. Both, because the ledger is what makes the balance
 * trustworthy: a customer whose credit is not what they expected can see the refund that created it,
 * the order that spent it or the expiry that took it — which is the difference between a number they
 * believe and one they write in about.
 *
 * **The balance is the server's figure, never a sum of the rows on screen.** The ledger is paged, so
 * adding up what is visible would produce a different number on every page, and the wallet is
 * append-only on the server for exactly this reason (Step 12).
 *
 * The page is behind `pricing.wallet`. The route carries the flag, so a deployment without store
 * credit has no such URL and no menu entry rather than a page explaining its own absence.
 */
@Component({
  selector: 'kh-account-wallet-page',
  imports: [Button, EmptyState, KhDatePipe, MoneyPipe, RouterLink, Skeleton],
  template: `
    <h1>Store credit</h1>

    @if (loading()) {
      <kh-skeleton height="6rem" />
    } @else {
      <section class="balance">
        <p class="label">Available balance</p>
        <p class="amount">{{ balance() | khMoney }}</p>
        <p class="note">Applied automatically at checkout, before any other payment.</p>
      </section>

      @if (entries().length === 0) {
        <kh-empty-state
          heading="Nothing here yet"
          message="Store credit appears when a refund is issued to it, or when we give you some."
        >
          <a khButton variant="primary" routerLink="/">Go shopping</a>
        </kh-empty-state>
      } @else {
        <h2>What has happened</h2>

        <ul class="ledger">
          @for (entry of entries(); track entry.id) {
            <li>
              <span class="detail">
                <span class="reason">{{ entry.reason }}</span>
                @if (entry.note) {
                  <span class="meta">{{ entry.note }}</span>
                }
                <span class="meta">
                  {{ entry.occurredAt | khDate: 'd MMM y' }}
                  @if (entry.expiresAt) {
                    · expires {{ entry.expiresAt | khDate: 'd MMM y' }}
                  }
                </span>
              </span>
              <span class="movement" [class.credit]="entry.isCredit">
                {{ entry.isCredit ? '+' : '−' }}{{ entry.amount | khMoney }}
                <span class="meta">balance {{ entry.balanceAfter | khMoney }}</span>
              </span>
            </li>
          }
        </ul>

        @if (cursor()) {
          <button khButton variant="secondary" type="button" [disabled]="loadingMore()" (click)="loadMore()">
            {{ loadingMore() ? 'Loading…' : 'Load more' }}
          </button>
        }
      }
    }
  `,
  styles: `
    :host {
      display: block;
    }

    h1 {
      font-size: var(--text-2xl);
    }

    h2 {
      font-size: var(--text-lg);
    }

    .balance {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      margin-block-end: var(--space-6);
    }

    .label {
      margin: 0;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .amount {
      margin: var(--space-1) 0;
      font-size: var(--text-3xl);
      font-weight: var(--weight-bold);
      font-variant-numeric: tabular-nums;
    }

    .note {
      margin: 0;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .ledger {
      list-style: none;
      margin: 0 0 var(--space-4);
      padding: 0;
    }

    .ledger li {
      display: flex;
      justify-content: space-between;
      gap: var(--space-4);
      padding-block: var(--space-3);
      border-block-end: 1px solid var(--color-border);
    }

    .detail,
    .movement {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      min-width: 0;
    }

    .movement {
      align-items: flex-end;
      text-align: end;
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }

    .movement.credit {
      color: var(--color-success);
    }

    .reason {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .meta {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
      font-weight: var(--weight-regular);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountWalletPage {
  private readonly api = inject(WalletService);
  private readonly mapper = inject(CommerceMapper);

  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly cursor = signal<string | null>(null);
  protected readonly entries = signal<readonly WalletEntryView[]>([]);

  private readonly wallet = signal<WalletResponse | null>(null);

  /** Zero rather than blank when there is no wallet row: a customer with no credit has ₹0 of it. */
  protected readonly balance = computed(() => {
    const wallet = this.wallet();
    return money(wallet?.balance ?? 0, wallet?.currencyCode || INR);
  });

  private readonly currency = computed(() => this.wallet()?.currencyCode || INR);

  constructor() {
    this.api.balance().subscribe((wallet) => {
      this.wallet.set(wallet);
      this.loadTransactions(true);
    });
  }

  protected loadMore(): void {
    if (!this.cursor() || this.loadingMore()) return;
    this.loadTransactions(false);
  }

  private loadTransactions(first: boolean): void {
    if (!first) this.loadingMore.set(true);

    this.api.transactions(first ? null : this.cursor()).subscribe({
      next: (page) => {
        const mapped = page.items.map((entry) => this.mapper.walletEntry(entry, this.currency()));
        this.entries.update((current) => (first ? mapped : [...current, ...mapped]));
        this.cursor.set(page.page.nextCursor);
        this.loading.set(false);
        this.loadingMore.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadingMore.set(false);
      },
    });
  }
}
