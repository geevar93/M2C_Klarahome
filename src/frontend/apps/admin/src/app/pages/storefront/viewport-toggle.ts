import { ChangeDetectionStrategy, Component, model } from '@angular/core';
import { Button } from '@klarahome/ui-primitives';

import { CanvasViewport } from './storefront-canvas';

/** Desktop or phone, as a pair of toggle buttons. Which one is on is said in words, not colour. */
@Component({
  selector: 'kh-viewport-toggle',
  imports: [Button],
  template: `
    <div class="group" role="group" aria-label="Preview width">
      @for (option of options; track option.value) {
        <button
          khButton
          type="button"
          size="sm"
          [variant]="value() === option.value ? 'primary' : 'secondary'"
          [attr.aria-pressed]="value() === option.value"
          (click)="value.set(option.value)"
        >
          {{ option.label }}
        </button>
      }
    </div>
  `,
  styles: `
    .group {
      display: inline-flex;
      gap: var(--space-1);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ViewportToggle {
  readonly value = model<CanvasViewport>('desktop');

  protected readonly options: readonly { readonly value: CanvasViewport; readonly label: string }[] = [
    { value: 'desktop', label: 'Desktop' },
    { value: 'phone', label: 'Phone' },
  ];
}
