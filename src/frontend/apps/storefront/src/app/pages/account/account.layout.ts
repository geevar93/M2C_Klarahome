import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterNextRender,
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
  { path: '/account/notifications', label: 'Notifications', icon: 'bell' },
];

/**
 * The account area's frame — `/account` and everything under it.
 *
 * A layout route rather than a menu repeated on nine pages: the navigation, the greeting and the
 * sign-out control are the same everywhere inside the account, and nine copies of them is nine
 * chances for one to fall behind.
 *
 * On a phone it is a horizontal scroller above the content, and from `lg` a column beside it. Not a
 * drawer — the account menu is the primary navigation of this section, and putting it behind a tap
 * would make every move between orders and addresses two gestures instead of one.
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
          <strong>{{ profile.displayName() || 'your account' }}</strong>
        </p>

        <!-- The wrapper exists so the strip can carry a fade at its edges. A pseudo-element on the
             scroller itself scrolls away with the content; this one stays over the edge. -->
        <div class="strip">
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
        </div>

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

    /* An address is one long unbreakable token; without this it sets the panel's min-content width
       and, before the track floor above, took the whole page with it. */
    .greeting strong {
      font-size: var(--text-base);
      color: var(--color-text);
      overflow-wrap: anywhere;
    }

    .strip {
      position: relative;
      margin-block-end: var(--space-3);
    }

    /* The affordance that says the strip continues. It replaces the native scrollbar, which
       scrollbar-width: thin did not make thin on Windows — it rendered as a full-height grey bar
       across the menu and read as a rendering fault rather than as navigation. A gradient into the
       panel's own surface, so it works in every theme. */
    .strip::after {
      content: '';
      position: absolute;
      inset-block: 0;
      inset-inline-end: 0;
      inline-size: var(--space-6);
      pointer-events: none;
      background: linear-gradient(
        to right,
        transparent,
        var(--color-surface-raised)
      );
    }

    ul {
      display: flex;
      gap: var(--space-1);
      list-style: none;
      margin: 0;
      padding: 0;
      overflow-x: auto;
      /* Hidden rather than thin: the fade above is the affordance now. */
      scrollbar-width: none;
    }

    ul::-webkit-scrollbar {
      display: none;
    }

    @media (min-width: 1024px) {
      .strip::after {
        content: none;
      }

      ul {
        flex-direction: column;
        overflow: visible;
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
      box-shadow: inset 0 -2px 0 var(--color-accent);
    }

    @media (min-width: 1024px) {
      a.active {
        box-shadow: inset 2px 0 0 var(--color-accent);
      }
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
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  constructor() {
    this.profile.loadOnce();

    // The menu is a horizontal scroller on a phone (`ul` below `lg`), and the active section is not
    // always the first one in it — landing on `/account/wallet` from a link should not leave its
    // entry sitting off the right edge of the strip.
    afterNextRender(() => {
      this.host.nativeElement
        .querySelector('.menu a.active')
        ?.scrollIntoView({ inline: 'nearest', block: 'nearest' });
    });
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
