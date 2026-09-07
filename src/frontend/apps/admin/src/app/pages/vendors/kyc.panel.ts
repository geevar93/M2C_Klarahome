import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import {
  KycDocumentResponse,
  KycDocumentType,
  KycSummaryResponse,
  MediaFileResponse,
  VendorsAdminService,
} from '@klarahome/data-access-admin';
import { ConfirmDialog, Modal } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Control, Field, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';
import { MediaPicker } from '../catalog/media-picker';
import { KYC_DOCUMENT_TYPES } from './vendor-vocabulary';

/**
 * The documents a seller has to produce, and what has become of each.
 *
 * **The API says which documents are required**, per seller — `KycSummaryResponse` carries
 * `required` and `missing` alongside the documents themselves. So this panel does not hold a list
 * of what a partnership needs as against a private limited company; it renders the server's
 * answer. That matters because the requirement depends on the constitution, and a client-side copy
 * would be wrong for whichever kind of seller was added last.
 *
 * **A rejection must carry its reason.** The API refuses one without, and rightly: a seller told
 * only that a document was rejected has no way to fix it, and will send the same file again.
 *
 * The number is masked on the way back (`numberMasked`) for the same reason a bank account's is.
 * The file itself is fetched through a short-lived download link the API issues, not through this
 * screen holding the bytes.
 */
