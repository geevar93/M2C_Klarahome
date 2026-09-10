import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import {
  CommissionPlanResponse,
  IdentityAdminService,
  ReferenceDataService,
  VendorBusinessType,
  VendorReadiness,
  VendorResponse,
  VendorsAdminService,
  VendorStaffResponse,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import { ConfirmDialog, EntityOption, EntityPicker, PageHeader, StatusBadge } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Control, Field, Icon, Rating, Skeleton } from '@klarahome/ui-primitives';
import { Observable, map } from 'rxjs';

import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';
import { BankAccountsPanel } from './bank-accounts.panel';
import { KycPanel } from './kyc.panel';
import { PickupLocationsPanel } from './pickup-locations.panel';
import { ServiceableRegionsPanel } from './serviceable-regions.panel';
import { VENDOR_TRANSITIONS, VendorTransition } from './vendor-vocabulary';

/**
 * One seller, as the platform sees them.
 *
 * **Readiness is the spine of this screen.** `GET /admin/vendors/{id}/readiness` answers what still
 * stands between this seller and being able to trade — unverified documents, no primary bank
 * account, no pickup address — and every panel below re-asks for it after a change. That is why
 * the blockers list sits at the top rather than the legal details: somebody opening a seller record
 * is nearly always answering "why can they not sell yet", and the server already knows.
 *
 * **The onboarding transitions are separate endpoints, not a transition table.** Unlike an order or
 * a return, a vendor carries no `nextStatuses`, so which buttons to offer from which status is a
 * client-side statement in `vendor-vocabulary.ts` — a second copy of a rule the server owns. It is
 * recorded in `PARKING_LOT.md` as such. It is not unsafe: the API refuses an edge it does not
 * allow, and the refusal is shown; it is merely a copy that can go stale, and the honest fix is
 * for the vendor record to carry its own allowed transitions the way everything else does.
 *
 * **Suspending and offboarding demand a reason** because a seller reads it and because somebody
 * will ask, months later, why a shop went dark. Approving does not, because approving is the
 * expected outcome and a mandatory note on the common path is a note that says "ok".
 */
