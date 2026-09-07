import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Breadcrumb } from '@klarahome/util';
import { Icon } from '@klarahome/ui-primitives';

/**
 * The trail, as an ordered list inside a labelled `<nav>`.
 *
 * The list is the semantics — a screen reader announces "list of four items", which is how a user
 * knows how deep they are — and the separator is a decorative icon rather than a character in the
 * text, so the trail is not read as "Home slash Lighting slash Table lamps".
 *
 * On a phone it scrolls horizontally rather than wrapping to three lines above the content. The
 * items come from `BreadcrumbTrail`, which derives them from the router; this component renders
 * what it is given and knows nothing about routes.
 */
@Component({
  selector: 'kh-breadcrumbs',
  imports: [Icon, RouterLink],
  template: `
    @if (items().length > 1) {
      <nav aria-label="Breadcrumb">
        <ol>
          @for (crumb of items(); track crumb.label; let last = $last) {
            <li>
              @if (crumb.path && !last) {
                <a [routerLink]="crumb.path">{{ crumb.label }}</a>
                <kh-icon name="chevron-right" size="sm" aria-hidden="true" />
              } @else {
                <!-- The current page is marked, not linked: a link to where you already are is a
                     wasted tab stop, and 'aria-current' is what says which one it is. -->
                <span aria-current="page">{{ crumb.label }}</span>
              }
            </li>
          }
        </ol>
      </nav>
    }
  `,
  styles: `
    nav {
      overflow-x: auto;
      scrollbar-width: none;
    }

    nav::-webkit-scrollbar {
      display: none;
    }

    ol {
      display: flex;
      align-items: center;
      gap: var(--space-1);
      list-style: none;
      margin: 0;
      padding: var(--space-3) 0;
      white-space: nowrap;
    }

    li {
      display: flex;
      align-items: center;
      gap: var(--space-1);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    a {
      color: var(--color-text-muted);
      text-decoration: none;
    }

    a:hover {
      text-decoration: underline;
    }

    [aria-current='page'] {
      color: var(--color-text);
      font-weight: var(--weight-medium);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Breadcrumbs {
  readonly items = input<readonly Breadcrumb[]>([]);
}
