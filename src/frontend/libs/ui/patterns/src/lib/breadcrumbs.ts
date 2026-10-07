import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  afterRenderEffect,
  input,
  viewChild,
} from '@angular/core';
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
 * On a phone it scrolls horizontally rather than wrapping to three lines above the content, starts
 * scrolled to the end (the current page is the part a shopper wants to read, and clipping it to
 * "Jaipur Blo" was the one thing the row must never do), and fades at both edges so a clipped
 * crumb reads as "more this way" rather than as a rendering fault. The items come from `BreadcrumbTrail`, which derives them from the router; this component renders
 * what it is given and knows nothing about routes.
 */
@Component({
  selector: 'kh-breadcrumbs',
  imports: [Icon, RouterLink],
  template: `
    @if (items().length > 1) {
      <nav #trail aria-label="Breadcrumb">
        <ol>
          <!-- Tracked by position, not label: ancestors arrive after hydration and are inserted
               before the leaf, and keyed moves over server-rendered nodes left them after it. -->
          @for (crumb of items(); track $index; let last = $last) {
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
      /* The container's own gutter is given to the nav and taken back as padding, so at rest the
         trail sits where it always did while the fade zone lives in the gutter, not over a label. */
      margin-inline: calc(-1 * var(--space-4));
      padding-inline: var(--space-4);
      /* Alpha only: the colour is irrelevant to a mask, so a token stands in for "opaque". */
      -webkit-mask-image: linear-gradient(
        to right,
        transparent,
        var(--color-bg) var(--space-4),
        var(--color-bg) calc(100% - var(--space-4)),
        transparent
      );
      mask-image: linear-gradient(
        to right,
        transparent,
        var(--color-bg) var(--space-4),
        var(--color-bg) calc(100% - var(--space-4)),
        transparent
      );
    }

    @media (min-width: 768px) {
      nav {
        margin-inline: 0;
        padding-inline: 0;
        -webkit-mask-image: none;
        mask-image: none;
      }
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

    /* The row scrolls, so an item never shrinks: squeezed, the chevron was drawn over the next label. */
    li {
      flex-shrink: 0;
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

    a {
      transition: color var(--duration-fast) var(--ease-standard);
    }

    a:hover {
      color: var(--color-text);
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

  private readonly trail = viewChild<ElementRef<HTMLElement>>('trail');

  constructor() {
    // After every render in which the trail changed, show its end. A browser-only hook: it never
    // runs during SSR, and on a trail that fits scrollWidth equals the width, so nothing moves.
    afterRenderEffect(() => {
      this.items();
      const element = this.trail()?.nativeElement;
      if (element) element.scrollLeft = element.scrollWidth;
    });
  }
}
