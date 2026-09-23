import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Badge, Icon } from '@klarahome/ui-primitives';

import { AdminNavHub, AdminNavItem, AdminNavSection } from './admin.model';
import { currentPath, hubFor, itemMatches, sectionFor } from './nav-location';

/** One group of tabs in the row, with the heading the row shows before it (if any). */
interface SubNavGroup {
  readonly label: string | null;
  readonly items: readonly AdminNavItem[];
}

/**
 * The row of tabs behind the open door.
 *
 * Where the sidebar used to list everything at once, this lists only what is behind the hub the
 * person is in: the Orders door opens onto Orders, To pack, Shipments, Delivery problems and
 * Returns, and nothing about warehouses or tax rates is on the screen. It scrolls sideways on a
 * phone and wraps on a desktop, so it never hides an item and never grows a second row of
 * chrome above the page.
 *
 * Three rules decide what it shows:
 *
 *  - A hub with a single screen behind it (Home) draws no row at all: one tab is not a choice.
 *  - A hub with several groups draws them in order, each led by its heading in small muted text —
 *    "Organise", "Inventory" — so the row reads as three short lists rather than one long one.
 *  - `More` is the exception in both directions. On its landing page the row is hidden, because
 *    the page *is* the list; inside one of its groups the row shows that group only, with a
 *    "More" tab first as the way back. Twenty tabs is a directory, and the point was to stop
 *    being one.
 */
@Component({
  selector: 'kh-admin-sub-nav',
  imports: [Badge, Icon, RouterLink],
  template: `
    @if (visible()) {
      <nav [attr.aria-label]="ariaLabel()">
        <ul>
          @if (backTo(); as back) {
            <li>
              <a class="back" [routerLink]="back.path">
                <kh-icon name="chevron-left" size="sm" />
                <span>{{ back.label }}</span>
              </a>
            </li>
          }

          @for (group of groups(); track group.label ?? '') {
            @if (group.label) {
              <li class="heading" aria-hidden="true">{{ group.label }}</li>
            }
            @for (item of group.items; track item.path) {
              <li>
                <a
                  [routerLink]="item.path"
                  [class.active]="isActive(item)"
                  [attr.aria-current]="isActive(item) ? 'page' : null"
                >
                  <span>{{ item.label }}</span>
                  @if (item.badge) {
                    <kh-badge tone="primary">{{ item.badge }}</kh-badge>
                  }
                </a>
              </li>
            }
          }
        </ul>
      </nav>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    nav {
      /* Edge to edge on a phone so the first tab sits at the page's own gutter and the last one
         can be scrolled fully into view. */
      margin-inline: calc(-1 * var(--space-4));
      padding-inline: var(--space-4);
      overflow-x: auto;
      scrollbar-width: none;
      border-block-end: 1px solid var(--color-border);
    }

    nav::-webkit-scrollbar {
      display: none;
    }

    ul {
      display: flex;
      gap: var(--space-1);
      align-items: center;
      width: max-content;
      min-width: 100%;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .heading {
      padding-inline: var(--space-2) var(--space-1);
      font-size: var(--text-xs);
      font-weight: var(--weight-medium);
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--color-text-muted);
      white-space: nowrap;
    }

    /* A heading after the first group gets a hairline before it, so the row reads as lists. */
    li + .heading {
      margin-inline-start: var(--space-2);
      border-inline-start: 1px solid var(--color-border);
      padding-inline-start: var(--space-3);
    }

    a {
      display: inline-flex;
      gap: var(--space-1);
      align-items: center;
      min-height: var(--touch-target-min);
      padding: var(--space-2) var(--space-3);
      border-block-end: 2px solid transparent;
      color: var(--color-text-muted);
      text-decoration: none;
      font-size: var(--text-sm);
      white-space: nowrap;
    }

    a:hover {
      color: var(--color-text);
    }

    a:focus-visible {
      outline: 2px solid var(--color-primary);
      outline-offset: -2px;
      border-radius: var(--radius-sm);
    }

    /* The current tab is marked by weight and a rule as well as by colour (WCAG 1.4.1). */
    a.active {
      color: var(--color-primary);
      font-weight: var(--weight-medium);
      border-block-end-color: var(--color-primary);
    }

    .back {
      padding-inline-start: var(--space-1);
    }

    @media (min-width: 1024px) {
      nav {
        margin-inline: 0;
        padding-inline: 0;
        overflow-x: visible;
      }

      ul {
        flex-wrap: wrap;
        width: auto;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminSubNav {
  readonly hubs = input.required<readonly AdminNavHub[]>();

  /** The key of the hub whose row is a landing page rather than a row of tabs. */
  readonly landingHub = input('more');

  private readonly path = currentPath();

  private readonly hub = computed(() => hubFor(this.hubs(), this.path()));

  private readonly isLanding = computed(() => this.hub()?.key === this.landingHub());

  /** Inside a landing hub, only the current group is shown. Elsewhere, every group is. */
  private readonly shownSections = computed<readonly AdminNavSection[]>(() => {
    const hub = this.hub();
    if (!hub) return [];
    if (!this.isLanding()) return hub.sections;

    const section = sectionFor(hub.sections, this.path());
    return section ? [section] : [];
  });

  protected readonly groups = computed<readonly SubNavGroup[]>(() => {
    const sections = this.shownSections();
    // One group needs no heading: the door's own name already said what it is.
    const headed = sections.length > 1;
    return sections.map((section) => ({ label: headed ? section.label : null, items: section.items }));
  });

  protected readonly backTo = computed(() => {
    const hub = this.hub();
    return this.isLanding() && hub && this.shownSections().length > 0 ? { label: hub.label, path: hub.path } : null;
  });

  protected readonly visible = computed(() => {
    const count = this.groups().reduce((total, group) => total + group.items.length, 0);
    return this.backTo() !== null || count > 1;
  });

  protected readonly ariaLabel = computed(() => {
    const hub = this.hub();
    return hub ? `${hub.label} sections` : 'Sections';
  });

  protected isActive(item: AdminNavItem): boolean {
    return itemMatches(item, this.path());
  }
}
