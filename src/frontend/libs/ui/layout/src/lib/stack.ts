import { ChangeDetectionStrategy, Component, ViewEncapsulation, computed, input } from '@angular/core';

import { SpaceStep, spaceToken } from './spacing';

/**
 * One dimension of flex layout, with the gap taken from the spacing scale.
 *
 * It replaces the margin-on-the-child habit, which is where most responsive layouts go wrong: a
 * child that carries its own bottom margin cannot be reordered, cannot be the last item, and
 * needs a `:last-child` rule that some other component eventually contradicts. The parent owns
 * the space between its children — one rule, and it reverses cleanly at every breakpoint.
 */
@Component({
  selector: 'kh-stack',
  template: '<ng-content />',
  styles: `
    :host {
      display: flex;
      min-width: 0;
    }
  `,
  encapsulation: ViewEncapsulation.Emulated,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[style.flex-direction]': 'direction()',
    '[style.gap]': 'gapValue()',
    '[style.align-items]': 'align()',
    '[style.justify-content]': 'justify()',
    '[style.flex-wrap]': 'wrap() ? "wrap" : "nowrap"',
  },
})
export class Stack {
  readonly direction = input<'row' | 'column' | 'row-reverse' | 'column-reverse'>('column');
  readonly gap = input<SpaceStep>(4);
  readonly align = input<'stretch' | 'flex-start' | 'center' | 'flex-end' | 'baseline'>('stretch');
  readonly justify = input<'flex-start' | 'center' | 'flex-end' | 'space-between'>('flex-start');
  readonly wrap = input(false);

  protected readonly gapValue = computed(() => spaceToken(this.gap()));
}
