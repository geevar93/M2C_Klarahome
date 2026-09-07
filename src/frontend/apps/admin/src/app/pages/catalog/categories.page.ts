import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  AttributeSetResponse,
  CatalogAdminService,
  CategoryBody,
  CategoryNode,
  CategoryResponse,
} from '@klarahome/data-access-admin';
import { ConfirmDialog, EntityDrawer, FormShell, PageHeader } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';

/** One row of the flattened tree, carrying the depth it is drawn at. */
interface CategoryRow {
  readonly node: CategoryNode;
  readonly depth: number;
}

/**
 * The category tree.
 *
 * A tree, not a table, because `catalog.categories` is a materialised path (Step 10) and the
 * question an operator asks of it — "where does this sit" — is answered by the shape and by
 * nothing else. A flat paged list of eighty categories would make the parent of a category a
 * column you have to cross-reference.
 *
 * Two behaviours are load-bearing:
 *
 *  - **Position is edited as a number and the tree is redrawn from the server.** Drag-to-reorder
 *    over a nested tree is a lot of interaction code for an operation done once a quarter, and it
 *    is the one that cannot be driven from a keyboard.
 *  - **Deleting asks for the name to be typed.** A category with products under it is refused by
 *    the API, but one with a *child* is a whole branch, and there is no undo. `productCount` is
 *    shown on every row for the same reason: the consequence should be legible before the click.
 *
 * The attribute set is chosen here rather than on the product, because it is what decides which
 * attributes a product in this category is even asked for.
 */
