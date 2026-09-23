import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Badge, ICON_NAMES, Icon, IconName } from '@klarahome/ui-primitives';

import { AdminNavHub } from './admin.model';
import { currentPath, hubFor } from './nav-location';

/**
 * The five doors.
 *
 * One component, two shapes. On a phone it is the bar along the bottom of the screen — the thing
 * a thumb reaches without the hand moving, which is where every app a merchant already uses puts
 * its primary navigation. From the shell's breakpoint up it is a narrow rail down the left edge,
 * with the same five items in the same order. The *shell* positions the host; this component only
 * decides which way its items run, so the two shapes cannot drift apart in content.
 *
 * **It filters nothing.** The hubs it is given are the hubs it draws — the app has already
 * dropped any door with nothing reachable behind it, from the same declaration the route guards
 * are built from.
 *
 * A door is lit when any screen behind it is the current one, including detail routes that have
 * no tab of their own. `routerLinkActive` cannot express that, so `hubFor` does.
 */
@Component({
  selector: 'kh-admin-hub-bar',
  imports: [Badge, Icon, RouterLink],
  template: `
    <nav aria-label="Main">
      <ul>
        @for (hub of hubs(); track hub.key) {
          <li>
            <a
              [routerLink]="hub.path"
              [class.active]="hub.key === activeKey()"
              [attr.aria-current]="hub.key === activeKey() ? 'page' : null"
            >
              <span class="glyph">
                <kh-icon [name]="iconFor(hub.icon)" />
                @if (hub.badge) {
                  <kh-badge tone="danger" class="count">{{ hub.badge }}</kh-badge>
                  <span class="kh-visually-hidden">, {{ hub.badge }} waiting</span>
                }
              </span>
              <span class="label">{{ hub.label }}</span>
            </a>
          </li>
        }
      </ul>
    </nav>
  `,
  styles: `
    :host {
      display: block;
      background: var(--color-surface);
    }

    nav,
    ul {
      height: 100%;
    }

    ul {
      display: flex;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    li {
      flex: 1 1 0;
      min-width: 0;
    }

    a {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      align-items: center;
      justify-content: center;
      height: 100%;
      /* The floor, not a suggestion: this is tapped standing up in a warehouse. */
      min-height: var(--touch-target-min);
      padding: var(--space-1) var(--space-2);
      color: var(--color-text-muted);
      text-decoration: none;
      font-size: var(--text-xs);
      font-weight: var(--weight-medium);
      line-height: var(--leading-tight);
    }

    a:hover {
      color: var(--color-text);
    }

    a:focus-visible {
      outline: 2px solid var(--color-primary);
      outline-offset: -2px;
    }

    /* The open door is marked by weight and a rule as well as by colour (WCAG 1.4.1). On the bar
       the rule sits along the top edge, on the rail along the leading edge. */
    a.active {
      color: var(--color-primary);
      font-weight: var(--weight-bold);
      box-shadow: inset 0 2px 0 var(--color-primary);
    }

    .glyph {
      position: relative;
      display: inline-flex;
    }

    /* Sits on the icon's top-right shoulder, so the label beneath stays centred. */
    .count {
      position: absolute;
      inset-block-start: calc(-1 * var(--space-2));
      inset-inline-start: calc(100% - var(--space-2));
      white-space: nowrap;
    }

    .label {
      max-width: 100%;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    @media (min-width: 1024px) {
      ul {
        flex-direction: column;
        gap: var(--space-1);
        padding: var(--space-3) var(--space-2);
      }

      li {
        flex: none;
      }

      a {
        height: auto;
        padding: var(--space-2) var(--space-1);
        border-radius: var(--radius-md);
      }

      a:hover {
        background: var(--color-surface-raised);
      }

      a.active {
        background: var(--color-primary-subtle);
        box-shadow: inset 2px 0 0 var(--color-primary);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AdminHubBar {
  readonly hubs = input.required<readonly AdminNavHub[]>();

  private readonly path = currentPath();

  protected readonly activeKey = computed(() => hubFor(this.hubs(), this.path())?.key ?? null);

  /** An icon checked against the registry, because a hub's icon is data. See `AdminSubNav`. */
  protected iconFor(name: string | undefined): IconName {
    return name && (ICON_NAMES as readonly string[]).includes(name) ? (name as IconName) : 'grid';
  }
}
