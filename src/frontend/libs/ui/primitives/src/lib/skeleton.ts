import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * A grey block the shape of the content that is coming.
 *
 * Skeletons, not spinners, for anything with a known shape
 * (docs/05-frontend-architecture.md §3.6). A spinner says "something is happening"; a skeleton
 * says "a product card is coming and it will be this tall", which is also what stops the page
 * from jumping when it arrives — the same layout shift the budget in §3.4 caps at 0.1.
 *
 * `aria-hidden` on purpose: the loading state is announced once, by the region that owns the
 * content, and not once per grey rectangle.
 */
@Component({
  selector: 'kh-skeleton',
  template: `
    @for (line of repeats(); track $index) {
      <span
        class="kh-skeleton"
        [style.width]="$last && lines() > 1 ? lastWidth() : width()"
        [style.height]="height()"
        [style.border-radius]="radius()"
      ></span>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
    }

    span {
      display: block;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'aria-hidden': 'true' },
})
export class Skeleton {
  readonly width = input('100%');
  readonly height = input('1rem');
  readonly radius = input('var(--radius-sm)');
  /** Number of blocks. More than one renders as lines of text. */
  readonly lines = input(1);
  /**
   * The last line is short, because the last line of a paragraph is. A block of identical bars is
   * read as a table by the eye, which is exactly the wrong expectation to set for prose.
   */
  readonly lastWidth = input('70%');

  protected readonly repeats = () => Array.from({ length: Math.max(1, this.lines()) });
}
