import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CanDeactivateFn } from '@angular/router';
import { Alert, Button } from '@klarahome/ui-primitives';

/**
 * A screen that can refuse to be left.
 *
 * Implemented by any component `unsavedChangesGuard` protects. It is one method rather than a
 * dirty flag so that a screen with three forms can answer for all of them.
 */
export interface HasUnsavedChanges {
  hasUnsavedChanges(): boolean;
}

/**
 * Stops a half-finished form being lost to a stray click on the sidebar.
 *
 * The back office is a place where somebody fills in forty fields of a product, clicks a
 * navigation item by accident, and loses the lot. `confirm()` is used deliberately in preference
 * to a styled dialog: this has to work during a **browser-initiated** navigation too, and there
 * the only thing the platform will show is the browser's own prompt — so using the same mechanism
 * for both keeps one behaviour rather than two that disagree.
 *
 * As with every guard here, it decides what is rendered and not what is allowed.
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) => {
  if (!component?.hasUnsavedChanges?.()) return true;
  return confirm('You have unsaved changes on this page. Leave without saving?');
};

/**
 * The frame every admin form sits in.
 *
 * The forms themselves differ completely — a product has ninety fields, a role has three — but
 * what surrounds them does not, and getting that wrong is what makes a back office frustrating:
 *
 *  - **The error summary is above the form and is a real list**, so a submit that failed says what
 *    is wrong in one place. A form that reports its eleven problems only next to eleven fields,
 *    three screens apart, is a form the user scrolls hunting for red.
 *  - **The actions are pinned to the bottom** and stay reachable without scrolling to the end of a
 *    long form.
 *  - **Save is disabled while saving, and says so** — a second submit on a slow connection is how
 *    duplicate records are made.
 *  - **The unsaved marker is visible**, so "did that save?" is answerable without guessing.
 *
 * Autosave for drafts (`docs/05-frontend-architecture.md` §4.3) is deliberately not here: what
 * counts as a draft is per-entity — a CMS page has a draft revision, a role does not — so it
 * belongs to the screens that have one rather than to a frame that would have to guess.
 */
@Component({
  selector: 'kh-form-shell',
  imports: [Alert, Button],
  template: `
    <form (submit)="onSubmit($event)" novalidate>
      <header>
        <div class="titles">
          <h2>{{ heading() }}</h2>
          @if (description(); as text) {
            <p class="description">{{ text }}</p>
          }
        </div>

        @if (dirty()) {
          <p class="dirty" role="status">Unsaved changes</p>
        }
      </header>

      @if (summary().length > 0) {
        <!-- role="alert" so a submit that failed is announced rather than merely drawn: the
             fields it refers to may be below the fold. -->
        <kh-alert tone="danger" heading="This could not be saved">
          <ul>
            @for (message of summary(); track message) {
              <li>{{ message }}</li>
            }
          </ul>
        </kh-alert>
      }

      <div class="body">
        <ng-content />
      </div>

      <footer>
        <ng-content select="[slot=actions]" />

        @if (showDefaultActions()) {
          <button khButton type="button" variant="tertiary" [disabled]="saving()" (click)="cancelled.emit()">
            {{ cancelLabel() }}
          </button>
          <button khButton type="submit" variant="primary" [disabled]="saving()">
            {{ saving() ? 'Saving…' : submitLabel() }}
          </button>
        }
      </footer>
    </form>
  `,
  styles: `
    :host {
      display: block;
    }

    form {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    header {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
      align-items: baseline;
      justify-content: space-between;
    }

    h2 {
      margin: 0;
      font-size: var(--text-xl);
    }

    .description {
      margin: var(--space-1) 0 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .dirty {
      margin: 0;
      font-size: var(--text-xs);
      color: var(--color-warning);
    }

    ul {
      margin: 0;
      padding-inline-start: var(--space-5);
    }

    footer {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      justify-content: flex-end;
      padding-block-start: var(--space-4);
      border-block-start: 1px solid var(--color-border);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FormShell {
  readonly heading = input.required<string>();
  readonly description = input<string | null>(null);
  /** Messages that belong to no single field — what `FormGroup.applyServerErrors` returned. */
  readonly summary = input<readonly string[]>([]);
  readonly saving = input(false);
  readonly dirty = input(false);
  readonly submitLabel = input('Save');
  readonly cancelLabel = input('Cancel');
  /** False when the screen projects its own footer through the `actions` slot. */
  readonly showDefaultActions = input(true);

  readonly submitted = output<void>();
  readonly cancelled = output<void>();

  protected onSubmit(event: Event): void {
    // A real `<form>` with a real submit button, so Enter submits and a password manager
    // recognises it — and `preventDefault` because navigation is the router's, not the browser's.
    event.preventDefault();
    if (!this.saving()) this.submitted.emit();
  }
}
