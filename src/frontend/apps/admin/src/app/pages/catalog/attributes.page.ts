import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AttributeBody,
  AttributeDataType,
  AttributeOptionPayload,
  AttributeResponse,
  AttributeSetResponse,
  CatalogAdminService,
} from '@klarahome/data-access-admin';
import { ConfirmDialog, EntityDrawer, FormShell, PageHeader } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';
import { forkJoin } from 'rxjs';

import { describeError, fieldErrors } from '../../core/describe-error';

/** The data types, and what each one means to somebody choosing between them. */
const DATA_TYPES: readonly { readonly value: AttributeDataType; readonly label: string }[] = [
  { value: 'Text', label: 'Text — free words' },
  { value: 'Number', label: 'Number — a measurement' },
  { value: 'Boolean', label: 'Yes or no' },
  { value: 'Select', label: 'Select — one of a fixed list' },
  { value: 'MultiSelect', label: 'Multi-select — several of a fixed list' },
  { value: 'Date', label: 'Date' },
];

/**
 * The catalogue's vocabulary.
 *
 * An attribute is the platform's answer to "a rug has a pile height and a lamp does not": products
 * do not have a fixed set of columns, they have typed attributes, and this is where those are
 * declared. Three flags on each one decide what it *does*, and they are three genuinely different
 * questions:
 *
 *  - **Variant-defining** — may this be a variant axis? Only `Select` and `MultiSelect` can be,
 *    because an axis has to enumerate its values; a free-text axis produces a variant per typo.
 *    The form enforces that by disabling the checkbox rather than by letting the API refuse it.
 *  - **Filterable** — does it appear as a facet on a category page? That has a real cost: every
 *    filterable attribute is a facet the search index counts on every query (Step 19).
 *  - **Searchable** — do its values feed the full-text vector?
 *
 * The options editor is the part that has to be careful. An option already used by a product
 * cannot simply vanish, so an existing option keeps its `id` and is edited in place; only a new
 * one is sent with a null id. Removing one is left to the API to refuse if it is in use.
 */