@Component({
  selector: 'kh-kyc-panel',
  imports: [Alert, Badge, Button, ConfirmDialog, Control, Field, MediaPicker, Modal, Skeleton],
  template: `
    <section class="panel">
      <header>
        <h2>Documents</h2>
        @if (canSubmit()) {
          <button khButton type="button" size="sm" (click)="startSubmit()">Submit a document</button>
        }
      </header>

      @if (error(); as message) {
        <kh-alert tone="danger" heading="Documents">{{ message }}</kh-alert>
      }

      @if (loading()) {
        <kh-skeleton height="8rem" />
      } @else if (summary(); as kyc) {
        @if (kyc.missing.length > 0) {
          <kh-alert tone="warning" heading="Still needed">
            {{ missingLabels() }}
          </kh-alert>
        } @else {
          <kh-alert tone="success" heading="Everything required has been submitted" />
        }

        @if (kyc.documents.length === 0) {
          <p class="empty">Nothing submitted yet.</p>
        } @else {
          <ul>
            @for (document of kyc.documents; track document.id) {
              <li>
                <div class="details">
                  <span class="label">
                    {{ typeLabel(document.documentType) }}
                    <kh-badge [tone]="toneFor(document)">{{ document.status }}</kh-badge>
                  </span>
                  <span class="note">
                    @if (document.numberMasked) {
                      {{ document.numberMasked }} ·
                    }
                    submitted {{ dateTime(document.submittedAt) }}
                  </span>
                  @if (document.rejectionReason) {
                    <span class="note warn">{{ document.rejectionReason }}</span>
                  }
                </div>

                <div class="actions">
                  @if (document.downloadUrl) {
                    <a
                      khButton
                      size="sm"
                      variant="tertiary"
                      [href]="document.downloadUrl"
                      target="_blank"
                      rel="noopener"
                    >
                      Open
                    </a>
                  }
                  @if (canVerify() && document.status === 'Pending') {
                    <button khButton type="button" size="sm" [disabled]="busy()" (click)="approve(document)">
                      Approve
                    </button>
                    <button
                      khButton
                      type="button"
                      size="sm"
                      variant="tertiary"
                      [disabled]="busy()"
                      (click)="rejecting.set(document)"
                    >
                      Reject
                    </button>
                  }
                </div>
              </li>
            }
          </ul>
        }
      }
    </section>

    <kh-modal [open]="submitting()" heading="Submit a document" (closed)="submitting.set(false)">
      @if (submitError(); as message) {
        <kh-alert tone="danger" heading="It could not be submitted">{{ message }}</kh-alert>
      }

      <kh-field label="Which document" for="kyc-type">
        <select
          khControl
          id="kyc-type"
          [value]="documentType()"
          (change)="documentType.set($any($event.target).value)"
        >
          @for (choice of documentTypes; track choice.value) {
            <option [value]="choice.value">{{ choice.label }}</option>
          }
        </select>
      </kh-field>

      <kh-field
        label="Number on the document"
        for="kyc-number"
        [optional]="true"
        hint="Stored masked; only the last few characters come back."
      >
        <input
          khControl
          id="kyc-number"
          type="text"
          [value]="documentNumber()"
          (input)="documentNumber.set($any($event.target).value)"
        />
      </kh-field>

      <div class="file">
        <span class="label">Scan or photograph</span>
        <span class="note">{{ fileId() ? fileName() : 'None chosen' }}</span>
        <button khButton type="button" size="sm" (click)="pickerOpen.set(true)">Choose a file</button>
      </div>

      <div slot="footer">
        <button khButton type="button" variant="tertiary" (click)="submitting.set(false)">Cancel</button>
        <button khButton type="button" variant="primary" [disabled]="busy() || !fileId()" (click)="submit()">
          {{ busy() ? 'Submitting…' : 'Submit' }}
        </button>
      </div>
    </kh-modal>

    <kh-media-picker
      [open]="pickerOpen()"
      [multiple]="false"
      accept="image/*,application/pdf"
      ownerType="VendorKyc"
      [ownerId]="vendorId()"
      (picked)="chooseFile($event)"
      (closed)="pickerOpen.set(false)"
    />

    <kh-confirm-dialog
      [open]="rejecting() !== null"
      heading="Reject this document"
      message="The reason is what the seller reads, and is the only thing that tells them what to send instead."
      confirmLabel="Reject"
      [requireReason]="true"
      [busy]="busy()"
      (confirmed)="reject($event.reason)"
      (cancelled)="rejecting.set(null)"
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
      margin-block-end: var(--space-3);
    }

    h2 {
      margin: 0;
      font-size: var(--text-lg);
    }

    ul {
      margin: 0;
      padding: 0;
      list-style: none;
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

    .note.warn {
      color: var(--color-danger);
    }

    .empty {
      margin: 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .actions {
      display: flex;
      gap: var(--space-2);
    }

    .file {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      flex-wrap: wrap;
      margin-block-start: var(--space-3);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class KycPanel {
  private readonly vendors = inject(VendorsAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly documentTypes = KYC_DOCUMENT_TYPES;
  protected readonly dateTime = tableDateTime;

  readonly vendorId = input.required<string>();
  /** Whether submitting a document is offered — the seller's own screen, and staff acting for them. */
  readonly canSubmit = input(true);
  /** Whether approving and rejecting are offered. Platform staff only. */
  readonly canVerify = input(false);
  readonly changed = output<void>();

  protected readonly summary = signal<KycSummaryResponse | null>(null);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly submitting = signal(false);
  protected readonly submitError = signal<string | null>(null);
  protected readonly documentType = signal<KycDocumentType>('Pan');
  protected readonly documentNumber = signal('');
  protected readonly fileId = signal<string | null>(null);
  protected readonly fileName = signal('');
  protected readonly pickerOpen = signal(false);
  protected readonly rejecting = signal<KycDocumentResponse | null>(null);

  protected readonly missingLabels = computed(() =>
    (this.summary()?.missing ?? []).map((type) => this.typeLabel(type)).join(', '),
  );

  constructor() {
    effect(() => {
      const id = this.vendorId();
      if (id) this.load(id);
    });
  }

  protected typeLabel(type: KycDocumentType): string {
    return this.documentTypes.find((choice) => choice.value === type)?.label ?? type;
  }

  protected toneFor(document: KycDocumentResponse): 'success' | 'warning' | 'danger' {
    if (document.status === 'Verified') return 'success';
    return document.status === 'Rejected' ? 'danger' : 'warning';
  }

  protected startSubmit(): void {
    this.submitError.set(null);
    // Defaults to the first thing still missing, which is what somebody opening this is here for.
    this.documentType.set(this.summary()?.missing[0] ?? 'Pan');
    this.documentNumber.set('');
    this.fileId.set(null);
    this.fileName.set('');
    this.submitting.set(true);
  }

  protected chooseFile(files: readonly MediaFileResponse[]): void {
    this.pickerOpen.set(false);
    const file = files[0];
    if (!file) return;
    this.fileId.set(file.id);
    this.fileName.set(file.fileName ?? 'Chosen');
  }

  protected submit(): void {
    const fileId = this.fileId();
    if (!fileId || this.busy()) return;

    const id = this.vendorId();
    this.busy.set(true);
    this.submitError.set(null);

    this.vendors
      .submitKyc(id, {
        documentType: this.documentType(),
        fileId,
        number: this.documentNumber().trim() || null,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.submitting.set(false);
          this.toasts.success('Document submitted.');
          this.load(id);
          this.changed.emit();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.submitError.set(describeError(error, 'It could not be submitted.'));
        },
      });
  }

  protected approve(document: KycDocumentResponse): void {
    const id = this.vendorId();
    this.busy.set(true);

    this.vendors.verifyKyc(id, document.id, true, null).subscribe({
      next: () => {
        this.busy.set(false);
        this.toasts.success('Document approved.');
        this.load(id);
        this.changed.emit();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(describeError(error, 'It could not be approved.'));
      },
    });
  }

  protected reject(reason: string): void {
    const document = this.rejecting();
    if (!document) return;

    const id = this.vendorId();
    this.busy.set(true);

    this.vendors.verifyKyc(id, document.id, false, reason).subscribe({
      next: () => {
        this.busy.set(false);
        this.rejecting.set(null);
        this.load(id);
        this.changed.emit();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.rejecting.set(null);
        this.error.set(describeError(error, 'It could not be rejected.'));
      },
    });
  }

  private load(vendorId: string): void {
    this.loading.set(true);
    this.error.set(null);

    this.vendors.kyc(vendorId).subscribe({
      next: (summary) => {
        this.loading.set(false);
        this.summary.set(summary);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(error, 'They could not be loaded.'));
      },
    });
  }
}