@Component({
  selector: 'kh-categories-page',
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
      heading="Categories"
      description="The shape of the catalogue. A product sits in exactly one of these."
    >
      <button khButton type="button" variant="primary" (click)="startCreate(null)">
        <kh-icon name="plus" size="sm" />
        New top-level category
      </button>
    </kh-page-header>

    @if (error(); as message) {
      <kh-alert tone="danger" heading="The tree could not be loaded" [dismissible]="true">{{
        message
      }}</kh-alert>
    }

    @if (loading()) {
      <kh-skeleton height="18rem" />
    } @else if (rows().length === 0) {
      <p class="hint">No categories yet. Start with a top-level one.</p>
    } @else {
      <ul class="tree">
        @for (row of rows(); track row.node.id) {
          <li [style.--depth]="row.depth">
            <span class="name">{{ row.node.name }}</span>
            <span class="slug">/{{ row.node.slug }}</span>
            @if (!row.node.isActive) {
              <kh-badge tone="warning">Hidden</kh-badge>
            }

            <span class="row-actions">
              <button khButton type="button" size="sm" (click)="startEdit(row.node.id)">Edit</button>
              <button khButton type="button" size="sm" (click)="startCreate(row.node.id)">Add child</button>
            </span>
          </li>
        }
      </ul>
    }

    @if (drawerOpen()) {
      <kh-entity-drawer
        [heading]="editingId() ? 'Edit category' : 'New category'"
        [subtitle]="parentName()"
        (closed)="drawerOpen.set(false)"
      >
        <kh-form-shell
          heading="Category"
          [summary]="summary()"
          [saving]="saving()"
          [dirty]="true"
          [submitLabel]="editingId() ? 'Save' : 'Create'"
          (submitted)="save()"
          (cancelled)="drawerOpen.set(false)"
        >
          <kh-field label="Name" for="category-name" [error]="form.fields.name.error()">
            <input
              khControl
              id="category-name"
              type="text"
              maxlength="120"
              [value]="form.fields.name.value()"
              (input)="form.fields.name.set($any($event.target).value)"
              (touched)="form.fields.name.markTouched()"
            />
          </kh-field>

          <kh-field
            label="URL slug"
            for="category-slug"
            [optional]="true"
            hint="Left blank, it is made from the name."
          >
            <input
              khControl
              id="category-slug"
              type="text"
              [value]="form.fields.slug.value()"
              (input)="form.fields.slug.set($any($event.target).value)"
            />
          </kh-field>

          <kh-field label="Description" for="category-description" [optional]="true">
            <textarea
              khControl
              id="category-description"
              rows="3"
              [value]="form.fields.description.value()"
              (input)="form.fields.description.set($any($event.target).value)"
            ></textarea>
          </kh-field>

          <kh-field
            label="Attribute set"
            for="category-attribute-set"
            [optional]="true"
            hint="Which attributes a product here is asked for."
          >
            <select
              khControl
              id="category-attribute-set"
              [value]="form.fields.attributeSetId.value()"
              (change)="form.fields.attributeSetId.set($any($event.target).value)"
            >
              <option value="">None</option>
              @for (set of attributeSets(); track set.id) {
                <option [value]="set.id">{{ set.name }}</option>
              }
            </select>
          </kh-field>

          <kh-field
            label="Position"
            for="category-position"
            hint="Lower numbers come first among its siblings."
          >
            <input
              khControl
              khNumeric
              id="category-position"
              type="number"
              min="0"
              [value]="form.fields.position.value()"
              (input)="form.fields.position.set($any($event.target).value)"
            />
          </kh-field>

          <kh-checkbox
            label="Show this category on the storefront"
            inputId="category-active"
            [checked]="isActive()"
            (checkedChange)="isActive.set($event)"
          />
        </kh-form-shell>

        <div slot="footer">
          @if (editing(); as current) {
            <p class="counts">{{ current.productCount }} products in this category.</p>
            <button khButton type="button" size="sm" variant="danger" (click)="deleting.set(true)">
              Delete
            </button>
          }
        </div>
      </kh-entity-drawer>
    }

    <kh-confirm-dialog
      [open]="deleting()"
      heading="Delete this category"
      message="Anything filed under it, including its child categories, has to be moved first. This cannot be undone."
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

    .hint,
    .counts {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .tree {
      margin: 0;
      padding: 0;
      list-style: none;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .tree li {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      padding: var(--space-2) var(--space-3);
      padding-inline-start: calc(var(--space-3) + var(--depth) * var(--space-5));
      border-block-end: 1px solid var(--color-border);
    }

    .tree li:last-child {
      border-block-end: none;
    }

    .name {
      font-weight: var(--weight-medium);
    }

    .slug {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .row-actions {
      display: flex;
      gap: var(--space-2);
      margin-inline-start: auto;
    }

    [slot='footer'] {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      justify-content: space-between;
      inline-size: 100%;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CategoriesPage {
  private readonly catalog = inject(CatalogAdminService);
  private readonly toasts = inject(ToastService);

  private readonly tree = signal<readonly CategoryNode[]>([]);
  protected readonly attributeSets = signal<readonly AttributeSetResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly drawerOpen = signal(false);
  protected readonly deleting = signal(false);
  protected readonly editingId = signal<string | null>(null);
  protected readonly editing = signal<CategoryResponse | null>(null);
  protected readonly parentId = signal<string | null>(null);
  protected readonly isActive = signal(true);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('The name')], this.submitted),
    slug: formField('', [], this.submitted),
    description: formField('', [], this.submitted),
    attributeSetId: formField('', [], this.submitted),
    position: formField('0', [], this.submitted),
  });

  protected readonly rows = computed<readonly CategoryRow[]>(() => flatten(this.tree(), 0));

  protected readonly parentName = computed(() => {
    const parent = this.parentId();
    if (!parent) return this.editingId() ? null : 'A new top-level category';
    const found = this.rows().find((row) => row.node.id === parent);
    return found ? `Under ${found.node.name}` : null;
  });

  constructor() {
    this.load();
    this.catalog.attributeSets().subscribe({
      next: (sets) => this.attributeSets.set(sets),
      // Not fatal: without the sets the field offers "None", and the rest of the form still works.
      error: () => this.attributeSets.set([]),
    });
  }

  protected startCreate(parentId: string | null): void {
    this.editingId.set(null);
    this.editing.set(null);
    this.parentId.set(parentId);
    this.isActive.set(true);
    this.summary.set([]);
    this.form.reset({ name: '', slug: '', description: '', attributeSetId: '', position: '0' });
    this.drawerOpen.set(true);
  }

  protected startEdit(id: string): void {
    this.editingId.set(id);
    this.summary.set([]);
    this.drawerOpen.set(true);

    this.catalog.category(id).subscribe({
      next: (category) => {
        this.editing.set(category);
        this.parentId.set(category.parentId);
        this.isActive.set(category.isActive);
        this.form.reset({
          name: category.name,
          slug: category.slug,
          description: category.description ?? '',
          attributeSetId: category.attributeSetId ?? '',
          position: String(category.position),
        });
      },
      error: (error: unknown) => {
        this.drawerOpen.set(false);
        this.error.set(describeError(error, 'That category could not be opened.'));
      },
    });
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    const body: CategoryBody = {
      parentId: this.parentId(),
      name: values.name,
      slug: values.slug || null,
      description: values.description || null,
      imageFileId: this.editing()?.imageFileId ?? null,
      attributeSetId: values.attributeSetId || null,
      position: Number(values.position || 0),
      isActive: this.isActive(),
      seo: null,
    };

    this.saving.set(true);
    this.summary.set([]);

    const id = this.editingId();
    const request = id ? this.catalog.updateCategory(id, body) : this.catalog.createCategory(body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.drawerOpen.set(false);
        this.toasts.success(id ? 'Category saved.' : 'Category created.');
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
    const id = this.editingId();
    if (!id) return;

    this.saving.set(true);
    this.catalog.deleteCategory(id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleting.set(false);
        this.drawerOpen.set(false);
        this.toasts.success('Category deleted.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.deleting.set(false);
        // The API refuses a category that still has products or children, and says which. That
        // sentence is the whole answer, so it is shown rather than replaced.
        this.summary.set([describeError(error, 'That category could not be deleted.')]);
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.error.set(null);
    // Inactive categories included: a hidden branch is exactly the thing somebody comes here to
    // find, and a tree that omitted it would look like the category had been deleted.
    this.catalog.categoryTree(false).subscribe({
      next: (nodes) => {
        this.loading.set(false);
        this.tree.set(nodes);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(error, 'The categories could not be loaded.'));
      },
    });
  }
}

function flatten(nodes: readonly CategoryNode[], depth: number): CategoryRow[] {
  return nodes.flatMap((node) => [{ node, depth }, ...flatten(node.children, depth + 1)]);
}
