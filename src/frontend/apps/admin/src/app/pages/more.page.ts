import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionStore } from '@klarahome/data-access-auth';
import { PageHeader } from '@klarahome/ui-admin';
import { EmptyState, ICON_NAMES, Icon, IconName } from '@klarahome/ui-primitives';

import { navCategories } from '../core/navigation';

/**
 * Every screen this account can open, in one place: the page behind "All screens" in the sidebar.
 *
 * The sidebar lists only categories and the tabs above a page only the open category's screens;
 * this page is the whole list, grouped under the same category names, with each section's screens
 * as full-width rows a thumb can hit. It renders from `navCategories()`, the declaration the route
 * guards are built from, so it cannot list a screen the session cannot open.
 */
@Component({
  selector: 'kh-more-page',
  imports: [EmptyState, Icon, PageHeader, RouterLink],
  template: `
    <kh-page-header heading="All screens" description="Everything your account can open, grouped by category." />

    @if (categories().length > 0) {
      <div class="groups">
        @for (category of categories(); track category.key) {
          <section class="card">
            <h2>{{ category.label }}</h2>
            @for (section of category.sections; track section.label) {
              @if (category.sections.length > 1) {
                <h3>{{ section.label }}</h3>
              }
              <ul>
                @for (item of section.items; track item.path) {
                  <li>
                    <a [routerLink]="item.path">
                      <span class="glyph" aria-hidden="true"><kh-icon [name]="iconFor(item.icon)" size="sm" /></span>
                      <span class="label">{{ item.label }}</span>
                      <kh-icon name="chevron-right" size="sm" class="chevron" />
                    </a>
                  </li>
                }
              </ul>
            }
          </section>
        }
      </div>
    } @else {
      <kh-empty-state
        heading="Nothing to show"
        message="Your account cannot open any screen yet. Ask whoever administers the platform to assign a role."
      />
    }
  `,
  styles: `
    .groups {
      display: grid;
      gap: var(--space-4);
    }

    .card {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-sm);
    }

    h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-xs);
      font-weight: var(--weight-semibold);
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--color-text-muted);
    }

    h3 {
      margin: var(--space-3) 0 var(--space-1);
      font-size: var(--text-sm);
      font-weight: var(--weight-semibold);
    }

    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    a {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      min-height: var(--touch-target-min);
      padding: var(--space-2) var(--space-2);
      border-radius: var(--radius-md);
      color: var(--color-text);
      text-decoration: none;
      font-size: var(--text-sm);
    }

    a:hover {
      background: var(--color-surface);
    }

    .glyph {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 2rem;
      height: 2rem;
      border-radius: var(--radius-md);
      background: var(--color-primary-subtle);
      color: var(--color-primary);
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

  protected readonly categories = computed(() => navCategories(this.session.session(), new Map()));

  protected iconFor(name: string | undefined): IconName {
    return name && (ICON_NAMES as readonly string[]).includes(name) ? (name as IconName) : 'chevron-right';
  }
}
