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
 * the same vertical line as the content above it; the link columns are rendered only when there is
 * a menu, so a store that has not built one yet gets a one-line footer rather than an empty band.
 */
@Component({
  selector: 'kh-site-footer',
  imports: [Icon, RouterLink],
  template: `
    <footer [class.clears-sticky-bar]="clearsStickyBar()">
      <div class="kh-container">
        @if (menu().length > 0) {
          <div class="groups">
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

    footer {
      border-block-start: 1px solid var(--color-border);
      background: var(--color-surface);
      padding-block: var(--space-6);
    }

    /* Reserved only while a page has registered a sticky action, the same rule as the shell's
       \`main.has-sticky-action\`: an empty bar reserves nothing. */
    footer.clears-sticky-bar {
      padding-block-end: calc(var(--bottom-bar-height) + var(--space-6));
    }

    .groups {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(12rem, 100%), 1fr));
      gap: var(--space-6);
      margin-block-end: var(--space-6);
      padding-block-end: var(--space-6);
      border-block-end: 1px solid var(--color-border);
    }

    .group-title {
      margin: 0 0 var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-bold);
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--color-text-muted);
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
      color: var(--color-text);
      text-decoration: none;
      font-size: var(--text-sm);
    }

    li a:hover {
      text-decoration: underline;
    }

    /* The direction is restated: the link columns' \`ul\` rule above stacks its items, and this one
       row must not inherit that. */
    .social {
      display: flex;
      flex-direction: row;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin: 0 0 var(--space-4);
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
      border: 1px solid var(--color-border);
      border-radius: var(--radius-full);
      color: var(--color-text);
    }

    .social a:hover,
    .social a:focus-visible {
      background: var(--color-surface-muted);
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

    .legal,
    .powered {
      margin: 0;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .vendor {
      font-weight: var(--weight-medium);
      color: var(--color-text);
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
