import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
} from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ProfileStore } from '@klarahome/data-access-account';
import { Button, Icon, IconName } from '@klarahome/ui-primitives';
import { FeatureFlags } from '@klarahome/util';

import { SignInFlow } from '../../core/sign-in.flow';

/** One entry of the account menu. The flag, where there is one, is what hides an unbuilt feature. */
interface AccountLink {
  readonly path: string;
  readonly label: string;
  readonly icon: IconName;
  readonly flag?: string;
  readonly exact?: boolean;
}

const LINKS: readonly AccountLink[] = [
  { path: '/account', label: 'Overview', icon: 'home', exact: true },
  { path: '/account/orders', label: 'Orders', icon: 'package' },
  { path: '/account/returns', label: 'Returns', icon: 'refresh' },
  { path: '/account/wishlist', label: 'Wishlist', icon: 'heart' },
  { path: '/account/addresses', label: 'Addresses', icon: 'pin' },
  { path: '/account/profile', label: 'Profile', icon: 'user' },
  { path: '/account/wallet', label: 'Store credit', icon: 'wallet', flag: 'pricing.wallet' },
  { path: '/account/inbox', label: 'Notifications', icon: 'bell' },
  { path: '/account/notifications', label: 'Notification settings', icon: 'edit' },
];

/**
 * The account area's frame — `/account` and everything under it.
 *
 * A layout route rather than a menu repeated on nine pages: the navigation, the greeting and the
 * sign-out control are the same everywhere inside the account, and nine copies of them is nine
 * chances for one to fall behind.
 *
 * A stacked list above the content on a phone, and from `lg` the same list as a column beside it.
 * Not a drawer — the account menu is the primary navigation of this section, and putting it behind
 * a tap would make every move between orders and addresses two gestures instead of one. Not a
 * sideways scroller either, which is what it was: eight entries never fitted one phone-width row,
 * so half the menu sat off-screen behind a swipe nobody could see was available.
 *
 * `routerLinkActive` with `aria-current="page"` rather than a colour alone: which section you are in
 * has to be available to somebody who cannot see the highlight.
 */
