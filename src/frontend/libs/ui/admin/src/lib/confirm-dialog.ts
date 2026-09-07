import { ChangeDetectionStrategy, Component, computed, effect, input, output, signal } from '@angular/core';
import { Alert, Button, Control, Field } from '@klarahome/ui-primitives';

import { Modal } from './modal';

/**
 * The confirmation a destructive action has to pass.
 *
 * Back-office mistakes are not undone by a shopper reloading a page: cancelling a sub-order that
 * has shipped, offboarding a seller mid-settlement, or deleting a category with four hundred
 * products under it are all irreversible from the browser. `docs/05-frontend-architecture.md` §4.3
 * requires that such an action take **a typed confirmation and a reason**, and this is the one
 * component that implements both — so that no screen gets to decide its own deletion is the safe
 * kind.
 *
 * Both are here for different reasons, and each does something the other cannot:
 *
 *  - **The typed phrase** defeats muscle memory. A user who has clicked "Delete → Confirm" forty
 *    times this week does not read the forty-first; typing `ORD-1042` cannot be done by habit.
 *  - **The reason** is not friction, it is the audit trail. Every one of these actions is written
 *    to `platform.audit_logs`, and "cancelled by Priya, 14:02" answers nothing a week later while
 *    "customer moved house, courier could not deliver" answers everything.
 *
 * Which of the two is demanded is the caller's decision: `confirmPhrase` and `requireReason` are
 * independent, and an action that needs neither should not be using this component at all.
 */
@Component({
  selector: 'kh-confirm-dialog',
  imports: [Alert, Button, Control, Field, Modal],
  template: `
    <kh-modal
      [open]="open()"
      [heading]="heading()"
      [dismissible]="!busy()"
      width="30rem"
      (closed)="cancelled.emit()"
    >
      <kh-alert [tone]="tone()">{{ message() }}</kh-alert>

      @if (confirmPhrase(); as phrase) {
        <kh-field
          label="Type {{ phrase }} to confirm"
          for="confirm-phrase"
          [error]="phraseError()"
          hint="This cannot be undone."
        >
          <input
            khControl
            id="confirm-phrase"
            type="text"
            autocomplete="off"
            spellcheck="false"
            [value]="typed()"
            [khInvalid]="!!phraseError()"
            (input)="typed.set($any($event.target).value)"
          />
        </kh-field>
      }

      @if (requireReason()) {
        <kh-field
          label="Reason"
          for="confirm-reason"
          [error]="reasonError()"
          hint="Recorded in the audit trail and shown to whoever asks why."
        >
          <textarea
            khControl
            id="confirm-reason"
            rows="3"
            [value]="reason()"
            [khInvalid]="!!reasonError()"
            (input)="reason.set($any($event.target).value)"
          ></textarea>
        </kh-field>
      }

      <div slot="footer">
        <button khButton type="button" variant="tertiary" [disabled]="busy()" (click)="cancelled.emit()">
          Cancel
        </button>
        <button
          khButton
          type="button"
          [variant]="tone() === 'danger' ? 'danger' : 'primary'"
          [disabled]="!canConfirm() || busy()"
          (click)="submit()"
        >
          {{ busy() ? 'Working…' : confirmLabel() }}
        </button>
      </div>
    </kh-modal>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ConfirmDialog {
  readonly open = input(false);
  readonly heading = input.required<string>();
  readonly message = input.required<string>();
  readonly confirmLabel = input('Confirm');
  readonly tone = input<'danger' | 'warning'>('danger');
  /** The exact text the user must type. Absent means no typed confirmation is demanded. */
  readonly confirmPhrase = input<string | null>(null);
  readonly requireReason = input(false);
  /** True while the action is in flight; the dialog stays up, disabled, rather than vanishing. */
  readonly busy = input(false);

  /** Carries the reason, which is empty when none was asked for. */
  readonly confirmed = output<{ reason: string }>();
  readonly cancelled = output<void>();

  protected readonly typed = signal('');
  protected readonly reason = signal('');
  private readonly attempted = signal(false);

  protected readonly phraseMatches = computed(() => {
    const phrase = this.confirmPhrase();
    // Case-insensitive and trimmed: the point is that the user read the identifier, not that
    // they can reproduce its capitalisation.
    return !phrase || this.typed().trim().toLowerCase() === phrase.trim().toLowerCase();
  });

  protected readonly reasonGiven = computed(() => !this.requireReason() || this.reason().trim().length > 0);
  protected readonly canConfirm = computed(() => this.phraseMatches() && this.reasonGiven());

  // Messages appear only once the user has tried to confirm — never while they are still typing
  // the phrase they were asked for.
  protected readonly phraseError = computed(() =>
    this.attempted() && !this.phraseMatches() ? `That does not match ${this.confirmPhrase()}.` : null,
  );
  protected readonly reasonError = computed(() =>
    this.attempted() && !this.reasonGiven() ? 'A reason is required.' : null,
  );

  constructor() {
    // A dialog reopened for a different row must not still hold the last one's text.
    effect(() => {
      if (!this.open()) {
        this.typed.set('');
        this.reason.set('');
        this.attempted.set(false);
      }
    });
  }

  protected submit(): void {
    this.attempted.set(true);
    if (!this.canConfirm()) return;
    this.confirmed.emit({ reason: this.reason().trim() });
  }
}
