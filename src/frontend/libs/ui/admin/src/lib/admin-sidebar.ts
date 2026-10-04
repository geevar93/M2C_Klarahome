import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { ICON_NAMES, Icon, IconName } from '@klarahome/ui-primitives';

import { AdminNavGroup, AdminNavItem, AdminNavSection } from './admin.model';
import { currentPath, itemMatches } from './nav-location';

/**
 * The sidebar: headed groups of links, the way a merchant's other back offices lay them out.
 *
 * It filters nothing. The groups it is given are the groups it draws: the app has already dropped
 * every destination this session may not open, from the same declaration the route guards are
 * built from (`navigation.ts`).
 *
 * The first section in each group is a flat list, and any further sections fold away behind a
 * disclosure button (`aria-expanded`) until they are the one you are in. The section holding the
 * current page opens itself on navigation; a section you have opened or closed by hand stays as
 * you left it until the next navigation into it. A folded section still shows its summed count,
 * so "something is waiting in Inventory" is visible without opening it.
 *
 * The current page is marked with `aria-current`, a heavier weight and a leading rule as well as
 * a tint (WCAG 1.4.1: never colour alone). `navigated` fires on any link press so the shell can
 * close the drawer it sits in on a phone.
 */
@Component({
  selector: 'kh-admin-sidebar',
  imports: [Icon, RouterLink],
  template: `
    <nav aria-label="Main navigation">
      @for (group of groups(); track $index) {
        <div class="group">
          @if (group.label; as heading) {
            <p class="heading">{{ heading }}</p>
          }

          @for (section of group.sections; track section.label) {
            @if (section.collapsible) {
              <button
                type="button"
                class="fold"
                [attr.aria-expanded]="isOpen(section)"
                [attr.aria-controls]="'nav-' + slug(section.label)"
                (click)="toggle(section)"
              >
                <span class="fold-label">{{ section.label }}</span>
                @if (section.badge && !isOpen(section)) {
                  <span class="count">{{ section.badge }}</span>
                }
                <kh-icon [name]="isOpen(section) ? 'chevron-up' : 'chevron-down'" size="sm" />
              </button>
            }

            @if (!section.collapsible || isOpen(section)) {
              <ul [id]="'nav-' + slug(section.label)" [class.indent]="section.collapsible">
                @for (item of section.items; track item.path) {
                  <li>
                    <a
                      [routerLink]="item.path"
                      [class.active]="isActive(item)"
                      [attr.aria-current]="isActive(item) ? 'page' : null"
                      (click)="navigated.emit()"
                    >
                      <kh-icon [name]="iconFor(item.icon)" size="sm" />
                      <span class="label">{{ item.label }}</span>
                      @if (item.badge) {
                        <span class="count">{{ item.badge }}</span>
                      }
                    </a>
                  </li>
                }
              </ul>
            }
          }
        </div>
      }
    </nav>

    <a class="all" routerLink="/more" (click)="navigated.emit()">
      <kh-icon name="grid" size="sm" />
      <span>All screens</span>
    </a>
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
      min-height: 0;
    }

    /* The list takes what the footer leaves and scrolls inside it, so nothing can sit underneath the
       pinned "All screens" link: min-height 0 lets a flex child shrink below its content. */
    nav {
      flex: 1 1 0;
      min-height: 0;
      padding: var(--space-3) var(--space-3) var(--space-4);
      overflow-y: auto;
      scrollbar-width: thin;
    }

    .heading {
      margin: var(--space-5) 0 var(--space-1);
      padding-inline: var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-xs);
      font-weight: var(--weight-semibold);
      letter-spacing: 0.06em;
      text-transform: uppercase;
    }

    .group:first-child .heading {
      margin-block-start: 0;
    }

    ul {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    ul.indent {
      margin-block: var(--space-1);
      margin-inline-start: var(--space-4);
      padding-inline-start: var(--space-2);
      border-inline-start: 1px solid var(--color-border);
    }

    a,
    .fold {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      width: 100%;
      min-height: 2.5rem;
      padding: var(--space-2) var(--space-3);
      border: 0;
      border-radius: var(--radius-md);
      background: none;
      color: var(--color-text-subtle);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
      text-align: start;
      text-decoration: none;
      cursor: pointer;
    }

    a:hover,
    .fold:hover {
      background: var(--color-surface);
      color: var(--color-text);
    }

    a:focus-visible,
    .fold:focus-visible {
      outline: 2px solid var(--color-focus-ring);
      outline-offset: -2px;
    }

    a.active {
      background: var(--color-primary-subtle);
      color: var(--color-primary);
      font-weight: var(--weight-semibold);
      box-shadow: inset 3px 0 0 var(--color-primary);
    }

    .fold {
      gap: var(--space-2);
      color: var(--color-text-muted);
    }

    .fold-label,
    .label {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .count {
      padding: 0 var(--space-2);
      border-radius: var(--radius-full);
      background: var(--color-warning-subtle);
      color: var(--color-warning-text);
      font-size: var(--text-xs);
      font-weight: var(--weight-semibold);
      line-height: 1.25rem;
    }

    .all {
      flex: none;
      width: auto;
      margin: 0 var(--space-3) var(--space-3);
      background: var(--color-surface-raised);
      border-block-start: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminSidebar {
  readonly groups = input.required<readonly AdminNavGroup[]>();
  readonly navigated = output<void>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly path = currentPath();
  /** Sections the person has opened (true) or closed (false) by hand. Absent means "follow the route". */
  private readonly manual = signal<Readonly<Record<string, boolean>>>({});

  constructor() {
    // The current page must be visible in the list: after a navigation (or when the groups arrive)
    // the active link is scrolled into view, and only as far as it takes (`nearest`).
    effect((onCleanup) => {
      this.path();
      this.groups();
      const timer = setTimeout(() => {
        this.host.nativeElement.querySelector<HTMLElement>('a.active')?.scrollIntoView({ block: 'nearest' });
      });
      onCleanup(() => clearTimeout(timer));
    });

    // Navigating into a section hands control back to the route: it opens, whatever was done by hand.
    effect(() => {
      const path = this.path();
      const entered = this.groups()
        .flatMap((group) => group.sections)
        .filter((section) => section.collapsible && this.holdsCurrent(section, path))
        .map((section) => section.label);
      if (entered.length === 0) return;
      this.manual.update((current) => {
        const next = { ...current };
        for (const label of entered) delete next[label];
        return next;
      });
    });
  }

  protected isActive(item: AdminNavItem): boolean {
    return itemMatches(item, this.path());
  }

  protected isOpen(section: AdminNavSection): boolean {
    return this.manual()[section.label] ?? this.holdsCurrent(section, this.path());
  }

  protected toggle(section: AdminNavSection): void {
    const open = this.isOpen(section);
    this.manual.update((current) => ({ ...current, [section.label]: !open }));
  }

  protected slug(label: string): string {
    return label.toLowerCase().replace(/[^a-z0-9]+/g, '-');
  }

  protected iconFor(name: string | undefined): IconName {
    return name && (ICON_NAMES as readonly string[]).includes(name) ? (name as IconName) : 'grid';
  }

  private holdsCurrent(section: AdminNavSection, path: string): boolean {
    return section.items.some((item) => itemMatches(item, path));
  }
}
