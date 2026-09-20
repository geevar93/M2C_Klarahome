import { Directive, computed, input } from '@angular/core';

/**
 * `inverse` is the primary action's stand-in on a `kh-band--inverse` surface: the coffee primary is
 * 3.2:1 against the ink band, so a primary button there would take its own label below the floor.
 */
export type ButtonVariant = 'primary' | 'secondary' | 'tertiary' | 'danger' | 'inverse';
export type ButtonSize = 'sm' | 'md' | 'lg';

/**
 * The button appearance, applied to whichever element is semantically right.
 *
 * A directive rather than a component because the element carries meaning: something that acts is
 * a `<button>`, something that navigates is an `<a href>`, and a component that rendered one of
 * them would force the wrong element somewhere — usually a `<div role="button">`, which is a
 * keyboard trap with a hand cursor.
 *
 * `<summary>` is the third element that legitimately looks like a button: it is the control of a
 * `<details>` disclosure, and the browser already gives it the keyboard behaviour a hand-rolled
 * popup would have to reimplement. It is in the selector because the alternative — a component
 * copying these classes onto its own element — is how a control ends up *nearly* matching the
 * ones beside it, which is worse than not matching at all. The only thing a summary needs of its
 * own is `list-style: none`, to drop the disclosure triangle.
 *
 * The classes live in `styles/_button.scss` rather than here so they are declared once for the
 * whole document instead of once per component that uses a button.
 */
@Directive({
  selector: 'button[khButton], a[khButton], summary[khButton]',
  host: {
    class: 'kh-button',
    '[class]': 'classes()',
  },
})
export class Button {
  readonly variant = input<ButtonVariant>('secondary');
  readonly size = input<ButtonSize>('md');
  /** Full width. The default for a primary action on a phone. */
  readonly block = input(false);
  /** Square, for a control whose whole content is an icon. Requires `aria-label` on the element. */
  readonly iconOnly = input(false);

  protected readonly classes = computed(() => {
    const classes = ['kh-button', `kh-button--${this.variant()}`];
    if (this.size() !== 'md') classes.push(`kh-button--${this.size()}`);
    if (this.block()) classes.push('kh-button--block');
    if (this.iconOnly()) classes.push('kh-button--icon');
    return classes.join(' ');
  });
}
