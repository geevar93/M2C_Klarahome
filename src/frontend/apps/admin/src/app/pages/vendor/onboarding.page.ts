import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { VendorReadiness, VendorResponse, VendorsAdminService } from '@klarahome/data-access-admin';
import { ConfirmDialog, PageHeader, StatusBadge } from '@klarahome/ui-admin';
import { Alert, Button, Icon, Skeleton, Stepper, StepperStep } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { BankAccountsPanel } from '../vendors/bank-accounts.panel';
import { KycPanel } from '../vendors/kyc.panel';
import { PickupLocationsPanel } from '../vendors/pickup-locations.panel';
import { ServiceableRegionsPanel } from '../vendors/serviceable-regions.panel';

/** The stages an application passes through, as a seller experiences them. */
const STAGES: readonly StepperStep[] = [
  { id: 'Applied', label: 'Your details' },
  { id: 'UnderReview', label: 'Under review' },
  { id: 'Approved', label: 'Approved' },
  { id: 'Active', label: 'Trading' },
];

/**
 * A seller's own onboarding.
 *
 * **The server says what is outstanding, and this screen shows it.** `GET
 * /admin/vendors/{id}/readiness` returns the blockers — a missing document, an unverified bank
 * account, no pickup address — and every panel re-asks for it after a change, so the checklist at
 * the top is never a client-side guess about what "complete" means. That matters here more than on
 * the platform's version of this screen: a seller who submits into a refusal has no way to find
 * out why, and this is what stops them doing it.
 *
 * **The same four panels as the platform's seller record, with the verifying taken away.** A seller
 * submits a document; only the platform marks it verified. A seller adds a bank account; only the
 * platform marks it verified. The panels take that as an input rather than being reimplemented,
 * which is what keeps the two sides of the same conversation describing the same rows.
 *
 * **Submitting is the seller's only transition.** Everything after it — approve, activate, send
 * back — belongs to the platform, so this screen shows where the application has got to and offers
 * exactly one button.
 */
@Component({
  selector: 'kh-vendor-onboarding-page',
  imports: [
    Alert,
    BankAccountsPanel,
    Button,
    ConfirmDialog,
    Icon,
    KycPanel,
    PageHeader,
    PickupLocationsPanel,
    ServiceableRegionsPanel,
    Skeleton,
    StatusBadge,
    Stepper,
  ],
  template: `
    <kh-page-header
      heading="Getting set up"
      description="What is still needed before you can sell, and where your application has got to."
    >
      @if (vendor(); as current) {
        <kh-status-badge [status]="current.status" />
        @if (canSubmit()) {
          <button khButton type="button" variant="primary" [disabled]="busy()" (click)="submitting.set(true)">
            <kh-icon name="check" size="sm" />
            Send for review
          </button>
        }
      }
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="Your seller account could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="20rem" />
    } @else if (vendor(); as current) {
      @if (actionError(); as message) {
        <kh-alert tone="danger" heading="That did not take">{{ message }}</kh-alert>
      }

      <kh-stepper [steps]="stages" [active]="stage()" />

      @if (current.statusReason && current.status === 'Applied') {
        <kh-alert tone="warning" heading="Sent back to you">
          {{ current.statusReason }}
        </kh-alert>
      }

      @if (readiness(); as state) {
        @if (state.isReady) {
          <kh-alert tone="success" heading="Everything is in place">
            @if (current.status === 'Applied') {
              Send it for review when you are ready.
            } @else {
              Nothing is outstanding on your account.
            }
          </kh-alert>
        } @else {
          <kh-alert tone="warning" heading="Still needed">
            <ul>
              @for (blocker of state.blockers; track blocker) {
                <li>{{ blocker }}</li>
              }
            </ul>
          </kh-alert>
        }
      }

      <div class="layout">
        <kh-kyc-panel
          [vendorId]="current.id"
          [canSubmit]="true"
          [canVerify]="false"
          (changed)="loadReadiness()"
        />

        <kh-bank-accounts-panel
          [vendorId]="current.id"
          [canManage]="true"
          [canVerify]="false"
          (changed)="loadReadiness()"
        />

        <kh-pickup-locations-panel [vendorId]="current.id" [canManage]="true" (changed)="loadReadiness()" />

        <kh-serviceable-regions-panel
          [vendorId]="current.id"
          [canManage]="true"
          (changed)="loadReadiness()"
        />
      </div>
    }

    <kh-confirm-dialog
      [open]="submitting()"
      heading="Send your application for review"
      message="Somebody at the store checks your documents and your bank details. You can still change things while it is being reviewed, but changes may restart the check."
      confirmLabel="Send it"
      tone="warning"
      [busy]="busy()"
      (confirmed)="submit()"
      (cancelled)="submitting.set(false)"
    />
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    kh-alert ul {
      margin: 0;
      padding-inline-start: var(--space-5);
    }

    kh-stepper {
      display: block;
      margin-block-end: var(--space-4);
    }

    .layout {
      display: grid;
      gap: var(--space-4);
      grid-template-columns: minmax(0, 1fr);
    }

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: repeat(2, minmax(0, 1fr));
        align-items: start;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VendorOnboardingPage {
  private readonly vendors = inject(VendorsAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly stages = STAGES;

  protected readonly vendor = signal<VendorResponse | null>(null);
  protected readonly readiness = signal<VendorReadiness | null>(null);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly submitting = signal(false);

  /**
   * Which stage the marker sits on.
   *
   * `Suspended` and `Offboarded` are not stages of an application — they are what happens after
   * one — so they fall back to the first, and the status badge in the header is what says the true
   * thing about them.
   */
  protected readonly stage = computed(() => {
    const status = this.vendor()?.status;
    return STAGES.some((step) => step.id === status) ? (status as string) : 'Applied';
  });

  protected readonly canSubmit = computed(() => this.vendor()?.status === 'Applied');

  constructor() {
    this.load();
  }

  protected submit(): void {
    const current = this.vendor();
    if (!current || this.busy()) return;

    this.busy.set(true);
    this.actionError.set(null);

    this.vendors.submit(current.id, null).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.submitting.set(false);
        this.vendor.set(saved);
        this.toasts.success('Sent for review.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.submitting.set(false);
        this.actionError.set(describeError(error, 'It could not be sent — something is still outstanding.'));
      },
    });
  }

  protected loadReadiness(): void {
    const current = this.vendor();
    if (!current) return;

    this.vendors.readiness(current.id).subscribe({
      next: (readiness) => this.readiness.set(readiness),
      // Not fatal: each panel still says what it knows about its own part.
      error: () => this.readiness.set(null),
    });
  }

  private load(): void {
    this.loading.set(true);
    this.vendors.mine().subscribe({
      next: (vendor) => {
        this.loading.set(false);
        this.vendor.set(vendor);
        this.loadReadiness();
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'Your seller account could not be loaded.'));
      },
    });
  }
}