@Component({
  selector: 'kh-account-layout',
  imports: [Button, Icon, RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <div class="layout">
      <nav class="menu" aria-label="Your account">
        <p class="greeting">
          <span>Signed in as</span>
          <strong [title]="profile.displayName()">{{ profile.displayName() || 'your account' }}</strong>
        </p>

        <ul>
          @for (link of links(); track link.path) {
            <li>
              <a
                [routerLink]="link.path"
                routerLinkActive="active"
                [routerLinkActiveOptions]="{ exact: !!link.exact }"
                #active="routerLinkActive"
                [attr.aria-current]="active.isActive ? 'page' : null"
              >
                <kh-icon [name]="link.icon" size="sm" />
                {{ link.label }}
              </a>
            </li>
          }
        </ul>

        <button khButton variant="tertiary" type="button" (click)="signOut()">Sign out</button>
      </nav>

      <div class="content">
        <router-outlet />
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
      padding-block: var(--space-4) var(--space-10);
    }

    /* minmax(0, 1fr), not the implicit auto track a bare single-column grid gets.

       An auto track is sized from its items' content, and a grid item's min-width defaults to
       auto — so the track grew to the menu's max-content width, which is all eight links laid
       out in one unwrapped row. On a 356px phone that made the column 624px inside a 324px
       container: the whole page scrolled sideways, and the empty state's heading and its button
       were cut off at the right edge. The overflow-x: auto on the strip could not save it,
       because the strip was being handed a track already wider than the screen.

       The lg template below always had the floor on its content column; the phone one never had
       a template at all. */
    .layout {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      gap: var(--space-6);
    }

    /* Belt and braces with the track floor above: a grid item that may hold something unbreakable
       (a long email in the greeting, a wide table in the content) must be allowed below its
       content's width or it pushes the track open again. */
    .menu,
    .content {
      min-inline-size: 0;
    }

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: 16rem minmax(0, 1fr);
        align-items: start;
      }
    }

    /* The card treatment the rest of the product uses (.kh-card, _base.scss). The menu was the
       one piece of primary navigation sitting as bare text on the page ground, which is why the
       account area read as a different, unfinished product on a phone — a greeting, a row of
       links and a button, none of them on anything. */
    .menu {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-sm);
    }

    .greeting {
      display: flex;
      flex-direction: column;
      margin: 0 0 var(--space-3);
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    /* One line, clipped, rather than broken mid-token. An email address is a single unbreakable
       word, and letting it break anywhere put the last character of the domain alone on a second
       line — in a 16rem sidebar, founder@klarahome.localhos / t. The full value stays in the DOM,
       so a screen reader still reads all of it and the title attribute shows it on hover; only the
       painting is shortened. */
    .greeting strong {
      font-size: var(--text-base);
      color: var(--color-text);
      min-inline-size: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    /* Stacked, never a sideways scroller.

       It used to be a horizontal strip on a phone, on the reasoning that the account menu should
       stay one gesture away. In practice eight items never fit: the row was cut off mid-item at
       whatever the screen ran out at, the entries past the edge were invisible until you thought
       to swipe a strip that does not look swipeable, and every attempt to signal the overflow —
       a native scrollbar, then a fade — was a decoration apologising for a layout that did not
       fit. A menu you can see all of needs no affordance.

       auto-fit rather than a breakpoint: one column when the card is narrow, two only when there
       is room for two 12rem cells, decided by the space actually available rather than by a guess
       about the device. 12rem is chosen so that every phone gets the single stacked column — a
       390px screen leaves 324px inside the card, short of the 388px two cells would need — and a
       tablet, where a column of eight full-width rows would be a waste of the width, gets two. The
       lg rule below pins it back to one column, because there it is a 16rem sidebar and two
       columns in it would be a pair of stubs. */
    ul {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(12rem, 1fr));
      gap: var(--space-1);
      list-style: none;
      margin: 0 0 var(--space-2);
      padding: 0;
    }

    @media (min-width: 1024px) {
      ul {
        grid-template-columns: minmax(0, 1fr);
      }
    }

    a {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      min-block-size: var(--touch-target-min);
      padding-inline: var(--space-3);
      border-radius: var(--radius-md);
      color: var(--color-text);
      text-decoration: none;
      white-space: nowrap;
      font-size: var(--text-sm);
    }

    a:hover {
      background: var(--color-surface);
    }

    /* The accent marks where you are, the same job it does under a section heading and on the
       header's active menu item. Never the only signal — the fill, the weight and aria-current
       all say it too. An inset shadow rather than a border so the pill does not change size when
       it becomes active and shove its neighbours along the strip. */
    a.active {
      background: var(--color-primary-subtle);
      color: var(--color-primary);
      font-weight: var(--weight-medium);
      box-shadow: inset 2px 0 0 var(--color-accent);
    }

    /* Separated from the links: signing out is not a ninth place to go. */
    .menu > button {
      margin-block-start: var(--space-2);
      border-block-start: 1px solid var(--color-border-subtle);
      padding-block-start: var(--space-2);
      inline-size: 100%;
      justify-content: flex-start;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountLayout {
  protected readonly profile = inject(ProfileStore);
  private readonly flags = inject(FeatureFlags);
  private readonly flow = inject(SignInFlow);

  constructor() {
    this.profile.loadOnce();
  }

  /**
   * The menu, with anything its deployment has switched off removed.
   *
   * A link to a page that answers "this is not available here" is worse than no link: the customer
   * cannot tell whether they are missing something. It is a `computed` over the flag set rather
   * than a method, so an operator flipping a flag reaches an open tab on the next `/store/config`.
   */
  protected readonly links = computed<readonly AccountLink[]>(() => {
    const flags = this.flags.all();
    return LINKS.filter((link) => !link.flag || flags[link.flag] === true);
  });

  protected signOut(): void {
    this.flow.signOut();
  }
}
