import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon, IconName } from '@klarahome/ui-primitives';

import { NavItem, isInternalHref } from './navigation.model';

/** One profile link as the row draws it. */
interface SocialLink {
  readonly href: string;
  readonly label: string;
  readonly icon: IconName | null;
}

/**
 * The footer.
 *
 * On a storefront in India this is not decoration: the legal pages an intermediary must publish —
 * terms, privacy, returns, grievance officer contact — are reached from here and from nowhere
 * else (docs/07-security-compliance.md §5). The menu is CMS-driven so a compliance page can be
 * added without a deploy, and the copyright line is computed rather than typed so it does not
 * quietly say 2026 for ever.
 *
 * The content sits in the same `.kh-container` the page body uses, so the first column starts on
 * the same vertical line as the content above it. It degrades in layers rather than collapsing: the
 * brand column (name, tagline, follow row) and the two storefront link columns (browse, account)
 * are always there, because they point at routes that always exist; the CMS menu columns join them
 * when an editor has built a footer menu. A store with no menu therefore still gets a footer that
 * looks designed, and never an empty band or a dead link.
 */
@Component({
  selector: 'kh-site-footer',
  imports: [Icon, RouterLink],
  template: `
    <footer [class.clears-sticky-bar]="clearsStickyBar()">
      <div class="kh-container">
        <div class="groups">
          <div class="brand">
            <p class="brand-name">{{ storeName() }}</p>
            @if (tagline()) {
              <p class="brand-tagline">{{ tagline() }}</p>
            }

            @if (socialLinks().length > 0) {
              <ul class="social" [attr.aria-label]="socialLabel()">
                @for (link of socialLinks(); track link.href) {
                  <li>
                    <a [href]="link.href" target="_blank" rel="noopener" [attr.aria-label]="link.label">
                      @if (link.icon; as name) {
                        <kh-icon [name]="name" />
                      } @else {
                        <span class="social-text">{{ link.label }}</span>
                      }
                    </a>
                  </li>
                }
              </ul>
            }
          </div>

          <!-- Always valid: every address below is a route the storefront ships, so these columns
               never depend on the CMS and never link to a page that does not exist. -->
          <nav class="group" aria-label="Browse">
            <h2 class="group-title">Browse</h2>
            <ul>
              <li><a routerLink="/search">Search</a></li>
              <li><a routerLink="/cart">Cart</a></li>
            </ul>
          </nav>

          <nav class="group" aria-label="Your account">
            <h2 class="group-title">Your account</h2>
            <ul>
              @if (isAuthenticated()) {
                <li><a routerLink="/account">Account</a></li>
                <li><a routerLink="/account/orders">Orders</a></li>
                <li><a routerLink="/account/wishlist">Wishlist</a></li>
              } @else {
                <li><a routerLink="/auth/login">Sign in</a></li>
                <li><a routerLink="/auth/register">Create account</a></li>
              }
            </ul>
          </nav>

          @for (group of menu(); track group.label) {
            <nav class="group" [attr.aria-label]="group.label">
              <!-- A heading that points somewhere is a link too: the editor lets a footer item
                   carry a target at the top level, and a label that looks like a heading but
                   silently ignores it was an item nobody could click. -->
              <h2 class="group-title">
                @if (isInternal(group.href)) {
                  <a [routerLink]="group.href">{{ group.label }}</a>
                } @else if (group.href) {
                  <a
                    [href]="group.href"
                    [attr.target]="group.opensInNewTab ? '_blank' : null"
                    rel="noopener"
                    >{{ group.label }}</a
                  >
                } @else {
                  {{ group.label }}
                }
              </h2>
              <ul>
                @for (item of group.children ?? []; track item.label) {
                  <li>
                    @if (isInternal(item.href)) {
                      <a [routerLink]="item.href">{{ item.label }}</a>
                    } @else if (item.href) {
                      <a
                        [href]="item.href"
                        [attr.target]="item.opensInNewTab ? '_blank' : null"
                        rel="noopener"
                        >{{ item.label }}</a
                      >
                    } @else {
                      <span>{{ item.label }}</span>
                    }
                  </li>
                }
              </ul>
            </nav>
          }
        </div>

        <div class="bottom">
          <p class="legal">© {{ year }} {{ storeName() }}. All rights reserved.</p>
          @if (poweredBy(); as vendor) {
            <p class="powered">
              Powered by <span class="vendor">{{ vendor }}</span>
            </p>
          }
        </div>
      </div>
    </footer>
  `,
  host: { '[class.hidden-on-mobile]': 'hideOnMobile()' },
  styles: `
    /* Below 'lg' a page can opt out of the footer: on a product page the sticky buy bar is the end of
       the page, and a band of links scrolled up beneath it only pushes the purchase out of view. */
    :host(.hidden-on-mobile) {
      display: none;
    }

    @media (min-width: 1024px) {
      :host(.hidden-on-mobile) {
        display: block;
      }
    }

    /* The inverse surface, which is the theme's ink. A footer one shade off the page ground is a
       page that never ends; a dark one is a floor, and it is the single largest area of a
       storefront where a theme's darkest colour can be seen at full strength rather than as a
       hairline or a line of type. The inverse-surface role is exactly this, and it had no caller
       on the storefront before.

       Everything below therefore reads its colour from the inverse side of the token set: the
       hairlines from the inverse border, the focus ring from the inverse focus ring (the ink ring
       is invisible on an ink band), and the quiet type from a mix of the inverse text with the
       ground beneath it rather than from the muted-text role, which is a mid-tone chosen to read
       on a *light* surface and disappears here. */
    footer {
      background: var(--color-surface-inverse);
      color: var(--color-text-inverse);
      padding-block: var(--space-10) var(--space-8);
    }

    footer :where(a, button, input, select, textarea, summary, [tabindex]):focus-visible {
      outline-color: var(--color-focus-ring-inverse);
    }

    /* Reserved only while a page has registered a sticky action, the same rule as the shell's
       \`main.has-sticky-action\`: an empty bar reserves nothing. */
    footer.clears-sticky-bar {
      padding-block-end: calc(var(--bottom-bar-height) + var(--space-6));
    }

    /* One auto-fit grid for the whole band, so the number of columns follows the width and the
       number of groups rather than a breakpoint list that has to be re-tuned whenever an editor adds
       a CMS column. \`auto-fit\` collapses empty tracks, so a store with few groups does not leave a
       gap on the right; \`min(9.5rem, 100%)\` keeps a track from forcing overflow on a 320px phone.
       The brand takes the full first row on a phone and tablet, where it would otherwise squeeze a
       link column; from 1280px it is a wider first column (below) and the links fill the rest of the
       same row — five link columns at 1440 fit one row, none wraps on its own. */
    .groups {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(min(9.5rem, 100%), 1fr));
      gap: var(--space-8) var(--space-6);
      margin-block-end: var(--space-6);
      padding-block-end: var(--space-6);
      border-block-end: 1px solid var(--color-border-inverse);
    }

    /* Two tracks on a phone: an odd number of link columns would leave the last one alone in half a
       row, so it takes the whole row instead. The brand is the first child, so an odd link count
       makes the last child an even-numbered one. */
    .group:last-child:nth-child(even) {
      grid-column: 1 / -1;
    }

    /* From 'md' the track floor drops to 8rem so the five columns every store has (browse, account
       and three CMS menus) share one row at 768px — at 9.5rem four fit and the fifth wraps alone. */
    @media (min-width: 768px) {
      .groups {
        grid-template-columns: repeat(auto-fit, minmax(8rem, 1fr));
      }

      .group:last-child:nth-child(even) {
        grid-column: auto;
      }
    }

    .brand {
      grid-column: 1 / -1;
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      min-inline-size: 0;
    }

    /* From 1280px the grid is explicit: a brand track half as wide again as a link track, then link
       tracks that still auto-fit. A fixed first column is what stops the brand fighting the links
       for an auto-placed slot (it spanned two of the old tracks, and the sixth item wrapped). Not
       earlier: at 1024px the brand plus five link tracks does not fit one row, and the fifth would
       wrap alone, whereas the links alone (five tracks) do. */
    @media (min-width: 1280px) {
      .groups {
        grid-template-columns: minmax(14rem, 1.6fr) repeat(auto-fit, minmax(9.5rem, 1fr));
      }

      .brand {
        grid-column: auto;
        padding-inline-end: var(--space-6);
      }
    }

    .brand-name {
      margin: 0 0 var(--space-2);
      font-family: var(--font-display);
      font-size: var(--text-2xl);
      font-weight: var(--weight-display);
      letter-spacing: var(--tracking-display);
      line-height: var(--leading-tight);
      color: var(--color-text-inverse);
      overflow-wrap: anywhere;
    }

    .brand-tagline {
      max-inline-size: 28ch;
      margin: 0 0 var(--space-4);
      font-size: var(--text-sm);
      color: color-mix(in srgb, var(--color-text-inverse) 78%, var(--color-surface-inverse));
    }

    .group-title {
      margin: 0 0 var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-bold);
      text-transform: uppercase;
      letter-spacing: 0.04em;
      /* The accent, at the strength each theme checked against a light ground — which is also
         where it is legible on ink. A column heading is a label, not an action, so it takes the
         second hue rather than the primary, and it is the one coloured thing in the footer. */
      color: var(--color-accent-muted);
    }

    .group-title a {
      display: inline-flex;
      align-items: center;
      min-height: var(--touch-target-min);
      color: inherit;
      text-decoration: none;
    }

    .group-title a:hover {
      text-decoration: underline;
    }

    ul {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
    }

    li a,
    li span {
      display: inline-flex;
      align-items: center;
      min-height: var(--touch-target-min);
      color: var(--color-text-inverse);
      text-decoration: none;
      font-size: var(--text-sm);
    }

    li a:hover {
      text-decoration: underline;
    }

    li a:hover,
    li a:focus-visible {
      color: var(--color-text-inverse);
    }

    /* The direction is restated: the link columns' \`ul\` rule above stacks its items, and this one
       row must not inherit that. */
    .social {
      display: flex;
      flex-direction: row;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .social a {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      min-inline-size: var(--touch-target-min);
      min-block-size: var(--touch-target-min);
      padding-inline: var(--space-2);
      border: 1px solid var(--color-border-inverse);
      border-radius: var(--radius-full);
      color: var(--color-text-inverse);
      transition: background-color var(--duration-fast) var(--ease-standard);
    }

    /* A lift off the ink rather than a fill from the light side of the token set: the surface and
       muted roles are both light colours and would flash a white disc under the pointer. */
    .social a:hover,
    .social a:focus-visible {
      background: color-mix(in srgb, var(--color-text-inverse) 14%, transparent);
    }

    /* A network with no mark in the icon set is still a link, by name. */
    .social-text {
      font-size: var(--text-sm);
    }

    .bottom {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      align-items: center;
      gap: var(--space-2) var(--space-4);
    }

    /* Quieter than the links without dropping below 4.5:1: the inverse text mixed towards the ink
       it sits on, rather than the muted-text role, which is a mid-tone picked to read on a light
       ground and is close to invisible on this one. */
    .legal,
    .powered {
      margin: 0;
      color: color-mix(in srgb, var(--color-text-inverse) 75%, var(--color-surface-inverse));
      font-size: var(--text-xs);
    }

    .vendor {
      font-weight: var(--weight-medium);
      color: var(--color-text-inverse);
    }

    /* 1024px is the 'lg' breakpoint, where the sticky bar stops being fixed. */
    @media (min-width: 1024px) {
      footer.clears-sticky-bar {
        padding-block-end: var(--space-6);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SiteFooter {
  readonly storeName = input('Klara Home');
  /** The store's one-line description, from the branding settings. Empty hides it. */
  readonly tagline = input('');
  /** Which account column to draw: the signed-in shortcuts or the way in. */
  readonly isAuthenticated = input(false);
  /** Top-level items are column headings — links themselves when they carry a target — and their children are the links beneath. */
  readonly menu = input<readonly NavItem[]>([]);
  /**
   * The social profiles, as a row of icons above the legal line.
   *
   * The `social` menu, flattened: an editor may put the four links at the top level or under one
   * heading, and both mean the same thing here. The icon is chosen from the address, so adding a
   * network is adding a link in admin — there is no per-item icon field to keep in step.
   */
  readonly social = input<readonly NavItem[]>([]);

  /** The row's accessible name. The `social` menu's own heading, when it has one. */
  readonly socialLabel = input('Follow us');

  /** Whether the page has a sticky action bar the last line has to clear. */
  readonly clearsStickyBar = input(false);

  protected readonly socialLinks = computed<readonly SocialLink[]>(() =>
    flattenLinks(this.social()).map((item) => ({
      href: item.href ?? '',
      label: item.label,
      icon: socialIcon(item.href ?? ''),
    })),
  );
  /** Hides the footer below the 'lg' breakpoint; a wide screen always shows it. */
  readonly hideOnMobile = input(false);
  /** The platform vendor's credit. Empty hides the line. */
  readonly poweredBy = input('Stardust Technologies');

  protected readonly year = new Date().getFullYear();
  protected readonly isInternal = isInternalHref;
}

/** Every item with an address, headings flattened into their children. */
function flattenLinks(items: readonly NavItem[]): NavItem[] {
  return items.flatMap((item) => [...(item.href ? [item] : []), ...flattenLinks(item.children ?? [])]);
}

/**
 * The mark for a profile address.
 *
 * Matched on the host so that a link to a post, a handle or a regional domain still gets its icon,
 * and so that an editor adding a network only has to paste its URL. A host with no mark in the set
 * is not an error — the link is drawn with its label instead.
 */
function socialIcon(href: string): IconName | null {
  const host = hostOf(href);
  if (!host) return null;

  for (const [icon, domains] of SOCIAL_HOSTS) {
    if (domains.some((domain) => host === domain || host.endsWith(`.${domain}`))) return icon;
  }
  return null;
}

function hostOf(href: string): string | null {
  try {
    return new URL(href).hostname.replace(/^www\./, '').toLowerCase();
  } catch {
    return null;
  }
}

const SOCIAL_HOSTS: readonly (readonly [IconName, readonly string[]])[] = [
  ['instagram', ['instagram.com', 'instagr.am']],
  ['facebook', ['facebook.com', 'fb.com', 'fb.me']],
  ['x', ['x.com', 'twitter.com', 't.co']],
  ['twitch', ['twitch.tv']],
  ['youtube', ['youtube.com', 'youtu.be']],
];
