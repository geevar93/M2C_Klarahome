import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { Chip } from '@klarahome/ui-primitives';

import { VariantAxisView, VariantView } from './catalog.model';

/**
 * Choosing a variant, one axis at a time.
 *
 * A product's variants arrive as a flat list, each carrying its options — `{colour: beige,
 * size: L}`. What a shopper needs is the transpose: the axes, their values, and which values are
 * still reachable given what is already chosen. `deriveAxes` below is that transpose, and it is
 * the one piece of real algorithm in this component, which is why it is a pure exported function
 * with a unit test rather than a method.
 *
 * **An unavailable value is shown, not hidden.** Removing "size L" because the beige one is sold
 * out leaves a shopper wondering whether the shop stocks it at all; showing it disabled tells them
 * it exists and this combination does not. That is also why availability is computed against the
 * *other* axes' selections and not against the whole selection — otherwise every value but the one
 * already chosen reads as unavailable.
 */

/** The axes and their values, given the variants and what is currently selected. */
export function deriveAxes(
  variants: readonly VariantView[],
  selectedId: string | null,
): readonly VariantAxisView[] {
  const selected = variants.find((variant) => variant.id === selectedId) ?? null;

  // Insertion order, which is the order the API sent the options in — the catalogue decides that
  // colour comes before size, and re-sorting here would contradict it.
  const axes = new Map<string, { name: string; values: Map<string, string> }>();

  for (const variant of variants) {
    for (const option of variant.options) {
      const axis = axes.get(option.code) ?? { name: option.name, values: new Map<string, string>() };
      axis.values.set(option.value, option.value);
      axes.set(option.code, axis);
    }
  }

  return [...axes.entries()].map(([code, axis]) => ({
    code,
    name: axis.name,
    values: [...axis.values.keys()].map((value) => ({
      value,
      label: value,
      selected: optionOf(selected, code) === value,
      // Reachable if some purchasable variant has this value on this axis *and* agrees with every
      // other axis already chosen.
      available: variants.some(
        (variant) =>
          variant.isPurchasable &&
          optionOf(variant, code) === value &&
          [...axes.keys()].every((other) => {
            if (other === code) return true;
            const chosen = optionOf(selected, other);
            return chosen === null || optionOf(variant, other) === chosen;
          }),
      ),
    })),
  }));
}

function optionOf(variant: VariantView | null, code: string): string | null {
  return variant?.options.find((option) => option.code === code)?.value ?? null;
}

/**
 * The variant that results from changing one axis to a value.
 *
 * Every other axis is kept if it can be — a shopper switching from beige to green expects to stay
 * on size L. When no variant matches the whole preserved selection, the first variant carrying the
 * newly chosen value wins: a near miss on the axis they just touched is what they asked for.
 */
export function resolveSelection(
  variants: readonly VariantView[],
  selectedId: string | null,
  code: string,
  value: string,
): VariantView | null {
  const selected = variants.find((variant) => variant.id === selectedId) ?? null;
  const candidates = variants.filter((variant) => optionOf(variant, code) === value);
  if (candidates.length === 0) return null;

  const preserved = candidates.find((variant) =>
    (selected?.options ?? []).every(
      (option) => option.code === code || optionOf(variant, option.code) === option.value,
    ),
  );

  return preserved ?? candidates.find((variant) => variant.isPurchasable) ?? candidates[0];
}

@Component({
  selector: 'kh-variant-selector',
  imports: [Chip],
  template: `
    @for (axis of axes(); track axis.code) {
      <fieldset>
        <legend>{{ axis.name }}</legend>
        <div class="values">
          @for (option of axis.values; track option.value) {
            <kh-chip
              [label]="option.label"
              [selected]="option.selected"
              [disabled]="!option.available && !option.selected"
              (toggled)="choose(axis.code, option.value)"
            />
          }
        </div>
      </fieldset>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    fieldset {
      margin: 0;
      padding: 0;
      border: 0;
    }

    legend {
      padding: 0 0 var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
      color: var(--color-text-muted);
    }

    .values {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VariantSelector {
  readonly variants = input.required<readonly VariantView[]>();
  readonly selectedId = input<string | null>(null);

  /** The variant that should now be shown. The page owns the selection and the URL. */
  readonly selected = output<VariantView>();

  protected readonly axes = computed(() => deriveAxes(this.variants(), this.selectedId()));

  protected choose(code: string, value: string): void {
    const next = resolveSelection(this.variants(), this.selectedId(), code, value);
    if (next && next.id !== this.selectedId()) this.selected.emit(next);
  }
}
