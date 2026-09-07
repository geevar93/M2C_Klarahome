import { Directive, computed, input } from '@angular/core';

/**
 * The page gutter and the maximum line length, applied by an attribute rather than by a wrapper
 * element.
 *
 * A directive and not a component on purpose: a container that emitted its own element would add
 * a `<div>` between a landmark and its contents on every page, and `<main khContainer>` is both
 * fewer nodes and a truer description of what is happening — this *is* the main region, held to a
 * width.
 *
 * `narrow` exists for reading measures: a legal page or an order confirmation set to 1280px is
 * technically responsive and physically unreadable.
 */
@Directive({
  selector: '[khContainer]',
  host: {
    class: 'kh-container',
    '[style.max-width]': 'maxWidth()',
  },
})
export class Container {
  /**
   * `full` uses the layout maximum; `narrow` is the reading measure for prose; `flush` keeps the
   * gutter and drops the cap.
   *
   * A separately named input rather than a value on `khContainer` itself, so the common case is
   * the bare attribute — `<main khContainer>` — and not `khContainer="full"` on every page.
   */
  readonly size = input<'full' | 'narrow' | 'flush'>('full', { alias: 'khContainerSize' });

  protected readonly maxWidth = computed(() => {
    switch (this.size()) {
      case 'narrow':
        return '68ch';
      case 'flush':
        return 'none';
      default:
        return 'var(--container-max)';
    }
  });
}
