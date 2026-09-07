import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import {
  AttributeResponse,
  CatalogAdminService,
  MediaPayload,
  VariantBody,
  VariantResponse,
} from '@klarahome/data-access-admin';
import { EntityDrawer, FormShell } from '@klarahome/ui-admin';
import { Button, Checkbox, Control, Field } from '@klarahome/ui-primitives';
import { formGroup, formField, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { MediaManager } from './media-manager';

/**
 * One variant of a product.
 *
 * A drawer rather than a page, because a variant is never edited on its own: the question being
 * answered is always "how does this one differ from the other five", and taking the operator away
 * from the list to answer it loses the comparison.
 *
 * Two fields carry more weight than they look:
 *
 *  - **The dimensions and the weight are what the courier bills on.** Volumetric weight is
 *    `L × W × H ÷ 5000` and the courier charges the greater of that and the actual weight
 *    (Step 16), so a variant saved with zeroes is a parcel whose freight is wrong at every
 *    checkout that quotes it. They are required here for that reason and not because the API
 *    demands them.
 *  - **The options are the variant axis.** Only attributes marked variant-defining may appear, and
 *    they come from the API's own list rather than a hard-coded "colour and size" — the whole
 *    point of typed attributes (Step 10) is that a rug has a pile height and a lamp does not.
 */
@Component({
  selector: 'kh-variant-editor',
  imports: [Button, Checkbox, Control, EntityDrawer, Field, FormShell, MediaManager],
  template: `
    <kh-entity-drawer
      [heading]="editing() ? 'Edit variant' : 'New variant'"
      [subtitle]="editing()?.sku ?? 'A new size, colour or pack of this product'"
      (closed)="cancelled.emit()"
    >
      <kh-form-shell
        heading="Variant"
        [summary]="summary()"
        [saving]="saving()"
        [dirty]="true"
        [submitLabel]="editing() ? 'Save variant' : 'Add variant'"
        (submitted)="save()"
        (cancelled)="cancelled.emit()"
      >
        <kh-field
          label="SKU"
          for="variant-sku"
          hint="Left blank, the platform generates one."
          [optional]="true"
        >
          <input
            khControl
            id="variant-sku"
            type="text"
            [value]="form.fields.sku.value()"
            (input)="form.fields.sku.set($any($event.target).value)"
          />
        </kh-field>

        <kh-field
          label="Name suffix"
          for="variant-suffix"
          hint="“Large, Slate” — appended to the product name."
          [optional]="true"
        >
          <input
            khControl
            id="variant-suffix"
            type="text"
            [value]="form.fields.nameSuffix.value()"
            (input)="form.fields.nameSuffix.set($any($event.target).value)"
          />
        </kh-field>

        <div class="pair">
          <kh-field label="MRP (₹)" for="variant-mrp" [error]="form.fields.mrp.error()">
            <input
              khControl
              khNumeric
              id="variant-mrp"
              type="number"
              min="0"
              step="0.01"
              [value]="form.fields.mrp.value()"
              (input)="form.fields.mrp.set($any($event.target).value)"
              (touched)="form.fields.mrp.markTouched()"
            />
          </kh-field>

          <kh-field label="Barcode" for="variant-barcode" [optional]="true">
            <input
              khControl
              id="variant-barcode"
              type="text"
              [value]="form.fields.barcode.value()"
              (input)="form.fields.barcode.set($any($event.target).value)"
            />
          </kh-field>
        </div>

        <fieldset>
          <legend>Parcel</legend>
          <p class="hint">
            The courier charges the greater of the actual weight and length × width × height ÷ 5000. Both have
            to be right or every quote for this variant is wrong.
          </p>

          <div class="pair">
            <kh-field label="Weight (grams)" for="variant-weight" [error]="form.fields.weightGrams.error()">
              <input
                khControl
                khNumeric
                id="variant-weight"
                type="number"
                min="1"
                [value]="form.fields.weightGrams.value()"
                (input)="form.fields.weightGrams.set($any($event.target).value)"
                (touched)="form.fields.weightGrams.markTouched()"
              />
            </kh-field>

            <kh-field
              label="Net quantity"
              for="variant-net"
              hint="“500 g”, “2 pieces” — the legal declaration."
              [optional]="true"
            >
              <input
                khControl
                id="variant-net"
                type="text"
                [value]="form.fields.netQuantity.value()"
                (input)="form.fields.netQuantity.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <div class="triple">
            <kh-field label="Length (mm)" for="variant-length" [error]="form.fields.lengthMm.error()">
              <input
                khControl
                khNumeric
                id="variant-length"
                type="number"
                min="1"
                [value]="form.fields.lengthMm.value()"
                (input)="form.fields.lengthMm.set($any($event.target).value)"
                (touched)="form.fields.lengthMm.markTouched()"
              />
            </kh-field>
            <kh-field label="Width (mm)" for="variant-width" [error]="form.fields.widthMm.error()">
              <input
                khControl
                khNumeric
                id="variant-width"
                type="number"
                min="1"
                [value]="form.fields.widthMm.value()"
                (input)="form.fields.widthMm.set($any($event.target).value)"
                (touched)="form.fields.widthMm.markTouched()"
              />
            </kh-field>
            <kh-field label="Height (mm)" for="variant-height" [error]="form.fields.heightMm.error()">
              <input
                khControl
                khNumeric
                id="variant-height"
                type="number"
                min="1"
                [value]="form.fields.heightMm.value()"
                (input)="form.fields.heightMm.set($any($event.target).value)"
                (touched)="form.fields.heightMm.markTouched()"
              />
            </kh-field>
          </div>
        </fieldset>

        <fieldset>
          <legend>Shelf life</legend>
          <div class="pair">
            <kh-field label="Shelf life (days)" for="variant-shelf" [optional]="true">
              <input
                khControl
                khNumeric
                id="variant-shelf"
                type="number"
                min="0"
                [value]="form.fields.shelfLifeDays.value()"
                (input)="form.fields.shelfLifeDays.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Expires on" for="variant-expires" [optional]="true">
              <input
                khControl
                id="variant-expires"
                type="date"
                [value]="form.fields.expiresOn.value()"
                (input)="form.fields.expiresOn.set($any($event.target).value)"
              />
            </kh-field>
          </div>
        </fieldset>

        @if (axes().length > 0) {
          <fieldset>
            <legend>What makes this variant different</legend>
            @for (axis of axes(); track axis.id) {
              <kh-field [label]="axis.name" [for]="'variant-axis-' + axis.id" [optional]="true">
                <select
                  khControl
                  [id]="'variant-axis-' + axis.id"
                  [value]="optionFor(axis.id)"
                  (change)="setOption(axis.id, $any($event.target).value)"
                >
                  <option value="">Not set</option>
                  @for (option of axis.options; track option.id) {
                    <option [value]="option.id">{{ option.label }}</option>
                  }
                </select>
              </kh-field>
            }
          </fieldset>
        }

        <fieldset>
          <legend>Images</legend>
          <kh-media-manager
            [(media)]="media"
            idPrefix="variant"
            ownerType="Variant"
            [ownerId]="editing()?.id ?? null"
          />
        </fieldset>

        <kh-checkbox
          label="Show this variant first"
          description="The one the product page opens on."
          [checked]="isDefault()"
          inputId="variant-default"
          (checkedChange)="isDefault.set($event)"
        />
      </kh-form-shell>

      <div slot="footer">
        @if (editing(); as variant) {
          <button khButton type="button" size="sm" (click)="toggleActive(variant)" [disabled]="saving()">
            {{ variant.status === 'Active' ? 'Deactivate' : 'Activate' }}
          </button>
        }
      </div>
    </kh-entity-drawer>
  `,
  styles: `
    fieldset {
      margin: 0 0 var(--space-4);
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    legend {
      padding-inline: var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .hint {
      margin-block-start: 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .pair,
    .triple {
      display: grid;
      gap: var(--space-3);
    }

    @media (min-width: 40rem) {
      .pair {
        grid-template-columns: 1fr 1fr;
      }

      .triple {
        grid-template-columns: repeat(3, 1fr);
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class VariantEditor {
  private readonly catalog = inject(CatalogAdminService);

  readonly productId = input.required<string>();
  /** The variant being edited, or null for a new one. */
  readonly editing = input<VariantResponse | null>(null);
  /** Every variant-defining attribute, fetched once by the page above. */
  readonly axes = input<readonly AttributeResponse[]>([]);

  readonly saved = output<VariantResponse>();
  readonly cancelled = output<void>();

  protected readonly saving = signal(false);
  protected readonly summary = signal<readonly string[]>([]);
  protected readonly isDefault = signal(false);
  protected readonly media = signal<readonly MediaPayload[]>([]);

  private readonly submitted = signal(false);
  private readonly options = signal<Readonly<Record<string, string>>>({});

  protected readonly form = formGroup(this.submitted, {
    sku: formField('', [], this.submitted),
    barcode: formField('', [], this.submitted),
    nameSuffix: formField('', [], this.submitted),
    mrp: formField('', [required('The MRP')], this.submitted),
    netQuantity: formField('', [], this.submitted),
    shelfLifeDays: formField('', [], this.submitted),
    expiresOn: formField('', [], this.submitted),
    weightGrams: formField('', [required('The weight')], this.submitted),
    lengthMm: formField('', [required('The length')], this.submitted),
    widthMm: formField('', [required('The width')], this.submitted),
    heightMm: formField('', [required('The height')], this.submitted),
  });

  constructor() {
    // Reloaded whenever the drawer is pointed at a different variant, rather than in a constructor
    // that runs once — the page keeps one editor and re-targets it.
    effect(() => {
      const variant = this.editing();
      this.summary.set([]);
      this.form.reset({
        sku: variant?.sku ?? '',
        barcode: variant?.barcode ?? '',
        nameSuffix: variant?.nameSuffix ?? '',
        mrp: variant ? String(variant.mrp) : '',
        netQuantity: variant?.netQuantity ?? '',
        shelfLifeDays: variant?.shelfLifeDays === null ? '' : String(variant?.shelfLifeDays ?? ''),
        expiresOn: (variant?.expiresOn ?? '').slice(0, 10),
        weightGrams: variant ? String(variant.weightGrams) : '',
        lengthMm: variant ? String(variant.lengthMm) : '',
        widthMm: variant ? String(variant.widthMm) : '',
        heightMm: variant ? String(variant.heightMm) : '',
      });
      this.isDefault.set(variant?.isDefault ?? false);
      this.media.set(
        (variant?.media ?? []).map((item) => ({
          fileId: item.fileId,
          kind: item.kind,
          altText: item.altText,
          position: item.position,
        })),
      );
      this.options.set(
        Object.fromEntries(
          (variant?.options ?? [])
            .filter((option) => option.optionId !== null)
            .map((option) => [option.attributeId, option.optionId as string]),
        ),
      );
    });
  }

  protected optionFor(attributeId: string): string {
    return this.options()[attributeId] ?? '';
  }

  protected setOption(attributeId: string, optionId: string): void {
    this.options.update((current) => {
      const next = { ...current };
      if (optionId) next[attributeId] = optionId;
      else delete next[attributeId];
      return next;
    });
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    const body: VariantBody = {
      sku: values.sku || null,
      barcode: values.barcode || null,
      nameSuffix: values.nameSuffix || null,
      mrp: Number(values.mrp),
      netQuantity: values.netQuantity || null,
      shelfLifeDays: values.shelfLifeDays ? Number(values.shelfLifeDays) : null,
      expiresOn: values.expiresOn || null,
      weightGrams: Number(values.weightGrams),
      lengthMm: Number(values.lengthMm),
      widthMm: Number(values.widthMm),
      heightMm: Number(values.heightMm),
      position: this.editing()?.position ?? 0,
      isDefault: this.isDefault(),
      options: Object.entries(this.options()).map(([attributeId, optionId]) => ({ attributeId, optionId })),
      media: [...this.media()],
    };

    this.saving.set(true);
    const variant = this.editing();
    const request = variant
      ? this.catalog.updateVariant(variant.id, body)
      : this.catalog.createVariant(this.productId(), body);

    request.subscribe({
      next: (result) => {
        this.saving.set(false);
        this.saved.emit(result);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const errors = fieldErrors(error);
        this.summary.set(
          errors
            ? this.form.applyServerErrors(errors)
            : [describeError(error, 'The variant could not be saved.')],
        );
      },
    });
  }

  protected toggleActive(variant: VariantResponse): void {
    this.saving.set(true);
    const request =
      variant.status === 'Active'
        ? this.catalog.deactivateVariant(variant.id)
        : this.catalog.activateVariant(variant.id);

    request.subscribe({
      next: (result) => {
        this.saving.set(false);
        this.saved.emit(result);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.summary.set([describeError(error, 'That variant could not be changed.')]);
      },
    });
  }
}
