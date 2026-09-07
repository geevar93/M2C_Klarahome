import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import {
  PickupLocationBody,
  PickupLocationResponse,
  VendorsAdminService,
} from '@klarahome/data-access-admin';
import { ConfirmDialog } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, pincode, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';

/**
 * A seller's pickup addresses.
 *
 * Shared between the platform's seller record and a seller's own onboarding, because it is the
 * same job seen from two sides — a manager fixing an address on the phone and a seller entering
 * their warehouse do exactly the same thing to exactly the same rows, and two copies of this form
 * would be two places for the courier code to be missed.
 *
 * **This is where a parcel is collected from, so it is part of shipping rather than part of a
 * profile.** `courierLocationCode` is the courier's own registration of the address (Step 16A);
 * it is shown and never edited, because it is issued by Shiprocket rather than typed — an address
 * without one has not been registered with the courier yet and a booking against it will fail.
 * Saying so on the row is the difference between a seller who fixes it and one who discovers it at
 * the first dispatch.
 *
 * The state is asked for as a `stateId` because that is what the API takes and there is no admin
 * endpoint that lists the platform's states — the same gap the Step 27 warehouse row records.
 */
@Component({
  selector: 'kh-pickup-locations-panel',
  imports: [Alert, Badge, Button, Checkbox, ConfirmDialog, Control, Field, Icon, Skeleton],
  template: `
    <section class="panel">
      <header>
        <h2>Pickup addresses</h2>
        @if (canManage()) {
          <button khButton type="button" size="sm" (click)="startCreate()">
            <kh-icon name="plus" size="sm" />
            Add one
          </button>
        }
      </header>

      <p class="hint">Where a courier collects. A parcel cannot be booked without one.</p>

      @if (error(); as message) {
        <kh-alert tone="danger" heading="Pickup addresses">{{ message }}</kh-alert>
      }

      @if (loading()) {
        <kh-skeleton height="6rem" />
      } @else if (locations().length === 0) {
        <p class="empty">No pickup address yet.</p>
      } @else {
        <ul>
          @for (location of locations(); track location.id) {
            <li>
              <div class="details">
                <span class="label">
                  {{ location.label }}
                  @if (location.isDefault) {
                    <kh-badge tone="primary">Default</kh-badge>
                  }
                  @if (!location.isActive) {
                    <kh-badge tone="neutral">Not in use</kh-badge>
                  }
                </span>
                <span class="note">
                  {{ location.line1 }}
                  @if (location.line2) {
                    , {{ location.line2 }}
                  }
                  , {{ location.city }}
                  {{ location.pincode }}
                </span>
                <span class="note">{{ location.contactName }} · {{ location.contactPhone }}</span>
                @if (location.courierLocationCode) {
                  <span class="note">Registered with the courier as {{ location.courierLocationCode }}</span>
                } @else {
                  <span class="note warn">Not registered with the courier — a booking here will fail</span>
                }
              </div>

              @if (canManage()) {
                <div class="actions">
                  <button khButton type="button" size="sm" variant="tertiary" (click)="startEdit(location)">
                    Edit
                  </button>
                  <button
                    khButton
                    type="button"
                    size="sm"
                    variant="tertiary"
                    [disabled]="busy()"
                    (click)="removing.set(location)"
                  >
                    Remove
                  </button>
                </div>
              }
            </li>
          }
        </ul>
      }

      @if (editorOpen()) {
        <div class="editor">
          <h3>{{ editing() ? 'Edit address' : 'New pickup address' }}</h3>

          @if (summary().length > 0) {
            <kh-alert tone="danger" heading="It could not be saved">
              <ul class="messages">
                @for (message of summary(); track message) {
                  <li>{{ message }}</li>
                }
              </ul>
            </kh-alert>
          }

          <div class="row">
            <kh-field
              label="Label"
              for="pickup-label"
              [error]="form.fields.label.error()"
              hint="Warehouse, Shop, Studio."
            >
              <input
                khControl
                id="pickup-label"
                type="text"
                maxlength="60"
                [value]="form.fields.label.value()"
                (input)="form.fields.label.set($any($event.target).value)"
                (touched)="form.fields.label.markTouched()"
              />
            </kh-field>
            <kh-field label="Contact name" for="pickup-contact" [error]="form.fields.contactName.error()">
              <input
                khControl
                id="pickup-contact"
                type="text"
                maxlength="120"
                [value]="form.fields.contactName.value()"
                (input)="form.fields.contactName.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Contact phone" for="pickup-phone" [error]="form.fields.contactPhone.error()">
              <input
                khControl
                id="pickup-phone"
                type="tel"
                [value]="form.fields.contactPhone.value()"
                (input)="form.fields.contactPhone.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <kh-field label="Address" for="pickup-line1" [error]="form.fields.line1.error()">
            <input
              khControl
              id="pickup-line1"
              type="text"
              [value]="form.fields.line1.value()"
              (input)="form.fields.line1.set($any($event.target).value)"
            />
          </kh-field>

          <div class="row">
            <kh-field label="Address line 2" for="pickup-line2" [optional]="true">
              <input
                khControl
                id="pickup-line2"
                type="text"
                [value]="form.fields.line2.value()"
                (input)="form.fields.line2.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Landmark" for="pickup-landmark" [optional]="true">
              <input
                khControl
                id="pickup-landmark"
                type="text"
                [value]="form.fields.landmark.value()"
                (input)="form.fields.landmark.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <div class="row">
            <kh-field label="City" for="pickup-city" [error]="form.fields.city.error()">
              <input
                khControl
                id="pickup-city"
                type="text"
                [value]="form.fields.city.value()"
                (input)="form.fields.city.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field
              label="State id"
              for="pickup-state"
              [error]="form.fields.stateId.error()"
              hint="The platform's identifier for the state."
            >
              <input
                khControl
                id="pickup-state"
                type="text"
                [value]="form.fields.stateId.value()"
                (input)="form.fields.stateId.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="PIN code" for="pickup-pincode" [error]="form.fields.pincode.error()">
              <input
                khControl
                khNumeric
                id="pickup-pincode"
                type="text"
                inputmode="numeric"
                maxlength="6"
                [value]="form.fields.pincode.value()"
                (input)="form.fields.pincode.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <kh-checkbox
            label="In use"
            inputId="pickup-active"
            [checked]="isActive()"
            (checkedChange)="isActive.set($event)"
          />
          <kh-checkbox
            label="Make this the default pickup address"
            inputId="pickup-default"
            [checked]="makeDefault()"
            (checkedChange)="makeDefault.set($event)"
          />

          <div class="actions">
            <button khButton type="button" variant="tertiary" (click)="editorOpen.set(false)">Cancel</button>
            <button khButton type="button" variant="primary" [disabled]="busy()" (click)="save()">
              {{ busy() ? 'Saving…' : 'Save address' }}
            </button>
          </div>
        </div>
      }
    </section>

    <kh-confirm-dialog
      [open]="removing() !== null"
      heading="Remove this pickup address"
      message="Any parcel already booked against it keeps the address it was booked with."
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
      margin: 0 0 var(--space-3);
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

    .details {
      min-inline-size: 0;
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
      color: var(--color-warning);
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
export class PickupLocationsPanel {
  private readonly vendors = inject(VendorsAdminService);
  private readonly toasts = inject(ToastService);

  readonly vendorId = input.required<string>();
  /** False for a read-only viewer — the API decides, and this only stops offering the controls. */
  readonly canManage = input(true);
  /** Fired after any change, so a parent showing readiness can re-ask for it. */
  readonly changed = output<void>();

  protected readonly locations = signal<readonly PickupLocationResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly editorOpen = signal(false);
  protected readonly editing = signal<PickupLocationResponse | null>(null);
  protected readonly removing = signal<PickupLocationResponse | null>(null);
  protected readonly isActive = signal(true);
  protected readonly makeDefault = signal(false);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    label: formField('', [required('A label')], this.submitted),
    contactName: formField('', [required('A contact name')], this.submitted),
    contactPhone: formField('', [required('A phone number')], this.submitted),
    line1: formField('', [required('An address')], this.submitted),
    line2: formField('', [], this.submitted),
    landmark: formField('', [], this.submitted),
    city: formField('', [required('A city')], this.submitted),
    stateId: formField('', [required('A state')], this.submitted),
    pincode: formField('', [required('A PIN code'), pincode], this.submitted),
  });

  constructor() {
    // The id arrives as an input, so the fetch belongs in an effect rather than a constructor call.
    effect(() => {
      const id = this.vendorId();
      if (id) this.load(id);
    });
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.isActive.set(true);
    this.makeDefault.set(this.locations().length === 0);
    this.summary.set([]);
    this.form.reset({
      label: '',
      contactName: '',
      contactPhone: '',
      line1: '',
      line2: '',
      landmark: '',
      city: '',
      stateId: '',
      pincode: '',
    });
    this.editorOpen.set(true);
  }

  protected startEdit(location: PickupLocationResponse): void {
    this.editing.set(location);
    this.isActive.set(location.isActive);
    this.makeDefault.set(location.isDefault);
    this.summary.set([]);
    this.form.reset({
      label: location.label,
      contactName: location.contactName,
      contactPhone: location.contactPhone,
      line1: location.line1,
      line2: location.line2 ?? '',
      landmark: location.landmark ?? '',
      city: location.city,
      stateId: location.stateId,
      pincode: location.pincode,
    });
    this.editorOpen.set(true);
  }

  protected save(): void {
    if (!this.form.submit() || this.busy()) return;

    const values = this.form.values();
    const body: PickupLocationBody = {
      label: values.label,
      contactName: values.contactName,
      contactPhone: values.contactPhone,
      line1: values.line1,
      line2: values.line2 || null,
      landmark: values.landmark || null,
      city: values.city,
      stateId: values.stateId,
      pincode: values.pincode,
      isActive: this.isActive(),
      makeDefault: this.makeDefault(),
    };

    this.busy.set(true);
    this.summary.set([]);

    const existing = this.editing();
    const id = this.vendorId();
    const request = existing
      ? this.vendors.updatePickupLocation(id, existing.id, body)
      : this.vendors.addPickupLocation(id, body);

    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.editorOpen.set(false);
        this.toasts.success(existing ? 'Address saved.' : 'Address added.');
        this.load(id);
        this.changed.emit();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        const errors = fieldErrors(error);
        this.summary.set(
          errors ? this.form.applyServerErrors(errors) : [describeError(error, 'It could not be saved.')],
        );
      },
    });
  }

  protected remove(): void {
    const location = this.removing();
    if (!location) return;

    this.busy.set(true);
    const id = this.vendorId();
    this.vendors.removePickupLocation(id, location.id).subscribe({
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

    this.vendors.pickupLocations(vendorId).subscribe({
      next: (locations) => {
        this.loading.set(false);
        this.locations.set(locations);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(error, 'They could not be loaded.'));
      },
    });
  }
}
