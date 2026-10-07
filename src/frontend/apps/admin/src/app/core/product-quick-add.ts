import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { Router } from '@angular/router';
import { CatalogAdminService, CategoryNode, ProductBody } from '@klarahome/data-access-admin';
import { Button, Control, Drawer, Field } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from './describe-error';

/** One row of the category select: the tree flattened, indented by depth. */
interface CategoryOption {
  readonly id: string;
  readonly label: string;
}

/**
 * The quickest way to get a product started.
 *
 * The full product editor is the longest screen in the back office — ninety fields, a media list,
 * a variant table — and asking someone to face it to add one cushion cover is why the catalogue
 * grows slowly. This sheet asks the three things the API needs to make a draft: a name, a
 * category and a GST rate. It saves, and lands on the editor for that product with everything
 * else — photos, variants with their price and weight, compliance details — one screen away and
 * clearly optional until publishing.
 *
 * Three fields and not five, because the other two a merchant would expect here do not exist
 * until the product does: a photo is attached to a product id, and a price belongs to a variant,
 * which also needs a weight and dimensions the courier's live rate is quoted from. Asking for a
 * price here and dropping it on the floor would be worse than not asking.
 *
 * The same request the editor sends, with the same nulls where the editor has fields: one set of
 * rules, and a product made here is indistinguishable from one made there.
 */
@Component({
  selector: 'kh-product-quick-add',
  imports: [Button, Control, Drawer, Field],
  template: `
    <kh-drawer [open]="open()" side="bottom" label="Add a product" (closed)="requestClose()">
      <form class="sheet" (submit)="$event.preventDefault(); save()">
        <h2>Add a product</h2>
        <p class="lead">Just enough to make a draft. Photos, prices and stock come next, on the product's own page.</p>

        @if (summary().length > 0) {
          <ul class="summary" role="alert">
            @for (message of summary(); track message) {
              <li>{{ message }}</li>
            }
          </ul>
        }

        <kh-field label="Name" for="quick-product-name" [error]="form.fields.name.error()">
          <input
            khControl
            id="quick-product-name"
            type="text"
            maxlength="200"
            autocomplete="off"
            [value]="form.fields.name.value()"
            (input)="form.fields.name.set($any($event.target).value)"
            (blur)="form.fields.name.markTouched()"
          />
        </kh-field>

        <kh-field label="Category" for="quick-product-category" [error]="form.fields.categoryId.error()">
          <select
            khControl
            id="quick-product-category"
            [value]="form.fields.categoryId.value()"
            (change)="form.fields.categoryId.set($any($event.target).value)"
            (blur)="form.fields.categoryId.markTouched()"
          >
            <option value="">Choose a category</option>
            @for (option of categoryOptions(); track option.id) {
              <option [value]="option.id" [selected]="option.id === form.fields.categoryId.value()">
                {{ option.label }}
              </option>
            }
          </select>
        </kh-field>

        <kh-field
          label="GST rate (%)"
          for="quick-product-gst"
          hint="The slab this product is taxed in. 18 covers most home goods."
          [error]="form.fields.gstRate.error()"
        >
          <input
            khControl
            khNumeric
            id="quick-product-gst"
            type="number"
            inputmode="decimal"
            min="0"
            max="100"
            step="0.01"
            [value]="form.fields.gstRate.value()"
            (input)="form.fields.gstRate.set($any($event.target).value)"
            (blur)="form.fields.gstRate.markTouched()"
          />
        </kh-field>

        <div class="actions">
          <button khButton type="button" variant="tertiary" [disabled]="saving()" (click)="requestClose()">Cancel</button>
          <button khButton type="submit" variant="primary" [disabled]="saving()">
            {{ saving() ? 'Creating…' : 'Create draft' }}
          </button>
        </div>
      </form>
    </kh-drawer>
  `,
  styles: `
    .sheet {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
      padding: var(--space-4);
      padding-block-end: max(var(--space-4), env(safe-area-inset-bottom, 0px));
    }

    h2 {
      margin: 0;
      font-size: var(--text-lg);
    }

    .lead {
      margin: calc(-1 * var(--space-2)) 0 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .summary {
      margin: 0;
      padding: var(--space-3) var(--space-3) var(--space-3) var(--space-6);
      border: 1px solid var(--color-danger);
      border-radius: var(--radius-md);
      font-size: var(--text-sm);
      color: var(--color-danger);
    }

    .actions {
      display: flex;
      gap: var(--space-2);
      justify-content: flex-end;
      margin-block-start: var(--space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductQuickAdd {
  private readonly catalog = inject(CatalogAdminService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  readonly open = input(false);
  readonly closed = output<void>();

  protected readonly saving = signal(false);
  protected readonly summary = signal<readonly string[]>([]);
  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('The name')], this.submitted),
    categoryId: formField('', [required('A category')], this.submitted),
    gstRate: formField('18', [required('The GST rate')], this.submitted),
  });

  private readonly categories = signal<readonly CategoryNode[]>([]);
  private categoriesLoaded = false;

  protected readonly categoryOptions = computed<readonly CategoryOption[]>(() => flatten(this.categories(), 0));

  constructor() {
    // The tree is fetched the first time the sheet opens, not on every screen the shell renders:
    // most sessions never add a product, and the taxonomy is not small.
    effect(() => {
      if (!this.open() || this.categoriesLoaded) return;
      this.categoriesLoaded = true;
      this.catalog.categoryTree().subscribe({
        next: (tree) => this.categories.set(tree),
        error: () => {
          this.categoriesLoaded = false;
          this.summary.set(['The categories could not be loaded. Close and try again.']);
        },
      });
    });
  }

  protected requestClose(): void {
    if (this.saving()) return;
    this.closed.emit();
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    const body: ProductBody = {
      name: values.name.trim(),
      slug: null,
      categoryId: values.categoryId,
      brandId: null,
      vendorId: null,
      shortDescription: null,
      description: null,
      hsnCode: null,
      gstRate: Number(values.gstRate),
      countryOfOrigin: null,
      manufacturer: null,
      packer: null,
      importer: null,
      isReturnable: true,
      returnWindowDays: null,
      warranty: null,
      specifications: [],
      seo: null,
      attributes: [],
    };

    this.saving.set(true);
    this.summary.set([]);

    this.catalog.createProduct(body).subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.reset();
        this.closed.emit();
        this.toasts.success('Add photos and a variant with its price to publish it.', 'Draft created');
        void this.router.navigate(['/catalog/products', saved.id]);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const errors = fieldErrors(error);
        this.summary.set(
          errors ? this.form.applyServerErrors(errors) : [describeError(error, 'The product could not be created.')],
        );
      },
    });
  }

  private reset(): void {
    this.form.reset({ name: '', categoryId: '', gstRate: '18' });
    this.submitted.set(false);
    this.summary.set([]);
  }
}

function flatten(nodes: readonly CategoryNode[], depth: number): CategoryOption[] {
  return nodes.flatMap((node) => [
    { id: node.id, label: `${'— '.repeat(depth)}${node.name}` },
    ...flatten(node.children, depth + 1),
  ]);
}
