import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ThemePreset } from '@klarahome/util';

/**
 * The store's colour theme, chosen from the presets.
 *
 * One card per preset: a strip of its five defining colours, its name and a sentence about it,
 * behind a native radio so the group is announced as a group and the arrow keys walk it. The
 * colours are shown, not named — an operator choosing a theme is choosing a feeling, and "#2f5d46"
 * tells them nothing that the swatch does not.
 *
 * A token set that matches no preset — hand-edited, or from before presets existed — selects
 * nothing, and the card list says so rather than pretending the nearest one is in use.
 */
@Component({
  selector: 'kh-theme-picker',
  template: `
    <fieldset>
      <legend>{{ legend() }}</legend>

      @if (selected() === null) {
        <p class="custom">
          This store uses a custom set of colour tokens. Choosing a theme below replaces them.
        </p>
      }

      <div class="grid">
        @for (preset of presets(); track preset.id) {
          <label class="card" [class.current]="preset.id === selected()">
            <input
              type="radio"
              name="theme-preset"
              [value]="preset.id"
              [checked]="preset.id === selected()"
              [disabled]="disabled()"
              (change)="chosen.emit(preset.id)"
            />
            <span class="strip" aria-hidden="true">
              @for (colour of preset.swatches; track $index) {
                <span class="swatch" [style.background]="colour"></span>
              }
            </span>
            <span class="name">{{ preset.name }}</span>
            <span class="description">{{ preset.description }}</span>
          </label>
        }
      </div>
    </fieldset>
  `,
  styles: `
    :host {
      display: block;
    }

    fieldset {
      margin: 0;
      padding: 0;
      border: 0;
    }

    legend {
      padding: 0;
      margin-block-end: var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .custom {
      margin: 0 0 var(--space-3);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .grid {
      display: grid;
      gap: var(--space-3);
    }

    @media (min-width: 768px) {
      .grid {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }

    @media (min-width: 1280px) {
      .grid {
        grid-template-columns: repeat(3, minmax(0, 1fr));
      }
    }

    .card {
      position: relative;
      display: grid;
      gap: var(--space-2);
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-sm);
      cursor: pointer;
    }

    .card:hover {
      border-color: var(--color-border-strong);
    }

    .card.current {
      border-color: var(--color-primary);
      box-shadow: 0 0 0 1px var(--color-primary), var(--shadow-sm);
    }

    .card:has(input:focus-visible) {
      outline: 2px solid var(--color-focus-ring);
      outline-offset: 2px;
    }

    .card input {
      position: absolute;
      inset-block-start: var(--space-3);
      inset-inline-end: var(--space-3);
      margin: 0;
    }

    .strip {
      display: flex;
      block-size: var(--space-8);
      overflow: hidden;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-sm);
    }

    .swatch {
      flex: 1;
    }

    .name {
      font-weight: var(--weight-medium);
      padding-inline-end: var(--space-6);
    }

    .description {
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .card:has(input:disabled) {
      cursor: default;
      opacity: 0.7;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ThemePicker {
  readonly presets = input.required<readonly ThemePreset[]>();
  /** The id of the preset in use, or null for a custom token set. */
  readonly selected = input<string | null>(null);
  readonly legend = input('Colour theme');
  readonly disabled = input(false);

  readonly chosen = output<string>();
}
