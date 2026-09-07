import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import {
  CatalogAdminService,
  CategoryNode,
  ContentAdminService,
  MenuItemBody,
  MenuItemResponse,
  MenuLinkType,
  MenuResponse,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import { ConfirmDialog, HasUnsavedChanges, PageHeader, ReorderItem, ReorderList } from '@klarahome/ui-admin';
import { Alert, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { MENU_LINK_TYPES } from './content-vocabulary';

/** One item as the editor holds it. Position comes from the array; depth from `parentId`. */
interface ItemDraft {
  readonly id: string;
  parentId: string | null;
  label: string;
  linkType: MenuLinkType;
  targetId: string | null;
  url: string | null;
  isVisible: boolean;
  opensInNewTab: boolean;
  badge: string | null;
}

/**
 * One menu, and everything in it.
 *
 * **An item points at a thing, not at a URL.** `linkType` plus `targetId` is what lets a page,
 * a category or a collection be renamed without breaking the navigation that points at it — Step
 * 20's reason for modelling menu items this way — and it is why the target picker offers a list of
 * real records rather than a text box. `Url` is the escape hatch and is labelled as the one that
 * breaks.
 *
 * **The whole tree is saved in one write.** Position and parentage are properties of the tree
 * rather than of an item: moving one item changes where its siblings sit, and expressing one drag
 * as three requests would leave the storefront's menu briefly wrong. So the editor holds the whole
 * list and `PUT`s it, which is what the endpoint accepts.
 *
 * **Depth is one level.** The model allows more, but a menu that nests three deep is one a shopper
 * on a phone cannot open, and the storefront's drawer draws two. An item is either top level or a
 * child of a top-level item, chosen from a select rather than by dragging into a tree — dragging
 * into a nested drop target is the interaction that most often produces an accidental move.
 */
@Component({
  selector: 'kh-menu-editor-page',
  imports: [
    Alert,
    Button,
    Checkbox,
    ConfirmDialog,
    Control,
    Field,
    HasPermission,
    Icon,
    PageHeader,
    ReorderList,
    Skeleton,
  ],
  template: `
    <kh-page-header
      [heading]="menu()?.name ?? 'Menu'"
      [description]="subtitle()"
      [crumbs]="[{ label: 'Menus', path: '/content/menus' }]"
    >
      <ng-container *khHasPermission="'content.content.manage'">
        <button
          khButton
          type="button"
          size="sm"
          variant="primary"
          [disabled]="!dirty() || busy()"
          (click)="save()"
        >
          {{ busy() ? 'Saving…' : 'Save menu' }}
        </button>
        <button khButton type="button" size="sm" variant="danger" (click)="deleting.set(true)">Delete</button>
      </ng-container>
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="This menu could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="20rem" />
    } @else {
      @if (summary().length > 0) {
        <kh-alert tone="danger" heading="It could not be saved">
          <ul>
            @for (message of summary(); track message) {
              <li>{{ message }}</li>
            }
          </ul>
        </kh-alert>
      }

      <div class="layout">
        <section>
          <h2>Items</h2>
          <p class="hint">The order here is the order in the menu. Children follow their parent.</p>

          <kh-reorder-list
            label="Menu items"
            [items]="reorderItems()"
            [removable]="true"
            emptyMessage="This menu is empty. Add an item to start."
            (reordered)="reorder($event)"
            (removed)="removeItem($event)"
          />

          <button khButton type="button" size="sm" class="add" (click)="addItem()">
            <kh-icon name="plus" size="sm" />
            Add an item
          </button>

          @for (item of items(); track item.id) {
            <article class="item">
              <div class="row">
                <kh-field [label]="'Label'" [for]="item.id + '-label'">
                  <input
                    khControl
                    [id]="item.id + '-label'"
                    type="text"
                    maxlength="80"
                    [value]="item.label"
                    (input)="setItem(item.id, 'label', $any($event.target).value)"
                  />
                </kh-field>

                <kh-field [label]="'Sits under'" [for]="item.id + '-parent'" [optional]="true">
                  <select
                    khControl
                    [id]="item.id + '-parent'"
                    [value]="item.parentId ?? ''"
                    (change)="setParent(item.id, $any($event.target).value)"
                  >
                    <option value="">Top level</option>
                    @for (parent of parentOptions(item.id); track parent.id) {
                      <option [value]="parent.id">{{ parent.label }}</option>
                    }
                  </select>
                </kh-field>
              </div>

              <div class="row">
                <kh-field [label]="'Points at'" [for]="item.id + '-type'" [hint]="linkHint(item.linkType)">
                  <select
                    khControl
                    [id]="item.id + '-type'"
                    [value]="item.linkType"
                    (change)="setLinkType(item.id, $any($event.target).value)"
                  >
                    @for (choice of linkTypes; track choice.value) {
                      <option [value]="choice.value">{{ choice.label }}</option>
                    }
                  </select>
                </kh-field>

                @if (item.linkType === 'Url') {
                  <kh-field [label]="'URL'" [for]="item.id + '-url'">
                    <input
                      khControl
                      [id]="item.id + '-url'"
                      type="text"
                      [value]="item.url ?? ''"
                      (input)="setItem(item.id, 'url', $any($event.target).value)"
                    />
                  </kh-field>
                } @else if (item.linkType !== 'None') {
                  <kh-field [label]="targetLabel(item.linkType)" [for]="item.id + '-target'">
                    <select
                      khControl
                      [id]="item.id + '-target'"
                      [value]="item.targetId ?? ''"
                      (change)="setItem(item.id, 'targetId', $any($event.target).value)"
                    >
                      <option value=""></option>
                      @for (option of targetsFor(item.linkType); track option.id) {
                        <option [value]="option.id">{{ option.label }}</option>
                      }
                    </select>
                  </kh-field>
                }
              </div>

              <div class="row">
                <kh-field
                  [label]="'Badge'"
                  [for]="item.id + '-badge'"
                  [optional]="true"
                  hint="A short word: New, Sale."
                >
                  <input
                    khControl
                    [id]="item.id + '-badge'"
                    type="text"
                    maxlength="20"
                    [value]="item.badge ?? ''"
                    (input)="setItem(item.id, 'badge', $any($event.target).value)"
                  />
                </kh-field>

                <div class="toggles">
                  <kh-checkbox
                    label="Visible"
                    [inputId]="item.id + '-visible'"
                    [checked]="item.isVisible"
                    (checkedChange)="setFlag(item.id, 'isVisible', $event)"
                  />
                  <kh-checkbox
                    label="Opens in a new tab"
                    [inputId]="item.id + '-newtab'"
                    [checked]="item.opensInNewTab"
                    (checkedChange)="setFlag(item.id, 'opensInNewTab', $event)"
                  />
                </div>
              </div>
            </article>
          }
        </section>

        <aside class="panel">
          <h2>The menu</h2>

          <kh-field label="Name" for="menu-name">
            <input
              khControl
              id="menu-name"
              type="text"
              maxlength="120"
              [value]="name()"
              (input)="setName($any($event.target).value)"
            />
          </kh-field>

          <kh-field label="Placement" for="menu-placement" [optional]="true">
            <input
              khControl
              id="menu-placement"
              type="text"
              maxlength="60"
              [value]="placement()"
              (input)="setPlacement($any($event.target).value)"
            />
          </kh-field>

          <kh-checkbox
            label="Show this menu on the storefront"
            inputId="menu-active"
            [checked]="isActive()"
            (checkedChange)="setActive($event)"
          />

          <p class="note">
            The code is <code>{{ menu()?.code }}</code> and cannot be changed — the storefront asks for this
            menu by it.
          </p>
        </aside>
      </div>
    }

    <kh-confirm-dialog
      [open]="deleting()"
      heading="Delete this menu"
      message="Every item in it goes too, and any storefront page asking for its code gets nothing."
      confirmLabel="Delete"
      [confirmPhrase]="menu()?.code ?? null"
      [busy]="busy()"
      (confirmed)="remove()"
      (cancelled)="deleting.set(false)"
    />
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .layout {
      display: grid;
      gap: var(--space-6);
      grid-template-columns: minmax(0, 1fr);
    }

    @media (min-width: 60rem) {
      .layout {
        grid-template-columns: minmax(0, 3fr) minmax(18rem, 1fr);
        align-items: start;
      }
    }

    h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
    }

    .panel,
    .item {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .item {
      margin-block-start: var(--space-4);
    }

    .add {
      margin-block-start: var(--space-3);
    }

    .row {
      display: flex;
      gap: var(--space-3);
      flex-wrap: wrap;
    }

    .row > kh-field {
      flex: 1 1 12rem;
    }

    .toggles {
      display: flex;
      gap: var(--space-4);
      align-items: center;
      flex: 1 1 12rem;
    }

    .hint,
    .note {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .hint {
      margin: 0 0 var(--space-3);
    }

    code {
      font-family: var(--font-mono);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MenuEditorPage implements HasUnsavedChanges {
  private readonly content = inject(ContentAdminService);
  private readonly catalog = inject(CatalogAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly linkTypes = MENU_LINK_TYPES;
  private readonly id = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly menu = signal<MenuResponse | null>(null);
  protected readonly items = signal<readonly ItemDraft[]>([]);
  protected readonly name = signal('');
  protected readonly placement = signal('');
  protected readonly isActive = signal(true);

  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly dirty = signal(false);
  protected readonly deleting = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly summary = signal<readonly string[]>([]);

  /**
   * The things a menu item can point at.
   *
   * Read straight off each list's own signal, one page of two hundred and no paging control: a
   * picker is a picker, and a store with more than two hundred published pages needs a search box
   * rather than a Next button inside a select.
   */
  private readonly pages = this.content.pages({ status: 'Published' }, 200);
  private readonly collections = this.content.collections({ activeOnly: true }, 200);
  private readonly categories = signal<readonly { id: string; label: string }[]>([]);

  protected readonly subtitle = computed(() => {
    const current = this.menu();
    if (!current) return null;
    return `${current.code}${current.placement ? ` · ${current.placement}` : ''} · ${this.items().length} item${this.items().length === 1 ? '' : 's'}`;
  });

  protected readonly reorderItems = computed<readonly ReorderItem[]>(() =>
    this.items().map((item) => ({
      id: item.id,
      // Indented rather than nested, so one flat list carries both the order and the parentage.
      label: item.parentId ? `— ${item.label || 'Untitled'}` : item.label || 'Untitled',
      sublabel: item.isVisible ? null : 'Hidden',
      meta: item.linkType,
    })),
  );

  constructor() {
    this.load();
    this.loadTargets();
  }

  hasUnsavedChanges(): boolean {
    return this.dirty();
  }

  // ---- Options ----------------------------------------------------------------------------------

  protected linkHint(linkType: string): string {
    return this.linkTypes.find((choice) => choice.value === linkType)?.hint ?? '';
  }

  protected targetLabel(linkType: string): string {
    return this.linkTypes.find((choice) => choice.value === linkType)?.label ?? 'Target';
  }

  protected targetsFor(linkType: string): readonly { id: string; label: string }[] {
    if (linkType === 'Page') return this.pages.rows().map((page) => ({ id: page.id, label: page.title }));
    if (linkType === 'Collection')
      return this.collections.rows().map((entry) => ({ id: entry.id, label: entry.name }));
    if (linkType === 'Category') return this.categories();
    return [];
  }

  /** Anything that is not this item and is not already a child — depth stops at one level. */
  protected parentOptions(itemId: string): readonly { id: string; label: string }[] {
    return this.items()
      .filter((item) => item.id !== itemId && item.parentId === null)
      .map((item) => ({ id: item.id, label: item.label || 'Untitled' }));
  }

  // ---- Editing ----------------------------------------------------------------------------------

  protected addItem(): void {
    this.items.update((current) => [
      ...current,
      {
        // Local until saved; `MenuItemBody.id` null is what tells the server this one is new.
        id: `new-${crypto.randomUUID()}`,
        parentId: null,
        label: '',
        linkType: 'Page',
        targetId: null,
        url: null,
        isVisible: true,
        opensInNewTab: false,
        badge: null,
      },
    ]);
    this.dirty.set(true);
  }

  protected removeItem(itemId: string): void {
    // A parent taking its children with it: an orphan would silently move to the top level.
    this.items.update((current) => current.filter((item) => item.id !== itemId && item.parentId !== itemId));
    this.dirty.set(true);
  }

  protected reorder(ids: readonly string[]): void {
    const byId = new Map(this.items().map((item) => [item.id, item]));
    this.items.set(ids.map((id) => byId.get(id)).filter((item): item is ItemDraft => item !== undefined));
    this.dirty.set(true);
  }

  protected setItem(itemId: string, field: 'label' | 'url' | 'targetId' | 'badge', value: string): void {
    this.items.update((current) =>
      current.map((item) => (item.id === itemId ? { ...item, [field]: value || null } : item)),
    );
    this.dirty.set(true);
  }

  protected setFlag(itemId: string, field: 'isVisible' | 'opensInNewTab', value: boolean): void {
    this.items.update((current) =>
      current.map((item) => (item.id === itemId ? { ...item, [field]: value } : item)),
    );
    this.dirty.set(true);
  }

  protected setParent(itemId: string, parentId: string): void {
    this.items.update((current) =>
      current.map((item) => (item.id === itemId ? { ...item, parentId: parentId || null } : item)),
    );
    this.dirty.set(true);
  }

  /** Changing the kind clears the other kind's target, so a stale id is never sent. */
  protected setLinkType(itemId: string, linkType: string): void {
    this.items.update((current) =>
      current.map((item) =>
        item.id === itemId
          ? { ...item, linkType: linkType as MenuLinkType, targetId: null, url: null }
          : item,
      ),
    );
    this.dirty.set(true);
  }

  protected setName(value: string): void {
    this.name.set(value);
    this.dirty.set(true);
  }

  protected setPlacement(value: string): void {
    this.placement.set(value);
    this.dirty.set(true);
  }

  protected setActive(value: boolean): void {
    this.isActive.set(value);
    this.dirty.set(true);
  }

  // ---- Saving -----------------------------------------------------------------------------------

  protected save(): void {
    if (this.busy()) return;

    this.busy.set(true);
    this.summary.set([]);

    const items: MenuItemBody[] = this.items().map((item) => ({
      id: item.id.startsWith('new-') ? null : item.id,
      // A parent that is itself unsaved has no server id yet, so a child of one is sent as top
      // level and re-parented on the next save. Rare, and better than a request the API refuses.
      parentId: item.parentId && !item.parentId.startsWith('new-') ? item.parentId : null,
      label: item.label,
      linkType: item.linkType,
      targetId: item.targetId,
      url: item.url,
      isVisible: item.isVisible,
      opensInNewTab: item.opensInNewTab,
      iconFileId: null,
      badge: item.badge,
    }));

    this.content
      .updateMenu(this.id, {
        name: this.name(),
        placement: this.placement() || null,
        isActive: this.isActive(),
        items,
      })
      .subscribe({
        next: (saved) => {
          this.busy.set(false);
          this.dirty.set(false);
          this.fill(saved);
          this.toasts.success('Menu saved.');
        },
        error: (error: unknown) => {
          this.busy.set(false);
          const errors = fieldErrors(error);
          this.summary.set(
            errors
              ? Object.values(errors).flatMap((messages) => [...messages])
              : [describeError(error, 'It could not be saved.')],
          );
        },
      });
  }

  protected remove(): void {
    this.busy.set(true);
    this.content.deleteMenu(this.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.deleting.set(false);
        this.dirty.set(false);
        this.toasts.success('Menu deleted.');
        void this.router.navigate(['/content/menus']);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.deleting.set(false);
        this.summary.set([describeError(error, 'It could not be deleted.')]);
      },
    });
  }

  // ---- Loading ----------------------------------------------------------------------------------

  private load(): void {
    this.loading.set(true);
    this.content.menu(this.id).subscribe({
      next: (menu) => {
        this.loading.set(false);
        this.fill(menu);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'That menu could not be loaded.'));
      },
    });
  }

  /**
   * Fetches the pickers' contents.
   *
   * None of the three is fatal on its own: a list that fails leaves that link kind with an empty
   * picker, which is visible and recoverable, rather than an editor that refuses to open.
   */
  private loadTargets(): void {
    this.pages.load();
    this.collections.load();

    this.catalog.categoryTree(true).subscribe({
      next: (tree) => this.categories.set(flatten(tree, 0)),
      error: () => this.categories.set([]),
    });
  }

  private fill(menu: MenuResponse): void {
    this.menu.set(menu);
    this.name.set(menu.name);
    this.placement.set(menu.placement ?? '');
    this.isActive.set(menu.isActive);
    this.items.set(menu.items.map(toDraft));
  }
}

function toDraft(item: MenuItemResponse): ItemDraft {
  return {
    id: item.id,
    parentId: item.parentId,
    label: item.label,
    linkType: item.linkType,
    targetId: item.targetId,
    url: item.url,
    isVisible: item.isVisible,
    opensInNewTab: item.opensInNewTab,
    badge: item.badge,
  };
}

function flatten(nodes: readonly CategoryNode[], depth: number): { id: string; label: string }[] {
  return nodes.flatMap((node) => [
    { id: node.id, label: `${'— '.repeat(depth)}${node.name}` },
    ...flatten(node.children ?? [], depth + 1),
  ]);
}
