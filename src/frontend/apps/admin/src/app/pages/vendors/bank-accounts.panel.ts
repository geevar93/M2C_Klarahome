import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { BankAccountResponse, VendorsAdminService } from '@klarahome/data-access-admin';
import { ConfirmDialog } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';

/**
 * Where a seller's payouts go.
 *
 * **The account number goes in and never comes back.** `AddBankAccountBody` carries it in full;
 * `BankAccountResponse` carries the last four digits and nothing else, because Step 9 encrypts it
 * at rest and the API will not decrypt it for a screen. Nothing here holds it, and there is
 * deliberately no edit — a wrong account number is corrected by adding the right account and
 * making it primary, which leaves a record of both.
 *
 * **Verification is the platform's, and it is what a payout depends on.** A seller can add an
 * account; only somebody with the seller-management permission can mark it verified, and Step 18's
 * payout run skips a seller whose primary account is not. The verify control is therefore offered
 * only when `canVerify` is set, which is what separates this panel's two callers.
 *
 * Exactly one account is primary. Making a second one primary demotes the first, which is why it
 * is an endpoint rather than a checkbox on the form.
 */
@Component({
  selector: 'kh-bank-accounts-panel',
  imports: [Alert, Badge, Button, Checkbox, ConfirmDialog, Control, Field, Icon, Skeleton],
  template: `
    <section class="panel">
      <header>
        <h2>Bank accounts</h2>
        @if (canManage()) {
          <button khButton type="button" size="sm" (click)="startCreate()">
            <kh-icon name="plus" size="sm" />
            Add one
          </button>
        }
      </header>

      <p class="hint">Payouts go to the primary account, and only once it is verified.</p>

      @if (error(); as message) {
        <kh-alert tone="danger" heading="Bank accounts">{{ message }}</kh-alert>
      }

      @if (loading()) {
        <kh-skeleton height="6rem" />
      } @else if (accounts().length === 0) {
        <p class="empty">No bank account yet.</p>
      } @else {
        <ul>
          @for (account of accounts(); track account.id) {
            <li>
              <div class="details">
                <span class="label">
                  {{ account.accountName }}
                  @if (account.isPrimary) {
                    <kh-badge tone="primary">Primary</kh-badge>
                  }
                  <kh-badge [tone]="toneFor(account)">{{ statusLabel(account) }}</kh-badge>
                </span>
                <span class="note">
                  ••••{{ account.accountNumberLast4 }} · {{ account.ifsc }}
                  @if (account.bankName) {
                    · {{ account.bankName }}
                  }
                </span>
                @if (account.verifiedAt) {
                  <span class="note">Verified {{ dateTime(account.verifiedAt) }}</span>
                }
                @if (account.verificationNote) {
                  <span class="note">{{ account.verificationNote }}</span>
                }
              </div>

              <div class="actions">
                @if (canManage() && !account.isPrimary) {
                  <button
                    khButton
                    type="button"
                    size="sm"
                    variant="tertiary"
                    [disabled]="busy()"
                    (click)="makePrimary(account)"
                  >
                    Make primary
                  </button>
                }
                @if (canVerify() && account.verificationStatus !== 'Verified') {
                  <button
                    khButton
                    type="button"
                    size="sm"
                    [disabled]="busy()"
                    (click)="verifying.set(account)"
                  >
                    Verify
                  </button>
                }
                @if (canManage()) {
                  <button
                    khButton
                    type="button"
                    size="sm"
                    variant="tertiary"
                    [disabled]="busy()"
                    (click)="removing.set(account)"
                  >
                    Remove
                  </button>
                }
              </div>
            </li>
          }
        </ul>
      }

      @if (editorOpen()) {
        <div class="editor">
          <h3>New bank account</h3>
          <p class="hint">
            The account number is stored encrypted and never shown again — only the last four digits come
            back.
          </p>

          @if (summary().length > 0) {
            <kh-alert tone="danger" heading="It could not be added">
              <ul class="messages">
                @for (message of summary(); track message) {
                  <li>{{ message }}</li>
                }
              </ul>
            </kh-alert>
          }

          <kh-field label="Account holder" for="bank-name" [error]="form.fields.accountName.error()">
            <input
              khControl
              id="bank-name"
              type="text"
              maxlength="120"
              [value]="form.fields.accountName.value()"
              (input)="form.fields.accountName.set($any($event.target).value)"
              (touched)="form.fields.accountName.markTouched()"
            />
          </kh-field>

          <div class="row">
            <kh-field label="Account number" for="bank-number" [error]="form.fields.accountNumber.error()">
              <input
                khControl
                khNumeric
                id="bank-number"
                type="text"
                inputmode="numeric"
                autocomplete="off"
                [value]="form.fields.accountNumber.value()"
                (input)="form.fields.accountNumber.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="IFSC" for="bank-ifsc" [error]="form.fields.ifsc.error()">
              <input
                khControl
                id="bank-ifsc"
                type="text"
                maxlength="11"
                [value]="form.fields.ifsc.value()"
                (input)="form.fields.ifsc.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <div class="row">
            <kh-field label="Bank" for="bank-bank" [optional]="true">
              <input
                khControl
                id="bank-bank"
                type="text"
                [value]="form.fields.bankName.value()"
                (input)="form.fields.bankName.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Branch" for="bank-branch" [optional]="true">
              <input
                khControl
                id="bank-branch"
                type="text"
                [value]="form.fields.branchName.value()"
                (input)="form.fields.branchName.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <kh-checkbox
            label="Make this the primary account"
            inputId="bank-primary"
            [checked]="makePrimaryOnAdd()"
            (checkedChange)="makePrimaryOnAdd.set($event)"
          />

          <div class="actions">
            <button khButton type="button" variant="tertiary" (click)="editorOpen.set(false)">Cancel</button>
            <button khButton type="button" variant="primary" [disabled]="busy()" (click)="save()">
              {{ busy() ? 'Adding…' : 'Add account' }}
            </button>
          </div>
        </div>
      }
    </section>

    <kh-confirm-dialog
      [open]="verifying() !== null"
      heading="Mark this account verified"
      message="Payouts to this seller become possible. Verify only against a bank statement or a penny drop, not against what was typed."
      confirmLabel="Mark verified"
      tone="warning"
      [requireReason]="true"
      [busy]="busy()"
      (confirmed)="verify($event.reason)"
      (cancelled)="verifying.set(null)"
    />

    <kh-confirm-dialog
      [open]="removing() !== null"
      heading="Remove this bank account"
      message="Payouts already sent to it are unaffected. A seller with no verified primary account cannot be paid."
      confirmLabel="Remove"
      [busy]="busy()"
      (confirmed)="remove()"
      (cancelled)="removing.set(null)"
    />
  `,
  styles: `
    .panel {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    header {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      justify-content: space-between;
    }

    h2 {
      margin: 0;
      font-size: var(--text-lg);
    }

    h3 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-base);
    }

    .hint {
      margin: var(--space-1) 0 var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    ul.messages {
      padding-inline-start: var(--space-5);
      list-style: disc;
    }

    li {
      display: flex;
      gap: var(--space-3);
      align-items: flex-start;
      justify-content: space-between;
      padding-block: var(--space-3);
      border-block-end: 1px solid var(--color-border);
    }

    .label {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      font-weight: var(--weight-medium);
    }

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .empty {
      margin: 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .editor {
      margin-block-start: var(--space-4);
      padding-block-start: var(--space-4);
      border-block-start: 1px solid var(--color-border);
    }

    .row {
      display: flex;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .row > kh-field {
      flex: 1 1 10rem;
    }

    .actions {
      display: flex;
      gap: var(--space-2);
      margin-block-start: var(--space-3);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BankAccountsPanel {
  private readonly vendors = inject(VendorsAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly dateTime = tableDateTime;

  readonly vendorId = input.required<string>();
  /** Whether add, remove and make-primary are offered. */
  readonly canManage = input(true);
  /** Whether marking an account verified is offered. Platform staff only. */
  readonly canVerify = input(false);
  readonly changed = output<void>();

  protected readonly accounts = signal<readonly BankAccountResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly editorOpen = signal(false);
  protected readonly verifying = signal<BankAccountResponse | null>(null);
  protected readonly removing = signal<BankAccountResponse | null>(null);
  protected readonly makePrimaryOnAdd = signal(true);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    accountName: formField('', [required('The account holder')], this.submitted),
    accountNumber: formField('', [required('An account number')], this.submitted),
    ifsc: formField('', [required('An IFSC')], this.submitted),
    bankName: formField('', [], this.submitted),
    branchName: formField('', [], this.submitted),
  });

  constructor() {
    effect(() => {
      const id = this.vendorId();
      if (id) this.load(id);
    });
  }

  protected toneFor(account: BankAccountResponse): 'success' | 'warning' | 'danger' {
    if (account.verificationStatus === 'Verified') return 'success';
    return account.verificationStatus === 'Failed' ? 'danger' : 'warning';
  }

  protected statusLabel(account: BankAccountResponse): string {
    if (account.verificationStatus === 'Verified') return 'Verified';
    return account.verificationStatus === 'Failed' ? 'Verification failed' : 'Not verified';
  }

  protected startCreate(): void {
    this.summary.set([]);
    this.makePrimaryOnAdd.set(this.accounts().length === 0);
    this.form.reset({ accountName: '', accountNumber: '', ifsc: '', bankName: '', branchName: '' });
    this.editorOpen.set(true);
  }

  protected save(): void {
    if (!this.form.submit() || this.busy()) return;

    const values = this.form.values();
    const id = this.vendorId();

    this.busy.set(true);
    this.summary.set([]);

    this.vendors
      .addBankAccount(id, {
        accountName: values.accountName,
        accountNumber: values.accountNumber,
        ifsc: values.ifsc.toUpperCase(),
        bankName: values.bankName || null,
        branchName: values.branchName || null,
        makePrimary: this.makePrimaryOnAdd(),
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.editorOpen.set(false);
          // Cleared rather than left on screen: the number must not sit in a form after it is sent.
          this.form.reset({ accountName: '', accountNumber: '', ifsc: '', bankName: '', branchName: '' });
          this.toasts.success('Bank account added.');
          this.load(id);
          this.changed.emit();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          const errors = fieldErrors(error);
          this.summary.set(
            errors ? this.form.applyServerErrors(errors) : [describeError(error, 'It could not be added.')],
          );
        },
      });
  }

  protected makePrimary(account: BankAccountResponse): void {
    const id = this.vendorId();
    this.busy.set(true);

    this.vendors.makeBankAccountPrimary(id, account.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.load(id);
        this.changed.emit();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(describeError(error, 'It could not be made primary.'));
      },
    });
  }

  protected verify(note: string): void {
    const account = this.verifying();
    if (!account) return;

    const id = this.vendorId();
    this.busy.set(true);

    this.vendors.verifyBankAccount(id, account.id, true, note || null).subscribe({
      next: () => {
        this.busy.set(false);
        this.verifying.set(null);
        this.toasts.success('Account marked verified.');
        this.load(id);
        this.changed.emit();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.verifying.set(null);
        this.error.set(describeError(error, 'It could not be verified.'));
      },
    });
  }

  protected remove(): void {
    const account = this.removing();
    if (!account) return;

    const id = this.vendorId();
    this.busy.set(true);

    this.vendors.removeBankAccount(id, account.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.removing.set(null);
        this.load(id);
        this.changed.emit();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.removing.set(null);
        this.error.set(describeError(error, 'It could not be removed.'));
      },
    });
  }

  private load(vendorId: string): void {
    this.loading.set(true);
    this.error.set(null);

    this.vendors.bankAccounts(vendorId).subscribe({
      next: (accounts) => {
        this.loading.set(false);
        this.accounts.set(accounts);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(error, 'They could not be loaded.'));
      },
    });
  }
}
