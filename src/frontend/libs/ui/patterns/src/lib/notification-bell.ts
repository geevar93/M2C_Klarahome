import { ChangeDetectionStrategy, Component, ElementRef, computed, input, output, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RelativeTimePipe } from '@klarahome/i18n';
import { Badge, Button, Icon } from '@klarahome/ui-primitives';

/**
 * One message as the bell shows it.
 *
 * A view model rather than the API's shape: this library knows nothing about the notifications
 * endpoint, and the storefront is free to map several sources onto one list.
 */
export interface NotificationItem {
  /** The message id, which is what a mark-read reports back. */
  readonly id: string;
  /** The headline. */
  readonly subject: string;
  /** The body, shown truncated in the dropdown and in full on the inbox page. */
  readonly body: string;
  /** When it was raised, as an ISO timestamp. */
  readonly createdAt: string;
  /** Whether the customer has already read it. */
  readonly isRead: boolean;
  /** Where reading it should take them, when there is somewhere useful. */
  readonly href?: string;
}

/**
 * The header bell: how many messages are waiting, and what the newest of them say.
 *
 * A `<details>` disclosure rather than a hand-rolled popup, the same choice the admin top bar
 * makes and for the same reason: a custom one would need roving focus, Escape handling and
 * outside-click dismissal written by hand to end up no better than what the platform already
 * gives. Escape closes it, the summary is a real button, and it works before hydration.
 *
 * **The badge is capped at 99+ and the panel holds five.** A number nobody can act on is
 * decoration, and a dropdown long enough to scroll is a page pretending to be a menu — which is
 * what the "See all" link at the foot exists for.
 *
 * Reading is what marking-read is hung on, not a separate control. A shopper who has opened the
 * panel and clicked a message has read it; asking them to also tick it off would be asking them to
 * do the application's bookkeeping.
 */
