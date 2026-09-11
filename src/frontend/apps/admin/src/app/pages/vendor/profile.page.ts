import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { MediaFileResponse, VendorResponse, VendorsAdminService } from '@klarahome/data-access-admin';
import { FormShell, PageHeader, StatusBadge } from '@klarahome/ui-admin';
import { Alert, Button, Checkbox, Control, Field, ProductImage, Skeleton } from '@klarahome/ui-primitives';
import { ImageUrls, ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { MediaPicker } from '../catalog/media-picker';

/**
 * A seller's shopfront and the promises attached to it.
 *
 * **Two forms, because they are two different records with two different consequences.** The
 * profile is what a shopper sees — the trading name, the story, the logo, the support contacts —
 * and changing it changes a page. Operations are the promises the platform holds a seller to: the
 * dispatch SLA that decides when an order is late, and the return policy that decides what a
 * customer may send back. Saving them separately is deliberate: somebody fixing a typo in their
 * "about" text should not be re-submitting their returns policy.
 *
 * **The legal record is not editable here.** Legal name, constitution, PAN and GSTIN are what the
 * platform verified, and a seller changing them unilaterally would invalidate the verification.
 * They are shown, and changing them is a conversation with the store.
 *
 * **The dispatch SLA is a promise, not a preference.** It is what the fulfilment queue measures
 * against, so the field says what it costs rather than presenting a number in a box.
 */
@Component({
  selector: 'kh-vendor-profile-page',
  imports: [
    Alert,
    Button,
    Checkbox,
    Control,
    Field,
    FormShell,
    MediaPicker,
    PageHeader,
    ProductImage,
    Skeleton,
    StatusBadge,
  ],
  template: `
    <kh-page-header heading="Your seller profile" description="What shoppers see, and what you promise them.">
      @if (vendor(); as current) {
        <kh-status-badge [status]="current.status" />
      }
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="Your seller account could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="20rem" />
    } @else if (vendor(); as current) {
      <div class="layout">
        <kh-form-shell
          heading="Your shopfront"
          description="What a shopper sees on your seller page and beside your listings."
          [summary]="profileSummary()"
          [saving]="savingProfile()"
          [dirty]="true"
          submitLabel="Save shopfront"
          [cancelLabel]="'Undo changes'"
          (submitted)="saveProfile()"
          (cancelled)="fill(current)"
        >
          <kh-field label="Trading name" for="profile-name" [error]="profileForm.fields.displayName.error()">
            <input
              khControl
              id="profile-name"
              type="text"
              maxlength="200"
              [value]="profileForm.fields.displayName.value()"
              (input)="profileForm.fields.displayName.set($any($event.target).value)"
              (touched)="profileForm.fields.displayName.markTouched()"
            />
          </kh-field>

          <kh-field label="About your shop" for="profile-about" [optional]="true">
            <textarea
              khControl
              id="profile-about"
              rows="5"
              [value]="profileForm.fields.about.value()"
              (input)="profileForm.fields.about.set($any($event.target).value)"
            ></textarea>
          </kh-field>

          <div class="images">
            <div class="image">
              <span class="image-label">Logo</span>
              <kh-product-image [source]="logoSource()" placeholder="No logo" />
              <button khButton type="button" size="sm" (click)="openPicker('logo')">Choose</button>
            </div>
            <div class="image">
              <span class="image-label">Banner</span>
              <kh-product-image [source]="bannerSource()" placeholder="No banner" />
              <button khButton type="button" size="sm" (click)="openPicker('banner')">Choose</button>
            </div>
          </div>

          <div class="row">
            <kh-field label="Support email" for="profile-email" [optional]="true">
              <input
                khControl
                id="profile-email"
                type="email"
                [value]="profileForm.fields.supportEmail.value()"
                (input)="profileForm.fields.supportEmail.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Support phone" for="profile-phone" [optional]="true">
              <input
                khControl
                id="profile-phone"
                type="tel"
                [value]="profileForm.fields.supportPhone.value()"
                (input)="profileForm.fields.supportPhone.set($any($event.target).value)"
              />
            </kh-field>
          </div>
        </kh-form-shell>

        <div class="column">
          <kh-form-shell
            heading="What you promise"
            description="The store holds you to these, and a shopper reads them before buying."
            [summary]="operationsSummary()"
            [saving]="savingOperations()"
            [dirty]="true"
            submitLabel="Save promises"
            [cancelLabel]="'Undo changes'"
            (submitted)="saveOperations()"
            (cancelled)="fill(current)"
          >
            <kh-field
              label="Dispatch within (hours)"
              for="ops-sla"
              hint="An order not handed to the courier within this is late, and the store's queue shows it as late."
            >
              <input
                khControl
                id="ops-sla"
                type="number"
                min="1"
                max="336"
                [value]="dispatchSlaHours()"
                (input)="dispatchSlaHours.set($any($event.target).value)"
              />
            </kh-field>

            <kh-checkbox
              label="Accept returns"
              inputId="ops-returns"
              [checked]="acceptsReturns()"
              (checkedChange)="acceptsReturns.set($event)"
            />

            @if (acceptsReturns()) {
              <kh-field label="Return window (days)" for="ops-window">
                <input
                  khControl
                  id="ops-window"
                  type="number"
                  min="0"
                  max="90"
                  [value]="returnWindowDays()"
                  (input)="returnWindowDays.set($any($event.target).value)"
                />
              </kh-field>

              <kh-checkbox
                label="Accept exchanges as well as refunds"
                inputId="ops-exchanges"
                [checked]="acceptsExchanges()"
                (checkedChange)="acceptsExchanges.set($event)"
              />

              <kh-checkbox
                label="The customer pays the return freight"
                description="Does not apply where the fault is yours — a damaged or wrong item is always collected at your cost."
                inputId="ops-freight"
                [checked]="customerPaysReturnShipping()"
                (checkedChange)="customerPaysReturnShipping.set($event)"
              />

              <kh-field label="Anything else a shopper should know" for="ops-notes" [optional]="true">
                <textarea
                  khControl
                  id="ops-notes"
                  rows="3"
                  [value]="returnNotes()"
                  (input)="returnNotes.set($any($event.target).value)"
                ></textarea>
              </kh-field>
            }

            <kh-checkbox
              label="Ship anywhere in India"
              description="Turn this off and set your regions under Getting set up."
              inputId="ops-all-india"
              [checked]="servesAllIndia()"
              (checkedChange)="servesAllIndia.set($event)"
            />
          </kh-form-shell>

          <section class="panel">
            <h2>Your legal record</h2>
            <p class="hint">
              Verified by the store. Changing any of it means talking to us — a unilateral change would
              invalidate the verification.
            </p>
            <dl>
              <dt>Legal name</dt>
              <dd>{{ current.legalName }}</dd>
              <dt>Constitution</dt>
              <dd>{{ current.businessType }}</dd>
              <dt>PAN</dt>
              <dd>{{ current.pan ?? '—' }}</dd>
              <dt>GSTIN</dt>
              <dd>{{ current.gstin ?? '—' }}</dd>
              <dt>Your seller code</dt>
              <dd>{{ current.code }}</dd>
              <dt>Your storefront page</dt>
              <dd>/sellers/{{ current.slug }}</dd>
            </dl>
          </section>
        </div>
      </div>
    }

    <kh-media-picker
      [open]="pickerFor() !== null"
      [multiple]="false"
      ownerType="Vendor"
      [ownerId]="vendor()?.id ?? null"
      (picked)="chooseImage($event)"
      (closed)="pickerFor.set(null)"
    />
  `,
  styles: `
    kh-alert {
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
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
    }

    .hint {
      margin: 0 0 var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    dl {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: var(--space-2) var(--space-4);
      margin: 0;
    }

    dt {
      color: var(--color-text-muted);
    }

    dd {
      margin: 0;
    }

    .row {
      display: flex;
      gap: var(--space-3);
    }

    .row > kh-field {
      flex: 1;
    }

    .images {
      display: flex;
      gap: var(--space-4);
      flex-wrap: wrap;
      margin-block: var(--space-4);
    }

    .image {
      flex: 1 1 10rem;
    }

    .image-label {
      display: block;
      margin-block-end: var(--space-1);
      font-weight: var(--weight-medium);
      font-size: var(--text-sm);
    }

    .image button {
      margin-block-start: var(--space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VendorProfilePage {
  private readonly vendors = inject(VendorsAdminService);
  private readonly images = inject(ImageUrls);
  private readonly toasts = inject(ToastService);

  protected readonly vendor = signal<VendorResponse | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);

  protected readonly savingProfile = signal(false);
  protected readonly savingOperations = signal(false);
  protected readonly profileSummary = signal<readonly string[]>([]);
  protected readonly operationsSummary = signal<readonly string[]>([]);

  protected readonly logoFileId = signal<string | null>(null);
  protected readonly bannerFileId = signal<string | null>(null);
  protected readonly pickerFor = signal<'logo' | 'banner' | null>(null);

  protected readonly dispatchSlaHours = signal('24');
  protected readonly acceptsReturns = signal(true);
  protected readonly returnWindowDays = signal('7');
  protected readonly acceptsExchanges = signal(false);
  protected readonly customerPaysReturnShipping = signal(false);
  protected readonly returnNotes = signal('');
  protected readonly servesAllIndia = signal(true);

  private readonly submitted = signal(false);

  protected readonly profileForm = formGroup(this.submitted, {
    displayName: formField('', [required('A trading name')], this.submitted),
    about: formField('', [], this.submitted),
    supportEmail: formField('', [], this.submitted),
    supportPhone: formField('', [], this.submitted),
  });

  protected readonly logoSource = computed(() =>
    this.images.sourceForImage({ url: null, fileId: this.logoFileId() }, 'Shop logo'),
  );

  protected readonly bannerSource = computed(() =>
    this.images.sourceForImage({ url: null, fileId: this.bannerFileId() }, 'Shop banner'),
  );

  constructor() {
    this.load();
  }

  protected openPicker(which: 'logo' | 'banner'): void {
    this.pickerFor.set(which);
  }

  protected chooseImage(files: readonly MediaFileResponse[]): void {
    const which = this.pickerFor();
    this.pickerFor.set(null);
    const file = files[0];
    if (!file || !which) return;

    if (which === 'logo') this.logoFileId.set(file.id);
    else this.bannerFileId.set(file.id);
  }

  protected saveProfile(): void {
    const current = this.vendor();
    if (!current || !this.profileForm.submit() || this.savingProfile()) return;

    const values = this.profileForm.values();
    this.savingProfile.set(true);
    this.profileSummary.set([]);

    this.vendors
      .updateProfile(current.id, {
        displayName: values.displayName,
        about: values.about || null,
        logoFileId: this.logoFileId(),
        bannerFileId: this.bannerFileId(),
        supportEmail: values.supportEmail || null,
        supportPhone: values.supportPhone || null,
      })
      .subscribe({
        next: (saved) => {
          this.savingProfile.set(false);
          this.vendor.set(saved);
          this.toasts.success('Shopfront saved.');
        },
        error: (error: unknown) => {
          this.savingProfile.set(false);
          const errors = fieldErrors(error);
          this.profileSummary.set(
            errors
              ? this.profileForm.applyServerErrors(errors)
              : [describeError(error, 'It could not be saved.')],
          );
        },
      });
  }

  protected saveOperations(): void {
    const current = this.vendor();
    if (!current || this.savingOperations()) return;

    this.savingOperations.set(true);
    this.operationsSummary.set([]);

    this.vendors
      .updateOperations(current.id, {
        dispatchSlaHours: Math.max(1, Number(this.dispatchSlaHours()) || 24),
        returnPolicy: {
          acceptsReturns: this.acceptsReturns(),
          windowDays: Math.max(0, Number(this.returnWindowDays()) || 0),
          acceptsExchanges: this.acceptsExchanges(),
          customerPaysReturnShipping: this.customerPaysReturnShipping(),
          notes: this.returnNotes() || null,
        },
        servesAllIndia: this.servesAllIndia(),
      })
      .subscribe({
        next: (saved) => {
          this.savingOperations.set(false);
          this.vendor.set(saved);
          this.toasts.success('Promises saved.');
        },
        error: (error: unknown) => {
          this.savingOperations.set(false);
          this.operationsSummary.set([describeError(error, 'They could not be saved.')]);
        },
      });
  }

  protected fill(vendor: VendorResponse): void {
    this.profileForm.reset({
      displayName: vendor.displayName,
      about: vendor.about ?? '',
      supportEmail: vendor.supportEmail ?? '',
      supportPhone: vendor.supportPhone ?? '',
    });
    this.logoFileId.set(vendor.logoFileId);
    this.bannerFileId.set(vendor.bannerFileId);
    this.dispatchSlaHours.set(String(vendor.dispatchSlaHours));
    this.acceptsReturns.set(vendor.returnPolicy.acceptsReturns);
    this.returnWindowDays.set(String(vendor.returnPolicy.windowDays));
    this.acceptsExchanges.set(vendor.returnPolicy.acceptsExchanges);
    this.customerPaysReturnShipping.set(vendor.returnPolicy.customerPaysReturnShipping);
    this.returnNotes.set(vendor.returnPolicy.notes ?? '');
    this.servesAllIndia.set(vendor.servesAllIndia);
  }

  private load(): void {
    this.loading.set(true);
    this.vendors.mine().subscribe({
      next: (vendor) => {
        this.loading.set(false);
        this.vendor.set(vendor);
        this.fill(vendor);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'Your seller account could not be loaded.'));
      },
    });
  }
}
