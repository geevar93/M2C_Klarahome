import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Badge, Button, Drawer, Icon } from '@klarahome/ui-primitives';

import { NavItem, isInternalHref } from './navigation.model';

/**
 * The mobile navigation drawer.
 *
 * Two levels and no further: a category tree rendered in full is a scroll a thumb cannot finish,
 * and the third level belongs on the category page where there is room to explain it. A parent
 * with children is a `<details>` — the browser gives it keyboard behaviour, an announced
 * expanded/collapsed state and a working page even before hydration, which no `@if` and a click
 * handler does.
 *
 * The drawer closes on any navigation: leaving it open over the page the user just chose is the
 * commonest mobile navigation bug there is.
 */
@Component({
  selector: 'kh-mobile-nav-drawer',
  imports: [Badge, Button, Drawer, Icon, RouterLink],
  template: `
    <kh-drawer [open]="open()" side="start" label="Menu" (closed)="closed.emit()">
      <div class="head">
        <span class="title">{{ storeName() }}</span>
        <button
          khButton
          variant="tertiary"
          [iconOnly]="true"
          type="button"
          aria-label="Close menu"
          (click)="closed.emit()"
        >
          <kh-icon name="close" />
        </button>
      </div>

      <nav aria-label="Main menu">
        <ul class="level-1">
          @for (item of menu(); track item.label) {
            <li>
              @if (item.children?.length) {
                <details>
                  <summary>
                    {{ item.label }}
                    <kh-icon name="chevron-down" size="sm" />
                  </summary>
                  <ul class="level-2">
                    @if (item.href) {
                      <li>
                        <a [routerLink]="item.href" (click)="closed.emit()">All {{ item.label }}</a>
                      </li>
                    }
                    @for (child of item.children; track child.label) {
                      <li>
                        @if (isInternal(child.href)) {
                          <a [routerLink]="child.href" (click)="closed.emit()">{{ child.label }}</a>
                        } @else if (child.href) {
                          <a [href]="child.href" rel="noopener">{{ child.label }}</a>
                        } @else {
                          <span class="heading">{{ child.label }}</span>
                        }
                      </li>
                    }
                  </ul>
                </details>
              } @else if (isInternal(item.href)) {
                <a [routerLink]="item.href" (click)="closed.emit()">
                  {{ item.label }}
                  @if (item.badge) {
                    <kh-badge tone="info">{{ item.badge }}</kh-badge>
                  }
                </a>
              } @else if (item.href) {
                <a [href]="item.href" rel="noopener">{{ item.label }}</a>
              } @else {
                <span class="heading">{{ item.label }}</span>
              }
            </li>
          } @empty {
            <li><span class="heading">Menu unavailable</span></li>
          }
        </ul>
      </nav>

      <div class="foot">
        <ng-content />
      </div>
    </kh-drawer>
  `,
  styles: `
    .head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
      padding: var(--space-2) var(--space-2) var(--space-2) var(--space-4);
      border-block-end: 1px solid var(--color-border);
    }

    .title {
      font-size: var(--text-lg);
      font-weight: var(--weight-bold);
    }

    ul {
      list-style: none;
      margin: 0;
      padding: 0;
    }

    a,
    summary,
    .heading {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
      min-height: var(--touch-target-min);
      padding: var(--space-2) var(--space-4);
      color: var(--color-text);
      text-decoration: none;
      font-size: var(--text-base);
    }

    summary {
      cursor: pointer;
      list-style: none;
    }

    summary::-webkit-details-marker {
      display: none;
    }

    details[open] summary kh-icon {
      transform: rotate(180deg);
    }

    .level-2 a,
    .level-2 .heading {
      padding-inline-start: var(--space-8);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .heading {
      font-weight: var(--weight-medium);
    }

    .foot {
      margin-block-start: auto;
      padding: var(--space-4);
      border-block-start: 1px solid var(--color-border);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MobileNavDrawer {
  readonly open = input(false);
  readonly storeName = input('Klara Home');
  readonly menu = input<readonly NavItem[]>([]);
  readonly closed = output<void>();

  protected readonly isInternal = isInternalHref;
}
