import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { Checkbox, Control, Field } from '@klarahome/ui-primitives';

/**
 * One field of a served schema, as this control renders it.
 *
 * The CMS block types' own shape, restated here because a component in `ui-admin` may not import
 * the generated client — the boundary that keeps this library renderable without an API. The caller
 * maps; the names match so the mapping is a cast in practice.
 */
export interface SchemaFieldView {
  readonly name: string;
  /** `Text`, `RichText`, `Html`, `Integer`, `Boolean`, `MediaRef`, `ProductRef`, and so on. */
  readonly kind: string;
  readonly isRequired: boolean;
  /** Whether the value is a list, edited as one value per line. */
  readonly isList: boolean;
  /** The longest a text value may be, or 0 for no limit. */
  readonly maxLength: number;
  /** The words accepted, for a choice field. */
  readonly choices?: readonly string[] | null;
}

/**
 * The control for one schema-declared field.
 *
 * **Extracted at Step 28B so the repeater could exist** (deliverable 16). A block's own fields and
 * the fields of its repeated children are the same problem one level apart: `BlockTypeResponse`
 * declares `fields` and `itemFields` with the same shape, and the composer rendered the first as
 * controls and the second as a JSON textarea. Writing the controls twice would have been two
 * renderings of one schema, and the second would have been the one nobody updated.
 *
 * It renders the value it is given and emits the value it produces, and holds nothing. The caller
 * owns the draft, which is what lets one component serve a block's config and an item's alike.
 */
@Component({
  selector: 'kh-schema-field',
  imports: [Checkbox, Control, Field],
  template: `
    @if (field().kind === 'Boolean') {
      <kh-checkbox
        [label]="label()"
        [inputId]="controlId()"
        [checked]="value() === true"
        (checkedChange)="changed.emit($event)"
      />
    } @else {
      <kh-field [label]="label()" [for]="controlId()" [optional]="!field().isRequired" [hint]="hint()">
        @if (choices(); as options) {
          <select
            khControl
            [id]="controlId()"
            [value]="asText()"
            (change)="emitText($any($event.target).value)"
          >
            <option value=""></option>
            @for (choice of options; track choice) {
              <option [value]="choice">{{ choice }}</option>
            }
          </select>
        } @else if (field().kind === 'Integer') {
          <input
            khControl
            [id]="controlId()"
            type="number"
            [value]="asText()"
            (input)="emitNumber($any($event.target).value)"
          />
        } @else if (isLongText()) {
          <textarea
            khControl
            [id]="controlId()"
            rows="3"
            [value]="asText()"
            (input)="emitText($any($event.target).value)"
          ></textarea>
        } @else {
          <input
            khControl
            [id]="controlId()"
            type="text"
            [attr.maxlength]="field().maxLength > 0 ? field().maxLength : null"
            [value]="asText()"
            (input)="emitText($any($event.target).value)"
          />
        }
      </kh-field>
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SchemaField {
  readonly field = input.required<SchemaFieldView>();

  /** A unique id for the control, so its label points at it. */
  readonly controlId = input.required<string>();

  /** The current value, as the draft holds it. */
  readonly value = input<unknown>(null);

  /** The label, and the hint beneath it. Both are the caller's, because wording is the app's. */
  readonly label = input.required<string>();
  readonly hint = input('');

  /** The new value: a string, a number, a boolean, a list of strings, or null for empty. */
  readonly changed = output<unknown>();

  protected readonly choices = computed(() => {
    const options = this.field().choices;
    return options && options.length > 0 ? options : null;
  });

  /**
   * Whether this needs a textarea.
   *
   * A list is one value per line, which is a textarea; so is rich text, HTML, and anything with a
   * generous limit. The threshold is arbitrary and the alternative — a schema flag for it — would
   * be the server describing a widget rather than a value.
   */
  protected readonly isLongText = computed(() => {
    const field = this.field();
    return field.isList || field.kind === 'RichText' || field.kind === 'Html' || field.maxLength > 400;
  });

  /** The value as the editor shows it. A list becomes one entry per line. */
  protected readonly asText = computed(() => {
    const value = this.value();

    if (Array.isArray(value)) return value.map((entry) => String(entry)).join('\n');
    if (value === null || value === undefined) return '';

    return String(value);
  });

  protected emitText(text: string): void {
    if (this.field().isList) {
      const lines = text
        .split('\n')
        .map((line) => line.trim())
        .filter((line) => line.length > 0);

      this.changed.emit(lines.length > 0 ? lines : null);
      return;
    }

    this.changed.emit(text === '' ? null : text);
  }

  protected emitNumber(text: string): void {
    const parsed = Number(text);
    this.changed.emit(text === '' || !Number.isFinite(parsed) ? null : parsed);
  }
}
