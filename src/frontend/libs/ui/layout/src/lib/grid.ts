import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { SpaceStep, spaceToken } from './spacing';

/**
 * A grid that decides its own column count from the space available.
 *
 * `repeat(auto-fill, minmax(min(<min>, 100%), 1fr))` rather than a set of media queries, because
 * the number of product cards that fit is a function of the container and not of the viewport —
 * the same grid is four across on a desktop page and two across inside a drawer, with no rule
 * written for either. The inner `min(...)` is what stops a 240px minimum from overflowing a
 * 360px screen with a gutter on each side, which is the standard bug in this pattern.
 */
@Component({
  selector: 'kh-grid',
  template: '<ng-content />',
  styles: `
    :host {
      display: grid;
      min-width: 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[style.grid-template-columns]': 'columns()',
    '[style.gap]': 'gapValue()',
  },
})
export class Grid {
  /** The narrowest a column may be before the grid drops one. A CSS length. */
  readonly minColumnWidth = input('16rem');
  readonly gap = input<SpaceStep>(4);
  /** Fixes the column count instead of computing it. For a layout that must not reflow. */
  readonly fixedColumns = input<number | null>(null);

  protected readonly gapValue = computed(() => spaceToken(this.gap()));

  protected readonly columns = computed(() => {
    const fixed = this.fixedColumns();
    if (fixed && fixed > 0) return `repeat(${fixed}, minmax(0, 1fr))`;
    return `repeat(auto-fill, minmax(min(${this.minColumnWidth()}, 100%), 1fr))`;
  });
}
