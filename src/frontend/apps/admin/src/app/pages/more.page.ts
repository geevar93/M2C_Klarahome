import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionStore } from '@klarahome/data-access-auth';
import { PageHeader } from '@klarahome/ui-admin';
import { EmptyState, ICON_NAMES, Icon, IconName } from '@klarahome/ui-primitives';

import { visibleHubs } from '../core/navigation';

/**
 * The landing page behind the `More` door.
 *
 * Every other hub opens straight onto its first screen with a row of tabs above it, because four
 * or five tabs is a choice a person can make at a glance. `More` holds everything that is set up
 * once or looked at occasionally — the marketplace's commercial side, settings, the platform's
 * own diagnostics — and for a platform admin that is twenty screens. A row of twenty tabs is the
 * sidebar again, laid sideways. So this hub opens onto a page: its groups as headed lists, each
 * link a full-width row a thumb can hit, in the same order the old sidebar had them, with System
 * last for the same reason it was last there.
 *
 * It renders from `visibleHubs()`, the same declaration the guards are built from, so it cannot
 * list a screen the session cannot open. A session with nothing behind this door never gets here
 * — the hub bar has already dropped the door — but a direct visit still gets an honest page.
 */
@Component({
  selector: 'kh-more-page',
  imports: [EmptyState, Icon, PageHeader, RouterLink],
  template: `
    <kh-page-header heading="More" description="Sellers, money, settings and the platform's own tools." />

    @if (sections().length > 0) {
      <div class="groups">
        @for (section of sections(); track section.label) {
          <section>
            <h2>{{ section.label }}</h2>
            <ul>
              @for (item of section.items; track item.path) {
                <li>
                  <a [routerLink]="item.path">
                    <kh-icon [name]="iconFor(item.icon)" size="sm" />
                    <span class="label">{{ item.label }}</span>
                    <kh-icon name="chevron-right" size="sm" class="chevron" />
                  </a>
                </li>
              }
            </ul>
          </section>
        }
      </div>
    } @else {
      <kh-empty-state
        heading="Nothing else to show"
        message="Everything your account can reach is already in the tabs along the bottom."
      />
    }
  `,
  styles: `
    .groups {
      display: grid;
      gap: var(--space-6);
    }

    h2 {
      margin: 0 0 var(--space-2);
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
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface);
      overflow: hidden;
    }

    li + li {
      border-block-start: 1px solid var(--color-border);
    }

    a {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      min-height: var(--touch-target-min);
      padding: var(--space-3) var(--space-4);
      color: var(--color-text);
      text-decoration: none;
      font-size: var(--text-sm);
    }

    a:hover {
      background: var(--color-surface-raised);
    }

    .label {
      flex: 1;
      min-width: 0;
    }

    .chevron {
      color: var(--color-text-muted);
    }

    @media (min-width: 768px) {
      .groups {
        grid-template-columns: repeat(auto-fill, minmax(20rem, 1fr));
        align-items: start;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MorePage {
  private readonly session = inject(SessionStore);

  protected readonly sections = computed(
    () => visibleHubs(this.session.session()).find((hub) => hub.key === 'more')?.sections ?? [],
  );

  protected iconFor(name: string | undefined): IconName {
    return name && (ICON_NAMES as readonly string[]).includes(name) ? (name as IconName) : 'chevron-right';
  }
}
