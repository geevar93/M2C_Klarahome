import {
  ChangeDetectionStrategy,
  Component,
  Directive,
  ElementRef,
  booleanAttribute,
  computed,
  inject,
  input,
  output,
} from '@angular/core';

/**
 * A labelled form control, with its hint and its error message.
 *
 * The wrapper exists for one reason, and it is the reason most hand-written forms are inaccessible:
 * a control needs an `id` that its `<label for>` points at, and an `aria-describedby` naming both
 * the hint and the error — and those four strings have to agree. Written by hand at each of the
 * forty inputs in this application, they eventually will not.
 *
 * So the field owns the identifiers and the control reads them:
 *
 * ```html
 * <kh-field label="PIN code" [error]="form.fields.pincode.error()" hint="Six digits">
 *   <input khControl id="pincode" [value]="…" />
 * </kh-field>
 * ```
 *
 * The `id` is passed rather than generated so it is stable across renders — a generated one
 * changes on every hydration and breaks the label association a screen reader has already read.
 */
@Component({
  selector: 'kh-field',
  template: `
    <label [attr.for]="for()">
      {{ label() }}
      @if (optional()) {
        <span class="optional">(optional)</span>
      }
    </label>

    <ng-content />

    @if (hint() && !error()) {
      <p class="hint" [id]="for() + '-hint'">{{ hint() }}</p>
    }

    <!-- role="alert" so a message that appears after a submit is announced rather than merely
         drawn. It is rendered only when there is one: an always-present empty live region gets
         announced as a change the moment anything else on the page moves. -->
    @if (error()) {
      <p class="error" role="alert" [id]="for() + '-error'">{{ error() }}</p>
    }
  `,
  styles: `
    :host {
      display: block;
      margin-block-end: var(--space-4);
    }

    label {
      display: block;
      margin-block-end: var(--space-1);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .optional {
      margin-inline-start: var(--space-1);
      color: var(--color-text-muted);
      font-weight: var(--weight-regular);
    }

    .hint,
    .error {
      margin: var(--space-1) 0 0;
      font-size: var(--text-xs);
    }

    .hint {
      color: var(--color-text-muted);
    }

    .error {
      color: var(--color-danger);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Field {
  readonly label = input.required<string>();
  /** The control's `id`. The same string the control carries. */
  readonly for = input.required<string>();
  readonly hint = input<string | null>(null);
  readonly error = input<string | null>(null);
  /**
   * Marks the field optional rather than marking the others required.
   *
   * A form where nine of eleven fields carry a red asterisk has told the reader nothing. Marking
   * the exceptions is shorter and is what people actually scan for.
   */
  readonly optional = input(false);
}

/**
 * The appearance and the accessibility wiring for a text control.
 *
 * A directive, not a component, so the element stays an `<input>`, a `<textarea>` or a `<select>` —
 * which is what autofill, password managers and mobile keyboards key off. It derives
 * `aria-describedby` and `aria-invalid` from the field's own error, so the two can never disagree.
 *
 * `value` and `valueChange` are a plain two-way binding rather than a `ControlValueAccessor`: this
 * application has no `@angular/forms` in it (see `@klarahome/util`'s `forms.ts`), and a signal
 * holds the value.
 */
@Directive({
  selector: 'input[khControl], textarea[khControl], select[khControl]',
  host: {
    '[class]': 'classes()',
    '[attr.aria-invalid]': "invalid() ? 'true' : null",
    '[attr.aria-describedby]': 'describedBy()',
    '(blur)': 'touched.emit()',
  },
})
export class Control {
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);

  /**
   * True when the field this control sits in is showing an error.
   *
   * All three take `booleanAttribute`, so `khNumeric` on its own means true — which is how every
   * other boolean attribute in HTML behaves, and the alternative is `[khNumeric]="true"` on forty
   * inputs.
   */
  readonly invalid = input(false, { alias: 'khInvalid', transform: booleanAttribute });
  /** Tabular figures and wider tracking, for an amount, a PIN code or an OTP. */
  readonly numeric = input(false, { alias: 'khNumeric', transform: booleanAttribute });
  /** Whether the field has a hint to describe it. */
  readonly described = input(true, { alias: 'khDescribed', transform: booleanAttribute });

  /** Blurred. The field's `touched` flag is what turns a problem into a visible error. */
  readonly touched = output<void>();

  protected readonly classes = computed(() => {
    const classes = ['kh-control'];
    if (this.numeric()) classes.push('kh-control--numeric');
    // Read off the element rather than asked for: a textarea is taller and resizable because it is
    // a textarea, and making the caller declare that again is a chance to forget.
    if (this.element.nativeElement.tagName === 'TEXTAREA') classes.push('kh-control--textarea');
    return classes.join(' ');
  });

  /**
   * The hint and the error, by convention `{id}-hint` and `{id}-error`.
   *
   * Read off the element's own `id` rather than passed in again, so the control and the field
   * cannot be given different ones. Both ids are named even though the field renders one at a
   * time: `aria-describedby` tolerates an id that is not in the document, and the alternative is
   * this directive having to know which of the two the field decided to show.
   */
  protected readonly describedBy = computed(() => {
    if (!this.described()) return null;
    const id = this.element.nativeElement.id;
    return id ? `${id}-hint ${id}-error` : null;
  });
}