@Component({
  selector: 'kh-notification-bell',
  imports: [Badge, Button, Icon, RelativeTimePipe, RouterLink],
  template: `
    <details #disclosure class="bell" (toggle)="onToggle($event)">
      <!-- The button primitive, not a copy of it: this sits in a row with the wishlist, account
           and cart icons, and anything less than the same directive is a control that *nearly*
           matches its neighbours. -->
      <summary khButton variant="tertiary" [iconOnly]="true" [attr.aria-label]="label()">
        <kh-icon name="bell" />
        @if (unreadCount() > 0) {
          <!-- 'aria-hidden': the count is already in the summary's accessible name, and a badge
               read separately turns "Notifications, 3 unread" into "…, 3 unread, 3". -->
          <kh-badge tone="primary" class="count" aria-hidden="true">{{ badge() }}</kh-badge>
        }
      </summary>

      <!-- The way out. A <details> does not close when you tap away from it, and on a touch screen
           there is no Escape key either - without this the sheet can only be dismissed by finding
           the bell again behind it. Transparent on a pointer device, where it is doing nothing but
           catching the click that dismisses a menu; a scrim on a phone, where the sheet really is
           a layer over the page. -->
      <button type="button" class="scrim" tabindex="-1" aria-label="Close notifications" (click)="close()"></button>

      <div class="panel" role="group" [attr.aria-label]="label()">
        <div class="panel-head">
          <h2>Notifications</h2>
          @if (unreadCount() > 0) {
            <button type="button" class="mark-all" (click)="markAllRead.emit()">Mark all as read</button>
          }
          <!-- Phones only: the scrim above it is the other way out, but a sheet with no visible
               close is a sheet somebody swipes at and then reloads the page to escape. -->
          <button type="button" class="close" aria-label="Close notifications" (click)="close()">
            <kh-icon name="close" />
          </button>
        </div>

        @if (loading() && items().length === 0) {
          <p class="state">Loading…</p>
        } @else if (items().length === 0) {
          <p class="state">Nothing yet. Updates about your orders will appear here.</p>
        } @else {
          <ul class="list">
            @for (item of items(); track item.id) {
              <li [class.unread]="!item.isRead">
                <a [routerLink]="item.href ?? inboxLink()" (click)="opened.emit(item.id)">
                  <!-- The dot is the only thing distinguishing unread, so it is labelled rather
                       than left as a colour a screen reader cannot see. -->
                  @if (!item.isRead) {
                    <span class="dot" aria-hidden="true"></span>
                    <span class="sr-only">Unread.</span>
                  }
                  <span class="subject">{{ item.subject }}</span>
                  <span class="body">{{ item.body }}</span>
                  <time class="when" [attr.datetime]="item.createdAt">{{ item.createdAt | khRelativeTime }}</time>
                </a>
              </li>
            }
          </ul>
        }

        <a class="see-all" [routerLink]="inboxLink()" (click)="close()">See all notifications</a>
      </div>
    </details>
  `,
  styles: `
    :host {
      display: contents;
    }

    /* 'display: contents' on the host makes *this* the flex item in the header's action row, so it
       has to behave like the buttons beside it: an inline-flex box that is exactly its summary,
       rather than a block that happens to contain one. Anything else and the icon sits a pixel or
       two off the line its neighbours are on. */
    .bell {
      position: relative;
      display: inline-flex;
    }

    summary {
      /* The badge is positioned against the trigger, exactly as the cart's is against its own
         button. Everything else about the box comes from .kh-button. */
      position: relative;
    }

    /* The cart's badge geometry, to the token: these two sit next to each other and a count
       tucked into a different corner of an identically sized icon reads as a mistake. */
    .count {
      position: absolute;
      inset-block-start: var(--space-1);
      inset-inline-end: calc(var(--space-1) * -1);
      min-width: var(--space-4);
      min-block-size: var(--space-4);
      padding: 0 var(--space-1);
      font-size: var(--text-xs);
      box-shadow: none;
    }

    .scrim {
      position: fixed;
      inset: 0;
      z-index: var(--z-drawer);
      /* Derived from a token rather than written as a colour, the same rule the drawer's scrim
         follows: no component may hold a hex value. */
      background: color-mix(in srgb, var(--color-text) 45%, transparent);
      border: 0;
      cursor: default;
    }

    /* Mobile first, and this is a sheet rather than a dropdown on purpose. A 22rem card hanging
       off the bell on a 360px screen covers the search box, leaves a sliver of page down one side,
       and puts its scrolling list under the one part of the screen a thumb cannot reach. Anchored
       to the bottom edge it is full width, reachable, and reads as the layer it is. */
    .panel {
      position: fixed;
      inset-inline: 0;
      inset-block-end: 0;
      z-index: var(--z-drawer);
      display: flex;
      flex-direction: column;
      max-block-size: 85vh;
      /* The home-indicator strip on a modern phone. Without it the last row of a sheet flush to
         the bottom edge sits under the gesture bar. */
      padding-block-end: env(safe-area-inset-bottom, 0);
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-start-start-radius: var(--radius-lg);
      border-start-end-radius: var(--radius-lg);
      box-shadow: var(--shadow-lg);
      animation: kh-bell-sheet-in var(--duration-base) var(--ease-standard);
    }

    @keyframes kh-bell-sheet-in {
      from {
        transform: translateY(100%);
      }
    }

    /* 480px is the header's own first breakpoint - the one where the wishlist icon appears - so
       the panel changes shape exactly when the row it hangs from does. */
    @media (min-width: 480px) {
      .scrim {
        /* Still there, still the click-away target; it has no business tinting a desktop page. */
        background: transparent;
      }

      .panel {
        position: absolute;
        inset-block-start: calc(100% + var(--space-2));
        inset-block-end: auto;
        /* Anchored to the end edge: the bell sits in the actions cluster at the end of the bar,
           and a panel aligned to its start edge would hang off the side of the window. */
        inset-inline-start: auto;
        inset-inline-end: 0;
        width: 22rem;
        max-block-size: none;
        padding-block-end: 0;
        border-radius: var(--radius-md);
        animation: none;
      }
    }

    .panel-head {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      justify-content: space-between;
      padding: var(--space-3);
      border-block-end: 1px solid var(--color-border);
    }

    .panel-head h2 {
      margin: 0;
      margin-inline-end: auto;
      font-size: var(--text-sm);
      font-weight: var(--weight-bold);
    }

    .close {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      inline-size: var(--touch-target-min);
      block-size: var(--touch-target-min);
      /* Pulled into the padding so the head is not made taller by a control that is only a hit
         area; the icon still lines up with the heading. */
      margin-block: calc(var(--space-3) * -1);
      margin-inline-end: calc(var(--space-2) * -1);
      border: 0;
      background: none;
      color: var(--color-text-muted);
      cursor: pointer;
    }

    @media (min-width: 480px) {
      .close {
        display: none;
      }
    }

    .mark-all {
      min-height: var(--touch-target-min);
      padding: 0;
      border: 0;
      background: none;
      color: var(--color-link);
      font-size: var(--text-xs);
      cursor: pointer;
    }

    .panel-head,
    .see-all {
      flex: 0 0 auto;
    }

    .state {
      margin: 0;
      padding: var(--space-4) var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .list {
      /* No cap on the phone: the sheet's own 85vh bounds it, and a second limit inside it would
         leave the list scrolling within a panel that was not full. The desktop cap is below. */
      margin: 0;
      padding: 0;
      overflow-y: auto;
      /* Stops the page behind scrolling on when the list reaches its end - the thing that makes a
         scrollable panel on a phone feel broken. */
      overscroll-behavior: contain;
      list-style: none;
    }

    @media (min-width: 480px) {
      .list {
        max-height: 24rem;
      }
    }

    .list li + li {
      border-block-start: 1px solid var(--color-border);
    }

    .list a {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: 0 var(--space-2);
      min-height: var(--touch-target-min);
      padding: var(--space-3);
      color: inherit;
      text-decoration: none;
    }

    .list a:hover,
    .list a:focus-visible {
      background: var(--color-surface-sunken, var(--color-bg));
    }

    .dot {
      grid-row: 1 / span 3;
      align-self: start;
      inline-size: 0.5rem;
      block-size: 0.5rem;
      /* Nudged down to sit on the subject's first line rather than above it. */
      margin-block-start: var(--space-1);
      border-radius: 50%;
      background: var(--color-primary);
    }

    /* The unread row carries its weight on the subject rather than on a background tint: a tinted
       row and a hover state are the same gesture, and one cancels the other out. */
    .unread .subject {
      font-weight: var(--weight-bold);
    }

    .subject,
    .body,
    .when {
      grid-column: 2;
    }

    .subject {
      font-size: var(--text-sm);
    }

    .body {
      display: -webkit-box;
      -webkit-box-orient: vertical;
      -webkit-line-clamp: 2;
      overflow: hidden;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .when {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .see-all {
      display: block;
      min-height: var(--touch-target-min);
      padding: var(--space-3);
      border-block-start: 1px solid var(--color-border);
      color: var(--color-link);
      font-size: var(--text-sm);
      text-align: center;
    }

    .sr-only {
      position: absolute;
      clip-path: inset(50%);
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      white-space: nowrap;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '(keydown.escape)': 'close()',
  },
})
export class NotificationBell {
  /** The newest messages, already trimmed to what the panel should hold. */
  readonly items = input<readonly NotificationItem[]>([]);

  /** How many are unread in total, which is not the same as how many are in `items`. */
  readonly unreadCount = input(0);

  /** Whether the first load is still in flight. */
  readonly loading = input(false);

  /** Where "See all" goes. */
  readonly inboxLink = input('/account/inbox');

  /** The panel was opened, so the shell can fetch what it holds. */
  readonly panelOpened = output<void>();

  /** A message was clicked; the id is what should be marked read. */
  readonly opened = output<string>();

  /** The "Mark all as read" button was pressed. */
  readonly markAllRead = output<void>();

  private readonly disclosure = viewChild.required<ElementRef<HTMLDetailsElement>>('disclosure');

  /** `99+` past the point where the exact number stops being actionable. */
  protected readonly badge = computed(() => (this.unreadCount() > 99 ? '99+' : `${this.unreadCount()}`));

  protected readonly label = computed(() => {
    const count = this.unreadCount();
    return count === 0 ? 'Notifications' : `Notifications, ${count} unread`;
  });

  /** Fetches on open rather than on every page view, so a shopper who never looks never pays. */
  protected onToggle(event: Event): void {
    if ((event.target as HTMLDetailsElement).open) this.panelOpened.emit();
  }

  /**
   * Closes the panel.
   *
   * Held as a view reference rather than queried from the event, so that Escape closes it from
   * anywhere inside — including from a link the browser has already moved focus to.
   */
  protected close(): void {
    this.disclosure().nativeElement.open = false;
  }
}
