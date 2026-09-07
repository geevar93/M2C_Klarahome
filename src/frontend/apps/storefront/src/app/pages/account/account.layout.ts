import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
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
}

const LINKS: readonly AccountLink[] = [
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

        <ul>
          @for (link of links(); track link.path) {
            <li>
              <a
                [routerLink]="link.path"
                routerLinkActive="active"
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

    .layout {
      display: grid;
      gap: var(--space-6);
    }

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: 16rem minmax(0, 1fr);
        align-items: start;
      }
    }

    .greeting {
      display: flex;
      flex-direction: column;
      margin: 0 0 var(--space-3);
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .greeting strong {
      font-size: var(--text-base);
      color: var(--color-text);
    }

    ul {
      display: flex;
      gap: var(--space-1);
      list-style: none;
      margin: 0 0 var(--space-3);
      padding: 0 0 var(--space-2);
      overflow-x: auto;
      /* The menu scrolls sideways on a phone; the fade is left to Step 30 along with everything
         else that is decoration. */
      scrollbar-width: thin;
    }

    @media (min-width: 1024px) {
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

    a.active {
      background: var(--color-primary-subtle);
      color: var(--color-primary);
      font-weight: var(--weight-medium);
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
