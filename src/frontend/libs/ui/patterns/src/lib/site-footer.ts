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
 */
@Component({
  selector: 'kh-site-footer',
  imports: [RouterLink],
  template: `
    <footer>
      <div class="inner">
        @for (group of menu(); track group.label) {
          <nav class="group" [attr.aria-label]="group.label">
            <h2 class="group-title">{{ group.label }}</h2>
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

      <p class="legal">© {{ year }} {{ storeName() }}. All rights reserved.</p>
    </footer>
  `,
  styles: `
    footer {
      border-block-start: 1px solid var(--color-border);
      background: var(--color-surface);
      padding: var(--space-8) var(--space-4) var(--space-6);
      /* Clears the sticky action bar on a phone, so the last link is reachable. */
      padding-block-end: calc(var(--bottom-bar-height) + var(--space-6));
    }

    .inner {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(12rem, 100%), 1fr));
      gap: var(--space-6);
      max-width: var(--container-max);
      margin-inline: auto;
    }

    .group-title {
      margin: 0 0 var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-bold);
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--color-text-muted);
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

    .legal {
      max-width: var(--container-max);
      margin: var(--space-8) auto 0;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    @media (min-width: 1024px) {
      footer {
        padding-block-end: var(--space-6);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SiteFooter {
  readonly storeName = input('Klara Home');
  /** Top-level items are column headings; their children are the links. */
  readonly menu = input<readonly NavItem[]>([]);

  protected readonly year = new Date().getFullYear();
  protected readonly isInternal = isInternalHref;
}
