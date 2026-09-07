import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Badge, ICON_NAMES, Icon, IconName } from '@klarahome/ui-primitives';

import { AdminNavSection } from './admin.model';

/**
 * The back office's primary navigation.
 *
 * **It filters nothing.** The sections it is given are the sections it draws — the permission
 * filtering happens once, in the app, against the same declaration the route guards are built
 * from, so the navigation and the guards cannot disagree. A sidebar that did its own filtering
 * would be a second copy of the rules, and the second copy is the one that goes stale.
 *
 * A section with no items is not rendered at all, so a vendor user does not see an empty
 * "Settlements" heading where the platform's items would be.
 *
 * Collapsed, it keeps the icons and hides the words — the width is what a tablet needs back, and
 * the labels stay reachable as `title` and as the accessible name, so the collapsed rail is not a
 * row of unlabelled glyphs to a screen reader.
 */
@Component({
  selector: 'kh-admin-sidebar',
  imports: [Badge, Icon, RouterLink, RouterLinkActive],
  template: `
    <nav [attr.aria-label]="label()">
      @for (section of sections(); track section.label) {
        @if (section.items.length > 0) {
          <div class="section">
            <h2 class="section-label" [class.kh-visually-hidden]="collapsed()">{{ section.label }}</h2>

            <ul>
              @for (item of section.items; track item.path) {
                <li>
                  <a
                    [routerLink]="item.path"
                    routerLinkActive="active"
                    [routerLinkActiveOptions]="{ exact: item.path === '/' }"
                    [attr.title]="collapsed() ? item.label : null"
                    [attr.aria-label]="collapsed() ? item.label : null"
                    (click)="navigated.emit()"
                  >
                    <kh-icon [name]="iconFor(item.icon)" size="sm" />
                    <span class="label" [class.kh-visually-hidden]="collapsed()">{{ item.label }}</span>

                    @if (item.badge) {
                      <kh-badge tone="primary">{{ item.badge }}</kh-badge>
                    }
                  </a>
                </li>
              }
            </ul>
          </div>
        }
      }
    </nav>
  `,
  styles: `
    :host {
      display: block;
      height: 100%;
      overflow-y: auto;
      padding: var(--space-3) var(--space-2);
      background: var(--color-surface);
    }

    .section + .section {
      margin-block-start: var(--space-5);
    }

    .section-label {
      margin: 0 0 var(--space-2);
      padding-inline: var(--space-2);
      font-size: var(--text-xs);
      font-weight: var(--weight-medium);
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--color-text-muted);
    }

    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    a {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      /* The floor, not a suggestion: a vendor doing dispatch is tapping this on a tablet. */
      min-height: var(--touch-target-min);
      padding: var(--space-2);
      border-radius: var(--radius-md);
      color: var(--color-text);
      text-decoration: none;
      font-size: var(--text-sm);
    }

    a:hover {
      background: var(--color-surface-raised);
    }

    /* The current page is marked by weight and a rule as well as by colour, because colour alone
       is not a distinction everyone can see (WCAG 1.4.1). */
    a.active {
      background: var(--color-primary-subtle);
      color: var(--color-primary);
      font-weight: var(--weight-medium);
      box-shadow: inset 2px 0 0 var(--color-primary);
    }

    .label {
      flex: 1;
      min-width: 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminSidebar {
  readonly sections = input.required<readonly AdminNavSection[]>();
  readonly collapsed = input(false);
  readonly label = input('Back office');

  /** A destination was chosen. The shell closes the drawer it is in, on a tablet. */
  readonly navigated = output<void>();

  /**
   * An item's icon, falling back to a neutral one.
   *
   * The icon set is a closed union (`IconName`) while a nav item's icon is a plain string, because
   * the navigation declaration is data. The name is therefore **checked against the registry**
   * rather than cast into it: `kh-icon` iterates the paths it finds, and a name that is not there
   * would take the whole navigation down over a missing glyph.
   */
  protected iconFor(name: string | undefined): IconName {
    return name && (ICON_NAMES as readonly string[]).includes(name) ? (name as IconName) : 'chevron-right';
  }
}
