import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';

/**
 * A checkbox with its label, and the whole row as the target.
 *
 * A real `<input type="checkbox">` inside a `<label>`, so it is reachable by Tab, toggled by Space,
 * announced with its state, and hit anywhere along the row rather than on a 16px square — which is
 * the difference between a consent tick a thumb can hit and one it cannot
 * (docs/05-frontend-architecture.md §3.3).
 *
 * `checked` is a `model()`, so the parent binds `[(checked)]` and the component never holds a
 * state the parent disagrees with. There is no `indeterminate`: nothing in the storefront has a
 * tri-state tick, and an unused third state is a third thing to get wrong.
 */
@Component({
  selector: 'kh-checkbox',
  template: `
    <label class="kh-choice" [class.kh-choice--bare]="bare()">
      <input
        type="checkbox"
        [id]="inputId()"
        [checked]="checked()"
        [disabled]="disabled()"
        (change)="checked.set($any($event.target).checked)"
      />
      <span class="body">
        <span class="label">{{ label() }}</span>
        @if (description()) {
          <span class="description">{{ description() }}</span>
        }
      </span>
    </label>
  `,
  styles: `
    :host {
      display: block;
    }

    .body {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      min-width: 0;
    }

    .label {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .description {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Checkbox {
  readonly checked = model(false);
  readonly label = input.required<string>();
  readonly description = input<string | null>(null);
  readonly disabled = input(false);
  readonly inputId = input<string | undefined>(undefined);
  /** No card around it. For a consent tick under a form, where the box would be furniture. */
  readonly bare = input(false);
}
