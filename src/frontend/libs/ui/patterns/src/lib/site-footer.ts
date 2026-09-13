import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

import { NavItem, isInternalHref } from './navigation.model';

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
  imports: [RouterLink],
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

        <div class="bottom">
          <p class="legal">© {{ year }} {{ storeName() }}. All rights reserved.</p>
          @if (poweredBy(); as vendor) {
            <p class="powered">Powered by <span class="vendor">{{ vendor }}</span></p>
          }
        </div>
      </div>
    </footer>
  `,
  styles: `
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
  /** Whether the page has a sticky action bar the last line has to clear. */
  readonly clearsStickyBar = input(false);
  /** The platform vendor's credit. Empty hides the line. */
  readonly poweredBy = input('Stardust Technologies');

  protected readonly year = new Date().getFullYear();
  protected readonly isInternal = isInternalHref;
}
