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
  /**
   * The column count a merchant or a fixed layout asks for — **at the widest a container gets**,
   * not at every width. A literal `repeat(fixed, 1fr)` was the bug this input used to carry: a CMS
   * block editor can set this to anything, and a 4-column grid that stays 4 columns at 360px
   * renders four unreadable slivers, which is exactly the class of defect this component exists to
   * prevent for its *un*-fixed callers. `minColumnWidth` is still the floor, so the same "never
   * narrower than this" guarantee applies here too — the grid degrades to fewer, wider columns
   * under width pressure instead of holding the count and clipping.
   */
  readonly fixedColumns = input<number | null>(null);

  protected readonly gapValue = computed(() => spaceToken(this.gap()));

  protected readonly columns = computed(() => {
    const fixed = this.fixedColumns();
    const gap = this.gapValue();
    if (fixed && fixed > 0) {
      // The width one of `fixed` equal columns would have at the container's full size, floored by
      // `minColumnWidth` — so on a wide screen this resolves to exactly `fixed` columns (the
      // calc'd width wins), and on a narrow one the floor wins and `auto-fill` packs however many
      // of it actually fit, same as the uncapped path below. `auto-fill`, not `auto-fit`: the
      // latter would stretch a short last row's items to fill the leftover tracks, which turns "two
      // banners in a four-column block" into two banners each twice their intended width — the
      // trailing gap `auto-fill` leaves instead is the more legible failure of the two.
      const perColumn = `calc((100% - ${fixed - 1} * ${gap}) / ${fixed})`;
      return `repeat(auto-fill, minmax(min(max(${this.minColumnWidth()}, ${perColumn}), 100%), 1fr))`;
    }
    return `repeat(auto-fill, minmax(min(${this.minColumnWidth()}, 100%), 1fr))`;
  });
}
