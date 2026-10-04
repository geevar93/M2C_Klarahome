import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationStart, Router, RouterLink } from '@angular/router';
import { ICON_NAMES, Icon, IconName } from '@klarahome/ui-primitives';

import { AdminAttentionItem } from './admin.model';

/**
 * The bell and the panel it opens.
 *
 * The panel lists the work queues that have something in them (see `AdminAttentionItem`), each a
 * link into its pre-filtered screen, and ends with a link to the message log when the session may
 * read it. The dot on the bell is a count of what is waiting, not of what is "unread": there is
 * nothing to mark as read, because the number goes down when the work is done and not before.
 *
 * Esc and a click outside close it, and Esc returns focus to the bell. Following a link closes it
 * as well, because the page underneath has changed.
 */
@Component({
  selector: 'kh-admin-notifications',
  imports: [Icon, RouterLink],
  template: `
    <button
      type="button"
      class="bell"
      aria-haspopup="true"
      aria-controls="admin-attention-panel"
      [attr.aria-expanded]="open()"
      [attr.aria-label]="label()"
      (click)="open.set(!open())"
    >
      <kh-icon name="bell" />
      @if (total() > 0) {
        <span class="dot" aria-hidden="true"></span>
      }
    </button>

    @if (open()) {
      <section id="admin-attention-panel" class="panel" aria-label="Needs your attention">
        <header>
          <h2>Needs your attention</h2>
          @if (total() > 0) {
            <span class="total">{{ total() }} waiting</span>
          }
        </header>

        @if (items().length > 0) {
          <ul>
            @for (item of items(); track item.key) {
              <li>
                <a [routerLink]="item.path" (click)="open.set(false)">
                  <span class="glyph" aria-hidden="true"><kh-icon [name]="iconFor(item.icon)" size="sm" /></span>
                  <span class="text">
                    <span class="title">{{ item.label }}</span>
                    <span class="hint">{{ item.hint }}</span>
                  </span>
                  <span class="count">{{ item.count }}</span>
                </a>
              </li>
            }
          </ul>
        } @else {
          <p class="empty">Nothing is waiting for you. New work will show up here.</p>
        }

        @if (messageLogPath(); as path) {
          <footer>
            <a [routerLink]="path" (click)="open.set(false)">Open the message log</a>
          </footer>
        }
      </section>
    }
  `,
  styles: `
    :host {
      position: relative;
      display: inline-flex;
    }

    .bell {
      position: relative;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2.5rem;
      height: 2.5rem;
      border: 0;
      border-radius: var(--radius-md);
      background: none;
      color: var(--color-text-muted);
      cursor: pointer;
    }

    .bell:hover,
    .bell[aria-expanded='true'] {
      background: var(--color-surface);
      color: var(--color-text);
    }

    .dot {
      position: absolute;
      inset-block-start: var(--space-2);
      inset-inline-end: var(--space-2);
      width: 0.6rem;
      height: 0.6rem;
      border: 2px solid var(--color-surface-raised);
      border-radius: var(--radius-full);
      background: var(--color-danger);
    }

    /* On a phone the bell is nowhere near the right edge, so the panel is pinned to the screen
       rather than to the bell; from 480px it hangs from the bell as a normal dropdown. */
    .panel {
      position: fixed;
      inset-block-start: calc(var(--header-height) + var(--space-2));
      inset-inline: var(--space-4);
      z-index: var(--z-header);
      overflow: hidden;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-lg);
    }

    header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: var(--space-3) var(--space-4);
      border-block-end: 1px solid var(--color-border);
    }

    h2 {
      margin: 0;
      font-size: var(--text-sm);
    }

    .total {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    ul {
      max-height: 22rem;
      margin: 0;
      padding: 0;
      overflow-y: auto;
      list-style: none;
    }

    li + li {
      border-block-start: 1px solid var(--color-border);
    }

    li a {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      padding: var(--space-3) var(--space-4);
      color: inherit;
      text-decoration: none;
    }

    li a:hover {
      background: var(--color-surface);
    }

    .glyph {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      flex: none;
      width: 2rem;
      height: 2rem;
      border-radius: var(--radius-full);
      background: var(--color-warning-subtle);
      color: var(--color-warning-text);
    }

    .text {
      display: flex;
      flex: 1;
      flex-direction: column;
      min-width: 0;
    }

    .title {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .hint {
      overflow: hidden;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .count {
      min-width: 1.5rem;
      padding: 0 var(--space-2);
      border-radius: var(--radius-full);
      background: var(--color-warning-subtle);
      color: var(--color-warning-text);
      font-size: var(--text-xs);
      font-weight: var(--weight-semibold);
      line-height: 1.5rem;
      text-align: center;
    }

    .empty {
      margin: 0;
      padding: var(--space-6) var(--space-4);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
      text-align: center;
    }

    footer {
      padding: var(--space-3) var(--space-4);
      border-block-start: 1px solid var(--color-border);
      font-size: var(--text-sm);
    }

    @media (min-width: 480px) {
      .panel {
        position: absolute;
        inset-block-start: calc(100% + var(--space-2));
        inset-inline: auto 0;
        width: 24rem;
      }
    }
  `,
  host: {
    '(document:click)': 'onDocumentClick($event)',
    '(document:keydown.escape)': 'onEscape()',
  },
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminNotifications {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly items = input<readonly AdminAttentionItem[]>([]);
  /** Where the message log lives, when this session may read it; null hides the link. */
  readonly messageLogPath = input<string | null>(null);

  protected readonly open = signal(false);

  /** What is waiting, added up. A capped queue (`50+`) counts as its cap: the dot only needs "any". */
  protected readonly total = computed(() =>
    this.items().reduce(
      (sum, item) => sum + (typeof item.count === 'number' ? item.count : parseInt(item.count, 10) || 0),
      0,
    ),
  );

  protected readonly label = computed(() =>
    this.total() > 0 ? `Notifications, ${this.total()} waiting` : 'Notifications, nothing waiting',
  );

  constructor() {
    // A navigation by any other route (the palette, a shortcut) also closes the panel.
    inject(Router)
      .events.pipe(takeUntilDestroyed())
      .subscribe((event) => {
      if (event instanceof NavigationStart) this.open.set(false);
    });
  }

  protected iconFor(name: string | undefined): IconName {
    return name && (ICON_NAMES as readonly string[]).includes(name) ? (name as IconName) : 'alert';
  }

  protected onDocumentClick(event: Event): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) this.open.set(false);
  }

  protected onEscape(): void {
    if (!this.open()) return;
    this.open.set(false);
    this.host.nativeElement.querySelector<HTMLElement>('.bell')?.focus();
  }
}