@Component({
  selector: 'kh-vendor-detail-page',
  imports: [
    Alert,
    Badge,
    BankAccountsPanel,
    Button,
    ConfirmDialog,
    Control,
    EntityPicker,
    Field,
    HasPermission,
    Icon,
    KycPanel,
    PageHeader,
    PickupLocationsPanel,
    Rating,
    ServiceableRegionsPanel,
    Skeleton,
    StatusBadge,
  ],
  template: `
    <kh-page-header
      [heading]="vendor()?.displayName ?? 'Seller'"
      [description]="subtitle()"
      [crumbs]="[{ label: 'Sellers', path: '/vendors' }]"
    >
      @if (vendor(); as current) {
        <kh-status-badge [status]="current.status" />

        <ng-container *khHasPermission="'vendors.vendor.approve'">
          @for (transition of available(); track transition.key) {
            <button
              khButton
              type="button"
              size="sm"
              [variant]="transition.destructive ? 'danger' : 'tertiary'"
              [disabled]="busy()"
              (click)="pending.set(transition)"
            >
              {{ transition.label }}
            </button>
          }
        </ng-container>
      }
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="This seller could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="24rem" />
    } @else if (vendor(); as current) {
      @if (actionError(); as message) {
        <kh-alert tone="danger" heading="That did not take">{{ message }}</kh-alert>
      }

      @if (readiness(); as state) {
        @if (state.isReady) {
          <kh-alert tone="success" heading="Ready to trade">
            Nothing is outstanding on this seller's record.
          </kh-alert>
        } @else {
          <kh-alert tone="warning" heading="Not ready to trade yet">
            <ul>
              @for (blocker of state.blockers; track blocker) {
                <li>{{ blocker }}</li>
              }
            </ul>
          </kh-alert>
        }
      }

      <div class="layout">
        <div class="column">
          <kh-kyc-panel [vendorId]="id" [canSubmit]="false" [canVerify]="true" (changed)="loadReadiness()" />

          <kh-bank-accounts-panel
            [vendorId]="id"
            [canManage]="true"
            [canVerify]="true"
            (changed)="loadReadiness()"
          />

          <kh-pickup-locations-panel [vendorId]="id" [canManage]="true" (changed)="loadReadiness()" />

          <kh-serviceable-regions-panel [vendorId]="id" [canManage]="true" (changed)="loadReadiness()" />
        </div>

        <div class="column">
          <section class="panel">
            <h2>The legal record</h2>
            <dl>
              <dt>Code</dt>
              <dd>{{ current.code }}</dd>
              <dt>Storefront</dt>
              <dd>/{{ current.slug }}</dd>
            </dl>

            <kh-field label="Legal name" for="vendor-legal-name">
              <input
                khControl
                id="vendor-legal-name"
                type="text"
                [value]="legalName()"
                (input)="legalName.set($any($event.target).value)"
              />
            </kh-field>

            <kh-field label="Constitution" for="vendor-business-type">
              <select
                khControl
                id="vendor-business-type"
                [value]="businessType()"
                (change)="businessType.set($any($event.target).value)"
              >
                @for (type of businessTypes; track type) {
                  <option [value]="type">{{ type }}</option>
                }
              </select>
            </kh-field>

            <div class="pair">
              <kh-field label="PAN" for="vendor-pan" [optional]="true">
                <input
                  khControl
                  id="vendor-pan"
                  type="text"
                  maxlength="10"
                  [value]="pan()"
                  (input)="pan.set($any($event.target).value)"
                />
              </kh-field>
              <kh-field label="GSTIN" for="vendor-gstin" [optional]="true">
                <input
                  khControl
                  id="vendor-gstin"
                  type="text"
                  maxlength="15"
                  [value]="gstin()"
                  (input)="gstin.set($any($event.target).value)"
                />
              </kh-field>
            </div>

            <kh-field label="Address line 1" for="vendor-address-line1">
              <input
                khControl
                id="vendor-address-line1"
                type="text"
                [value]="addressLine1()"
                (input)="addressLine1.set($any($event.target).value)"
              />
            </kh-field>

            <kh-field label="Address line 2" for="vendor-address-line2" [optional]="true">
              <input
                khControl
                id="vendor-address-line2"
                type="text"
                [value]="addressLine2()"
                (input)="addressLine2.set($any($event.target).value)"
              />
            </kh-field>

            <div class="pair">
              <kh-field label="City" for="vendor-address-city">
                <input
                  khControl
                  id="vendor-address-city"
                  type="text"
                  [value]="addressCity()"
                  (input)="addressCity.set($any($event.target).value)"
                />
              </kh-field>
              <kh-field label="Pincode" for="vendor-address-pincode">
                <input
                  khControl
                  id="vendor-address-pincode"
                  type="text"
                  maxlength="6"
                  [value]="addressPincode()"
                  (input)="addressPincode.set($any($event.target).value)"
                />
              </kh-field>
            </div>

            <kh-field label="State" for="vendor-address-state">
              <select
                khControl
                id="vendor-address-state"
                [value]="addressStateId()"
                (change)="addressStateId.set($any($event.target).value)"
              >
                <option value="">Choose a state</option>
                @for (state of states(); track state.id) {
                  <option [value]="state.id">{{ state.name }}</option>
                }
              </select>
            </kh-field>

            <button
              khButton
              type="button"
              *khHasPermission="'vendors.vendor.manage'"
              [disabled]="busy()"
              (click)="saveLegal()"
            >
              Save the legal record
            </button>

            @if (current.statusReason) {
              <p class="note">Last status change: {{ current.statusReason }}</p>
            }
          </section>

          <section class="panel">
            <h2>Trading terms</h2>
            <dl>
              <dt>Dispatch promise</dt>
              <dd>{{ current.dispatchSlaHours }} hours</dd>
              <dt>Returns</dt>
              <dd>
                {{
                  current.returnPolicy.acceptsReturns
                    ? current.returnPolicy.windowDays + ' days'
                    : 'Not accepted'
                }}
              </dd>
              <dt>Rating</dt>
              <dd>
                @if (current.rating !== null) {
                  <kh-rating [average]="current.rating" size="sm" />
                } @else {
                  <span class="note">No reviews yet</span>
                }
              </dd>
              <dt>Gateway account</dt>
              <dd>{{ current.gatewayAccountId ?? 'Not linked' }}</dd>
            </dl>
          </section>

          <section class="panel">
            <h2>Commission</h2>
            <p class="hint">
              What the platform charges on each sale. Left on the default, the store's default plan applies.
              Changing it does not re-rate orders already placed — the rate is frozen onto the line.
            </p>

            <kh-field label="Plan" for="vendor-plan">
              <select
                khControl
                id="vendor-plan"
                [value]="commissionPlanId()"
                (change)="commissionPlanId.set($any($event.target).value)"
              >
                <option value="">The store's default</option>
                @for (plan of plans(); track plan.id) {
                  <option [value]="plan.id">{{ plan.name }} ({{ plan.defaultRate }}%)</option>
                }
              </select>
            </kh-field>

            <button
              khButton
              type="button"
              *khHasPermission="'vendors.commission.manage'"
              [disabled]="busy()"
              (click)="assignPlan()"
            >
              Save the plan
            </button>
          </section>

          <section class="panel">
            <h2>People</h2>
            <p class="hint">Who may sign in as this seller. Add a user under Settings first.</p>

            @if (staff().length === 0) {
              <p class="empty">Nobody is attached to this seller yet.</p>
            } @else {
              <ul class="staff">
                @for (member of staff(); track member.id) {
                  <li>
                    <div>
                      <span class="label">
                        {{ member.jobTitle || 'Staff' }}
                        @if (member.isOwner) {
                          <kh-badge tone="primary">Owner</kh-badge>
                        }
                      </span>
                      <span class="note">{{ member.userId }} · added {{ dateTime(member.addedAt) }}</span>
                    </div>
                    <button
                      khButton
                      type="button"
                      size="sm"
                      variant="tertiary"
                      *khHasPermission="'vendors.vendor.manage'"
                      [disabled]="busy()"
                      (click)="removingStaff.set(member)"
                    >
                      Remove
                    </button>
                  </li>
                }
              </ul>
            }

            <div class="add-staff" *khHasPermission="'vendors.vendor.manage'">
              <kh-entity-picker
                label="User"
                inputId="staff-user"
                hint="An existing seller account, by email or mobile."
                [search]="userSearch"
                (chose)="staffUserId.set($event?.id ?? '')"
              />
              <kh-field label="Job title" for="staff-title" [optional]="true">
                <input
                  khControl
                  id="staff-title"
                  type="text"
                  [value]="staffJobTitle()"
                  (input)="staffJobTitle.set($any($event.target).value)"
                />
              </kh-field>
              <button khButton type="button" [disabled]="busy()" (click)="addStaff()">
                <kh-icon name="plus" size="sm" />
                Attach
              </button>
            </div>
          </section>
        </div>
      </div>
    }

    <kh-confirm-dialog
      [open]="pending() !== null"
      [heading]="pending()?.label ?? ''"
      [message]="pendingMessage()"
      [confirmLabel]="pending()?.label ?? 'Confirm'"
      [tone]="pending()?.destructive ? 'danger' : 'warning'"
      [requireReason]="pending()?.requiresReason ?? false"
      [busy]="busy()"
      (confirmed)="runTransition($event.reason)"
      (cancelled)="pending.set(null)"
    />

    <kh-confirm-dialog
      [open]="removingStaff() !== null"
      heading="Remove this person"
      message="They lose access to this seller's screens at their next sign-in."
      confirmLabel="Remove"
      [busy]="busy()"
      (confirmed)="removeStaff()"
      (cancelled)="removingStaff.set(null)"
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

    .layout {
      display: grid;
      gap: var(--space-4);
      grid-template-columns: minmax(0, 1fr);
    }

    @media (min-width: 64rem) {
      .layout {
        grid-template-columns: repeat(2, minmax(0, 1fr));
        align-items: start;
      }
    }

    .column {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    .panel {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    h2 {
      margin: 0 0 var(--space-3);
      font-size: var(--text-lg);
    }

    dl {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: var(--space-2) var(--space-4);
      margin: 0 0 var(--space-4);
    }

    .pair {
      display: grid;
      gap: var(--space-3);
      grid-template-columns: minmax(0, 1fr);
    }

    @media (min-width: 32rem) {
      .pair {
        grid-template-columns: repeat(2, minmax(0, 1fr));
      }
    }

    dt {
      color: var(--color-text-muted);
    }

    dd {
      margin: 0;
    }

    .hint {
      margin: 0 0 var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .note {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .empty {
      margin: 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .staff {
      margin: 0 0 var(--space-3);
      padding: 0;
      list-style: none;
    }

    .staff li {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      justify-content: space-between;
      padding-block: var(--space-2);
      border-block-end: 1px solid var(--color-border);
    }

    .label {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      font-weight: var(--weight-medium);
    }

    .add-staff {
      display: flex;
      gap: var(--space-2);
      align-items: flex-end;
      flex-wrap: wrap;
    }

    .add-staff > kh-field {
      flex: 1 1 9rem;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VendorDetailPage {
  private readonly vendors = inject(VendorsAdminService);
  private readonly identity = inject(IdentityAdminService);

  /**
   * Finds seller accounts for the staff picker.
   *
   * Narrowed to `Vendor` accounts: a seller's staff member is somebody who already has a seller
   * login, and offering customers here would be offering a mistake (Step 28B, deliverable 15).
   */
  protected readonly userSearch = (term: string): Observable<readonly EntityOption[]> =>
    this.identity.searchUsers(term, 'Vendor').pipe(
      map((users) =>
        users.map((user) => ({
          id: user.id,
          label: user.email ?? user.mobile ?? user.id,
          hint: user.status,
        })),
      ),
    );
  private readonly route = inject(ActivatedRoute);
  private readonly toasts = inject(ToastService);

  /** The platform's states, so nobody types an identifier (Step 28B, deliverable 3). */
  protected readonly states = toSignal(inject(ReferenceDataService).states, { initialValue: [] });

  protected readonly businessTypes: readonly VendorBusinessType[] = [
    'Individual',
    'SoleProprietorship',
    'Partnership',
    'LimitedLiabilityPartnership',
    'PrivateLimited',
    'PublicLimited',
    'HinduUndividedFamily',
    'Trust',
  ];

  protected readonly dateTime = tableDateTime;
  protected readonly id = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly vendor = signal<VendorResponse | null>(null);
  protected readonly readiness = signal<VendorReadiness | null>(null);
  protected readonly plans = signal<readonly CommissionPlanResponse[]>([]);
  protected readonly staff = signal<readonly VendorStaffResponse[]>([]);

  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);

  protected readonly commissionPlanId = signal('');
  protected readonly staffUserId = signal('');
  protected readonly staffJobTitle = signal('');

  protected readonly legalName = signal('');
  protected readonly businessType = signal<VendorBusinessType>('Individual');
  protected readonly pan = signal('');
  protected readonly gstin = signal('');
  protected readonly addressLine1 = signal('');
  protected readonly addressLine2 = signal('');
  protected readonly addressCity = signal('');
  protected readonly addressStateId = signal('');
  protected readonly addressPincode = signal('');

  protected readonly pending = signal<VendorTransition | null>(null);
  protected readonly removingStaff = signal<VendorStaffResponse | null>(null);

  protected readonly subtitle = computed(() => {
    const current = this.vendor();
    if (!current) return null;
    const since = current.onboardedAt
      ? `trading since ${tableDateTime(current.onboardedAt)}`
      : 'not trading yet';
    return `${current.code} · ${current.legalName} · ${since}`;
  });

  /**
   * The edges the server says are available from here.
   *
   * Read off `nextStatuses` rather than off a table this app keeps, so a life-cycle rule that
   * changes on the server changes the buttons without a release.
   */
  protected readonly available = computed<readonly VendorTransition[]>(() => {
    const next = this.vendor()?.nextStatuses;
    if (!next?.length) return [];
    return VENDOR_TRANSITIONS.filter((transition) => next.includes(transition.to));
  });

  protected readonly pendingMessage = computed(() => {
    const transition = this.pending();
    if (!transition) return '';
    switch (transition.key) {
      case 'suspend':
        return 'The seller stops trading immediately. Their listings come down and no new order can be placed with them. Orders already in flight are unaffected.';
      case 'offboard':
        return 'The seller leaves the platform. This is the end of the relationship, and settlements still owed are unaffected.';
      case 'return':
        return 'The application goes back to the seller. The reason is what they read, and is the only thing that tells them what to fix.';
      case 'activate':
        return 'The seller may trade. Their listings become buyable as soon as they publish them.';
      default:
        return 'The application moves on to the next stage.';
    }
  });

  constructor() {
    this.load();
    this.loadReadiness();
    this.loadPlans();
    this.loadStaff();
  }

  // ---- The onboarding machine -------------------------------------------------------------------

  protected runTransition(reason: string): void {
    const transition = this.pending();
    if (!transition || this.busy()) return;

    this.busy.set(true);
    this.actionError.set(null);

    const note = reason.trim() || null;
    const request = {
      submit: () => this.vendors.submit(this.id, note),
      approve: () => this.vendors.approve(this.id, note),
      activate: () => this.vendors.activate(this.id, note),
      return: () => this.vendors.return_(this.id, note),
      suspend: () => this.vendors.suspend(this.id, note),
      offboard: () => this.vendors.offboard(this.id, note),
    }[transition.key]();

    request.subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.pending.set(null);
        this.vendor.set(saved);
        this.loadReadiness();
        this.toasts.success(`${transition.label} — done.`);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.pending.set(null);
        this.actionError.set(describeError(error, 'That was refused.'));
      },
    });
  }

  // ---- The legal record --------------------------------------------------------------------------

  /**
   * `PUT /vendors/{id}` (`UpdateVendorBusinessCommand`) has always accepted this from platform
   * staff, at any status — the page simply never had a form for it, only the read-out below.
   */
  protected saveLegal(): void {
    this.busy.set(true);
    this.actionError.set(null);

    this.vendors
      .updateVendor(this.id, {
        legalName: this.legalName(),
        businessType: this.businessType(),
        pan: this.pan() || null,
        gstin: this.gstin() || null,
        registeredAddress: {
          line1: this.addressLine1(),
          line2: this.addressLine2() || null,
          city: this.addressCity(),
          stateId: this.addressStateId(),
          pincode: this.addressPincode(),
        },
      })
      .subscribe({
        next: (saved) => {
          this.busy.set(false);
          this.vendor.set(saved);
          this.toasts.success('Legal record saved.');
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(describeError(error, 'That could not be saved.'));
        },
      });
  }

  // ---- Commission -------------------------------------------------------------------------------

  /**
   * "Left on the default" is a promise that the store's default plan applies — it is not supposed
   * to mean "unassigned". But `AssignCommissionPlanCommandHandler` treats a null id as exactly
   * that: it clears `CommissionPlanId`, and `VendorReadinessService` requires it non-null. A
   * vendor whose plan was auto-assigned at creation would fail readiness the moment somebody opened
   * this panel and clicked Save without changing anything. Resolve the blank option to the
   * platform's actual default plan id here, so the promise in the hint text is true.
   */
  protected assignPlan(): void {
    this.busy.set(true);
    this.actionError.set(null);

    const planId = this.commissionPlanId() || this.plans().find((plan) => plan.isDefault)?.id || null;

    this.vendors.assignCommissionPlan(this.id, planId).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.vendor.set(saved);
        this.toasts.success('Commission plan saved.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'The plan could not be assigned.'));
      },
    });
  }

  // ---- Staff ------------------------------------------------------------------------------------

  protected addStaff(): void {
    const userId = this.staffUserId().trim();
    if (!userId || this.busy()) return;

    this.busy.set(true);
    this.actionError.set(null);

    this.vendors
      .addStaff(this.id, { userId, isOwner: false, jobTitle: this.staffJobTitle().trim() || null })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.staffUserId.set('');
          this.staffJobTitle.set('');
          this.loadStaff();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.actionError.set(describeError(error, 'They could not be attached.'));
        },
      });
  }

  protected removeStaff(): void {
    const member = this.removingStaff();
    if (!member) return;

    this.busy.set(true);
    this.vendors.removeStaff(this.id, member.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.removingStaff.set(null);
        this.loadStaff();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.removingStaff.set(null);
        this.actionError.set(describeError(error, 'They could not be removed.'));
      },
    });
  }

  // ---- Loading ----------------------------------------------------------------------------------

  private load(): void {
    this.loading.set(true);
    this.vendors.vendor(this.id).subscribe({
      next: (vendor) => {
        this.loading.set(false);
        this.vendor.set(vendor);
        this.commissionPlanId.set(vendor.commissionPlanId ?? '');
        this.legalName.set(vendor.legalName);
        this.businessType.set(vendor.businessType);
        this.pan.set(vendor.pan ?? '');
        this.gstin.set(vendor.gstin ?? '');
        this.addressLine1.set(vendor.registeredAddress.line1);
        this.addressLine2.set(vendor.registeredAddress.line2 ?? '');
        this.addressCity.set(vendor.registeredAddress.city);
        this.addressStateId.set(vendor.registeredAddress.stateId);
        this.addressPincode.set(vendor.registeredAddress.pincode);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'That seller could not be loaded.'));
      },
    });
  }

  /** Re-asked after every panel's change, because every panel can close a blocker. */
  protected loadReadiness(): void {
    this.vendors.readiness(this.id).subscribe({
      next: (readiness) => this.readiness.set(readiness),
      // Not fatal: the panels below still say what they know.
      error: () => this.readiness.set(null),
    });
  }

  private loadPlans(): void {
    this.vendors.commissionPlans(false).subscribe({
      next: (plans) => this.plans.set(plans),
      error: () => this.plans.set([]),
    });
  }

  private loadStaff(): void {
    this.vendors.staff(this.id).subscribe({
      next: (staff) => this.staff.set(staff),
      error: () => this.staff.set([]),
    });
  }
}