@Component({
  selector: 'kh-attributes-page',
  imports: [
    Alert,
    Badge,
    Button,
    Checkbox,
    ConfirmDialog,
    Control,
    EntityDrawer,
    Field,
    FormShell,
    Icon,
    PageHeader,
    Skeleton,
  ],
  template: `
    <kh-page-header
      heading="Attributes"
      description="What the catalogue can say about a product, and which of those a shopper can filter by."
    >
      <button khButton type="button" variant="primary" (click)="startCreate()">
        <kh-icon name="plus" size="sm" />
        New attribute
      </button>
    </kh-page-header>

    @if (error(); as message) {
      <kh-alert tone="danger" heading="That did not work" [dismissible]="true">{{ message }}</kh-alert>
    }

    @if (loading()) {
      <kh-skeleton height="16rem" />
    } @else {
      <section class="panel">
        <h2>Attributes</h2>
        <table>
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Code</th>
              <th scope="col">Type</th>
              <th scope="col">Used for</th>
              <th scope="col" class="numeric">Options</th>
              <th scope="col"><span class="sr-only">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (attribute of attributes(); track attribute.id) {
              <tr>
                <td>{{ attribute.name }}{{ attribute.unit ? ' (' + attribute.unit + ')' : '' }}</td>
                <td>
                  <code>{{ attribute.code }}</code>
                </td>
                <td>{{ attribute.dataType }}</td>
                <td class="flags">
                  @if (attribute.isVariantDefining) {
                    <kh-badge tone="primary">Variant axis</kh-badge>
                  }
                  @if (attribute.isFilterable) {
                    <kh-badge tone="info">Filter</kh-badge>
                  }
                  @if (attribute.isSearchable) {
                    <kh-badge tone="info">Search</kh-badge>
                  }
                  @if (attribute.isRequired) {
                    <kh-badge tone="warning">Required</kh-badge>
                  }
                </td>
                <td class="numeric">{{ attribute.options.length || '—' }}</td>
                <td>
                  <button khButton type="button" size="sm" (click)="startEdit(attribute)">Edit</button>
                </td>
              </tr>
            } @empty {
              <tr>
                <td colspan="6" class="hint">No attributes yet.</td>
              </tr>
            }
          </tbody>
        </table>
      </section>

      <section class="panel">
        <h2>Attribute sets</h2>
        <p class="hint">
          A set is the group of attributes a category asks for. Sets are assigned to a category on the
          categories screen.
        </p>
        <ul class="sets">
          @for (set of attributeSets(); track set.id) {
            <li>
              <strong>{{ set.name }}</strong>
              <span class="hint">{{ set.attributes.length }} attributes</span>
            </li>
          } @empty {
            <li class="hint">No sets yet.</li>
          }
        </ul>
      </section>
    }

    @if (drawerOpen()) {
      <kh-entity-drawer
        [heading]="editing() ? 'Edit attribute' : 'New attribute'"
        [subtitle]="editing()?.code ?? null"
        (closed)="drawerOpen.set(false)"
      >
        <kh-form-shell
          heading="Attribute"
          [summary]="summary()"
          [saving]="saving()"
          [dirty]="true"
          [submitLabel]="editing() ? 'Save' : 'Create'"
          (submitted)="save()"
          (cancelled)="drawerOpen.set(false)"
        >
          <kh-field label="Name" for="attribute-name" [error]="form.fields.name.error()">
            <input
              khControl
              id="attribute-name"
              type="text"
              maxlength="120"
              [value]="form.fields.name.value()"
              (input)="form.fields.name.set($any($event.target).value)"
              (touched)="form.fields.name.markTouched()"
            />
          </kh-field>

          <kh-field
            label="Code"
            for="attribute-code"
            [optional]="true"
            hint="How the API names it. Left blank, it is made from the name. It cannot be changed later."
          >
            <input
              khControl
              id="attribute-code"
              type="text"
              [disabled]="!!editing()"
              [value]="form.fields.code.value()"
              (input)="form.fields.code.set($any($event.target).value)"
            />
          </kh-field>

          <kh-field label="Type" for="attribute-type">
            <select
              khControl
              id="attribute-type"
              [value]="dataType()"
              (change)="setDataType($any($event.target).value)"
            >
              @for (type of dataTypes; track type.value) {
                <option [value]="type.value">{{ type.label }}</option>
              }
            </select>
          </kh-field>

          <kh-field label="Unit" for="attribute-unit" [optional]="true" hint="“cm”, “kg”, “W”.">
            <input
              khControl
              id="attribute-unit"
              type="text"
              maxlength="20"
              [value]="form.fields.unit.value()"
              (input)="form.fields.unit.set($any($event.target).value)"
            />
          </kh-field>

          <kh-checkbox
            label="Can be a variant axis"
            [description]="
              canBeAxis()
                ? 'Products can have a variant per value — a size, a colour.'
                : 'Only a select or multi-select can be an axis: an axis has to have a fixed list of values.'
            "
            inputId="attribute-axis"
            [disabled]="!canBeAxis()"
            [checked]="isVariantDefining()"
            (checkedChange)="isVariantDefining.set($event)"
          />

          <kh-checkbox
            label="Shoppers can filter by it"
            description="Adds a facet to category and search pages. Each one is counted on every query."
            inputId="attribute-filterable"
            [checked]="isFilterable()"
            (checkedChange)="isFilterable.set($event)"
          />

          <kh-checkbox
            label="Its values are searchable"
            inputId="attribute-searchable"
            [checked]="isSearchable()"
            (checkedChange)="isSearchable.set($event)"
          />

          <kh-checkbox
            label="Every product must have a value"
            inputId="attribute-required"
            [checked]="isRequired()"
            (checkedChange)="isRequired.set($event)"
          />

          @if (needsOptions()) {
            <fieldset>
              <legend>Options</legend>
              <p class="hint">
                The values this attribute can take. An option already used by a product cannot be removed —
                the API will say so.
              </p>

              @for (option of options(); track $index; let index = $index) {
                <div class="option-row">
                  <kh-field label="Label" [for]="'option-label-' + index">
                    <input
                      khControl
                      [id]="'option-label-' + index"
                      type="text"
                      [value]="option.label"
                      (input)="setOption(index, 'label', $any($event.target).value)"
                    />
                  </kh-field>
                  <kh-field label="Value" [for]="'option-value-' + index">
                    <input
                      khControl
                      [id]="'option-value-' + index"
                      type="text"
                      [value]="option.value"
                      (input)="setOption(index, 'value', $any($event.target).value)"
                    />
                  </kh-field>
                  <kh-field label="Swatch" [for]="'option-swatch-' + index" [optional]="true">
                    <input
                      khControl
                      [id]="'option-swatch-' + index"
                      type="text"
                      placeholder="#RRGGBB"
                      [value]="option.swatchHex ?? ''"
                      (input)="setOption(index, 'swatchHex', $any($event.target).value)"
                    />
                  </kh-field>
                  <button
                    khButton
                    type="button"
                    size="sm"
                    variant="danger"
                    aria-label="Remove option"
                    (click)="removeOption(index)"
                  >
                    <kh-icon name="trash" size="sm" />
                  </button>
                </div>
              }

              <button khButton type="button" size="sm" (click)="addOption()">
                <kh-icon name="plus" size="sm" />
                Add an option
              </button>
            </fieldset>
          }
        </kh-form-shell>

        <div slot="footer">
          @if (editing()) {
            <button khButton type="button" size="sm" variant="danger" (click)="deleting.set(true)">
              Delete
            </button>
          }
        </div>
      </kh-entity-drawer>
    }

    <kh-confirm-dialog
      [open]="deleting()"
      heading="Delete this attribute"
      message="Products that carry a value for it have to be edited first. This cannot be undone."
      confirmLabel="Delete"
      [confirmPhrase]="editing()?.name ?? null"
      [busy]="saving()"
      (confirmed)="remove()"
      (cancelled)="deleting.set(false)"
    />
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .panel {
      margin-block-end: var(--space-5);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .panel h2 {
      margin: 0 0 var(--space-3);
      font-size: var(--text-lg);
    }

    table {
      inline-size: 100%;
      border-collapse: collapse;
      font-size: var(--text-sm);
    }

    th,
    td {
      padding: var(--space-2);
      border-block-end: 1px solid var(--color-border);
      text-align: start;
      vertical-align: top;
    }

    .numeric {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }

    .flags {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
    }

    .hint {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .sets {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .sets li {
      display: flex;
      gap: var(--space-2);
      align-items: baseline;
      padding-block: var(--space-1);
    }

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

    .option-row {
      display: grid;
      grid-template-columns: 1fr 1fr 8rem auto;
      gap: var(--space-2);
      align-items: start;
    }

    .sr-only {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AttributesPage {
  private readonly catalog = inject(CatalogAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly dataTypes = DATA_TYPES;

  protected readonly attributes = signal<readonly AttributeResponse[]>([]);
  protected readonly attributeSets = signal<readonly AttributeSetResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly drawerOpen = signal(false);
  protected readonly deleting = signal(false);
  protected readonly editing = signal<AttributeResponse | null>(null);

  protected readonly dataType = signal<AttributeDataType>('Text');
  protected readonly isVariantDefining = signal(false);
  protected readonly isFilterable = signal(false);
  protected readonly isSearchable = signal(false);
  protected readonly isRequired = signal(false);
  protected readonly options = signal<readonly AttributeOptionPayload[]>([]);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('The name')], this.submitted),
    code: formField('', [], this.submitted),
    unit: formField('', [], this.submitted),
  });

  constructor() {
    this.load();
  }

  protected canBeAxis(): boolean {
    return this.dataType() === 'Select' || this.dataType() === 'MultiSelect';
  }

  protected needsOptions(): boolean {
    return this.canBeAxis();
  }

  protected setDataType(value: AttributeDataType): void {
    this.dataType.set(value);
    // A type that cannot enumerate its values cannot be an axis, so the flag is cleared rather
    // than left set and quietly refused on save.
    if (!this.canBeAxis()) this.isVariantDefining.set(false);
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.dataType.set('Text');
    this.isVariantDefining.set(false);
    this.isFilterable.set(false);
    this.isSearchable.set(false);
    this.isRequired.set(false);
    this.options.set([]);
    this.summary.set([]);
    this.form.reset({ name: '', code: '', unit: '' });
    this.drawerOpen.set(true);
  }

  protected startEdit(attribute: AttributeResponse): void {
    this.editing.set(attribute);
    this.dataType.set(attribute.dataType);
    this.isVariantDefining.set(attribute.isVariantDefining);
    this.isFilterable.set(attribute.isFilterable);
    this.isSearchable.set(attribute.isSearchable);
    this.isRequired.set(attribute.isRequired);
    // Existing options keep their ids: an option a product already points at must survive a save.
    this.options.set(
      attribute.options.map((option) => ({
        id: option.id,
        value: option.value,
        label: option.label,
        swatchHex: option.swatchHex,
        position: option.position,
      })),
    );
    this.summary.set([]);
    this.form.reset({ name: attribute.name, code: attribute.code, unit: attribute.unit ?? '' });
    this.drawerOpen.set(true);
  }

  protected addOption(): void {
    this.options.update((current) => [
      ...current,
      { id: null, value: '', label: '', swatchHex: null, position: current.length },
    ]);
  }

  protected setOption(index: number, field: 'label' | 'value' | 'swatchHex', value: string): void {
    this.options.update((current) =>
      current.map((option, at) =>
        at === index ? { ...option, [field]: field === 'swatchHex' ? value || null : value } : option,
      ),
    );
  }

  protected removeOption(index: number): void {
    this.options.update((current) =>
      current.filter((_option, at) => at !== index).map((option, position) => ({ ...option, position })),
    );
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    const body: AttributeBody = {
      code: values.code || null,
      name: values.name,
      dataType: this.dataType(),
      unit: values.unit || null,
      isVariantDefining: this.isVariantDefining(),
      isFilterable: this.isFilterable(),
      isSearchable: this.isSearchable(),
      isRequired: this.isRequired(),
      position: this.editing()?.position ?? this.attributes().length,
      options: this.needsOptions() ? this.options().filter((option) => option.label && option.value) : null,
    };

    this.saving.set(true);
    this.summary.set([]);

    const attribute = this.editing();
    const request = attribute
      ? this.catalog.updateAttribute(attribute.id, body)
      : this.catalog.createAttribute(body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.drawerOpen.set(false);
        this.toasts.success(attribute ? 'Attribute saved.' : 'Attribute created.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const errors = fieldErrors(error);
        this.summary.set(
          errors ? this.form.applyServerErrors(errors) : [describeError(error, 'It could not be saved.')],
        );
      },
    });
  }

  protected remove(): void {
    const attribute = this.editing();
    if (!attribute) return;

    this.saving.set(true);
    this.catalog.deleteAttribute(attribute.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleting.set(false);
        this.drawerOpen.set(false);
        this.toasts.success('Attribute deleted.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.deleting.set(false);
        this.summary.set([describeError(error, 'That attribute could not be deleted.')]);
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    forkJoin({ attributes: this.catalog.attributes(), sets: this.catalog.attributeSets() }).subscribe({
      next: (result) => {
        this.loading.set(false);
        this.attributes.set(result.attributes);
        this.attributeSets.set(result.sets);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(error, 'The attributes could not be loaded.'));
      },
    });
  }
}
