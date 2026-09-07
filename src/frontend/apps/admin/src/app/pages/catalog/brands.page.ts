import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  BrandBody,
  BrandFilters,
  BrandResponse,
  CatalogAdminService,
  MediaFileResponse,
} from '@klarahome/data-access-admin';
import {
  CellTemplate,
  ConfirmDialog,
  DataTable,
  DataTableColumn,
  EntityDrawer,
  FilterBar,
  FilterValues,
  FormShell,
  PageHeader,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, ProductImage } from '@klarahome/ui-primitives';
import { ImageUrls, ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { MediaPicker } from './media-picker';

/**
 * Brands.
 *
 * A small screen, and the only thing in it worth explaining is what it does *not* do: there is no
 * merge. Two rows for the same manufacturer is the commonest mess a catalogue accumulates, and
 * fixing it means moving every product from one to the other and then deleting — which is a job
 * for an endpoint that can do it in one transaction, and there is not one. Offering a client-side
 * loop over products here would be a migration that can stop halfway.
 *
 * Deleting is offered because the API refuses a brand that still has products, so the destructive
 * case is already closed on the server. The typed confirmation is for the other one — a brand with
 * no products that somebody is about to spend an afternoon re-entering.
 */
@Component({
  selector: 'kh-brands-page',
  imports: [
    Alert,
    Badge,
    Button,
    CellTemplate,
    Checkbox,
    ConfirmDialog,
    Control,
    DataTable,
    EntityDrawer,
    Field,
    FilterBar,
    FormShell,
    Icon,
    MediaPicker,
    PageHeader,
    ProductImage,
  ],
  template: `
    <kh-page-header heading="Brands" description="Who makes the things in the catalogue.">
      <button khButton type="button" variant="primary" (click)="startCreate()">
        <kh-icon name="plus" size="sm" />
        New brand
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Brands could not be loaded">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Brands"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      exportMode="page"
      emptyMessage="No brand matches this search."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="[]"
        [values]="values()"
        searchLabel="Search brands"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="name" let-row>
        <button type="button" class="link" (click)="startEdit(row)">{{ row.name }}</button>
        <span class="slug">/{{ row.slug }}</span>
      </ng-template>

      <ng-template khCell="isActive" let-row>
        <kh-badge [tone]="row.isActive ? 'success' : 'warning'">
          {{ row.isActive ? 'Active' : 'Hidden' }}
        </kh-badge>
      </ng-template>
    </kh-data-table>

    @if (drawerOpen()) {
      <kh-entity-drawer
        [heading]="editing() ? 'Edit brand' : 'New brand'"
        [subtitle]="editing()?.slug ?? null"
        (closed)="drawerOpen.set(false)"
      >
        <kh-form-shell
          heading="Brand"
          [summary]="summary()"
          [saving]="saving()"
          [dirty]="true"
          [submitLabel]="editing() ? 'Save' : 'Create'"
          (submitted)="save()"
          (cancelled)="drawerOpen.set(false)"
        >
          <kh-field label="Name" for="brand-name" [error]="form.fields.name.error()">
            <input
              khControl
              id="brand-name"
              type="text"
              maxlength="120"
              [value]="form.fields.name.value()"
              (input)="form.fields.name.set($any($event.target).value)"
              (touched)="form.fields.name.markTouched()"
            />
          </kh-field>

          <kh-field
            label="URL slug"
            for="brand-slug"
            [optional]="true"
            hint="Left blank, it is made from the name."
          >
            <input
              khControl
              id="brand-slug"
              type="text"
              [value]="form.fields.slug.value()"
              (input)="form.fields.slug.set($any($event.target).value)"
            />
          </kh-field>

          <kh-field label="Description" for="brand-description" [optional]="true">
            <textarea
              khControl
              id="brand-description"
              rows="3"
              [value]="form.fields.description.value()"
              (input)="form.fields.description.set($any($event.target).value)"
            ></textarea>
          </kh-field>

          <div class="logo">
            <kh-product-image [source]="logoSource()" placeholder="No logo" />
            <div class="logo-actions">
              <button khButton type="button" size="sm" (click)="pickerOpen.set(true)">Choose a logo</button>
              @if (logoFileId()) {
                <button khButton type="button" size="sm" variant="tertiary" (click)="logoFileId.set(null)">
                  Remove
                </button>
              }
            </div>
          </div>

          <kh-checkbox
            label="Show this brand on the storefront"
            inputId="brand-active"
            [checked]="isActive()"
            (checkedChange)="isActive.set($event)"
          />
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

    <kh-media-picker
      [open]="pickerOpen()"
      [multiple]="false"
      ownerType="Brand"
      [ownerId]="editing()?.id ?? null"
      (picked)="setLogo($event)"
      (closed)="pickerOpen.set(false)"
    />

    <kh-confirm-dialog
      [open]="deleting()"
      heading="Delete this brand"
      message="Products still filed under it have to be moved first. This cannot be undone."
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

    .link {
      display: block;
      padding: 0;
      border: none;
      background: none;
      color: var(--color-link);
      font: inherit;
      font-weight: var(--weight-medium);
      text-align: start;
      cursor: pointer;
    }

    .slug {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .logo {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      margin-block-end: var(--space-4);
    }

    .logo kh-product-image {
      inline-size: 6rem;
    }

    .logo-actions {
      display: flex;
      gap: var(--space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BrandsPage {
  private readonly catalog = inject(CatalogAdminService);
  private readonly images = inject(ImageUrls);
  private readonly toasts = inject(ToastService);

  protected readonly list = this.catalog.brands();
  protected readonly values = signal<FilterValues>({});
  protected readonly saving = signal(false);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly drawerOpen = signal(false);
  protected readonly deleting = signal(false);
  protected readonly pickerOpen = signal(false);
  protected readonly editing = signal<BrandResponse | null>(null);
  protected readonly isActive = signal(true);
  protected readonly logoFileId = signal<string | null>(null);

  private readonly logoUrl = signal<string | null>(null);
  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('The name')], this.submitted),
    slug: formField('', [], this.submitted),
    description: formField('', [], this.submitted),
  });

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly logoSource = computed(() =>
    this.images.sourceForImage({ url: this.logoUrl(), fileId: this.logoFileId() }, 'Brand logo'),
  );

  protected readonly rowKey = (row: BrandResponse) => row.id;
  protected readonly rowLabel = (row: BrandResponse) => row.name;

  protected readonly columns: readonly DataTableColumn<BrandResponse>[] = [
    { key: 'name', label: 'Brand', kind: 'custom' },
    { key: 'isActive', label: 'Status', kind: 'custom', width: '8rem' },
    {
      key: 'description',
      label: 'Description',
      value: (row) => row.description,
      hiddenByDefault: true,
    },
  ];

  constructor() {
    this.list.load();
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: BrandFilters = { search: values['q'] };
    this.list.setFilters(filters);
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.isActive.set(true);
    this.logoFileId.set(null);
    this.logoUrl.set(null);
    this.summary.set([]);
    this.form.reset({ name: '', slug: '', description: '' });
    this.drawerOpen.set(true);
  }

  protected startEdit(brand: BrandResponse): void {
    this.editing.set(brand);
    this.isActive.set(brand.isActive);
    this.logoFileId.set(brand.logoFileId);
    this.logoUrl.set(null);
    this.summary.set([]);
    this.form.reset({ name: brand.name, slug: brand.slug, description: brand.description ?? '' });
    this.drawerOpen.set(true);
  }

  protected setLogo(files: readonly MediaFileResponse[]): void {
    this.pickerOpen.set(false);
    const file = files[0];
    if (!file) return;
    this.logoFileId.set(file.id);
    // Kept so the chosen logo renders before the brand is saved, when no resizer is configured.
    this.logoUrl.set(file.url);
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    const body: BrandBody = {
      name: values.name,
      slug: values.slug || null,
      description: values.description || null,
      logoFileId: this.logoFileId(),
      isActive: this.isActive(),
      seo: null,
    };

    this.saving.set(true);
    this.summary.set([]);

    const brand = this.editing();
    const request = brand ? this.catalog.updateBrand(brand.id, body) : this.catalog.createBrand(body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.drawerOpen.set(false);
        this.toasts.success(brand ? 'Brand saved.' : 'Brand created.');
        this.list.refresh();
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
    const brand = this.editing();
    if (!brand) return;

    this.saving.set(true);
    this.catalog.deleteBrand(brand.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleting.set(false);
        this.drawerOpen.set(false);
        this.toasts.success('Brand deleted.');
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.deleting.set(false);
        this.summary.set([describeError(error, 'That brand could not be deleted.')]);
      },
    });
  }
}
