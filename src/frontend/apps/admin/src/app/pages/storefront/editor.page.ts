import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import {
  BlockTypeResponse,
  ContentAdminService,
  PageResponse,
  SeoBody,
  UpdatePageBody,
} from '@klarahome/data-access-admin';
import {
  ConfirmDialog,
  EntityMultiPicker,
  EntityOption,
  HasUnsavedChanges,
  Modal,
  PageHeader,
  StatusBadge,
} from '@klarahome/ui-admin';
import { CmsBlockView } from '@klarahome/ui-patterns';
import { Alert, Button, Checkbox, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';
import { EMPTY, Subject, catchError, debounce, forkJoin, map, of, switchMap, timer } from 'rxjs';

import { describeError, fieldErrors } from '../../core/describe-error';
import { MediaPicker } from '../catalog/media-picker';
import { BlockForm, FieldChange, ItemChange } from './block-form';
import { BlockDraft, HomeDraft, alignPreview, toBody } from './draft-model';
import { BrowseTarget, ReferenceDirectory, idsOf, nounFor } from './reference-directory';
import { CanvasFrame, CanvasKey, CanvasViewport, StorefrontCanvas } from './storefront-canvas';
import { Branding, StorefrontStudio } from './storefront-studio';
import { ViewportToggle } from './viewport-toggle';

/** How long after the last change the canvas is re-rendered from the server. */
export const PREVIEW_DELAY_MS = 400;

/** What the confirmation dialog is asking about. */
type Confirming = 'update-live' | 'publish';

/**
 * Step 3: the home page editor.
 *
 * **The local draft is the source of truth; the canvas is a picture of it.** Every edit changes a
 * `HomeDraft`, and about 400 ms after the last one the draft is posted to `preview-draft`, which
 * answers with the page as the storefront would render it (bound, resolved, not saved). The canvas
 * draws that answer through the shared `CmsBlockRenderer`, so a product tile here is the storefront's
 * product tile, not a second implementation of it. While a preview is in flight, or refused, the
 * last good picture stays and the draft is untouched - a slow or failed render cannot lose a word.
 *
 * Fields are the page composer's: `kh-schema-field` and `kh-reference-tags`, drawn from the block
 * schemas the server sends. Saving and publishing use the composer's own calls (`updatePage`,
 * `transitionPage`), and publishing or saving over a live page is confirmed the same way. The old
 * composer remains at `content/pages/:id` under Advanced.
 *
 * Keyboard: Tab reaches each block frame and each `+`; Enter selects or adds; ArrowUp and ArrowDown
 * move the focused block; Delete removes it (with an Undo). The block list in the side panel does
 * the same with buttons, for anyone who would rather not use the canvas.
 */
@Component({
  selector: 'kh-storefront-editor-page',
  imports: [
    Alert,
    BlockForm,
    Button,
    Checkbox,
    ConfirmDialog,
    EntityMultiPicker,
    MediaPicker,
    Modal,
    PageHeader,
    RouterLink,
    Skeleton,
    StatusBadge,
    StorefrontCanvas,
    ViewportToggle,
  ],
  providers: [ReferenceDirectory],
  template: `
    <kh-page-header
      heading="Edit the home page"
      [description]="subtitle()"
      [crumbs]="[{ label: 'Your storefront', path: '/storefront' }]"
    >
      @if (page(); as current) {
        <kh-status-badge [status]="current.status" />
        <kh-viewport-toggle [(value)]="viewport" />

        @if (isLive()) {
          <button
            khButton
            type="button"
            size="sm"
            variant="primary"
            [disabled]="!dirty() || busy()"
            (click)="confirming.set('update-live')"
          >
            {{ busy() ? 'Saving…' : 'Save & update live site' }}
          </button>
        } @else {
          <button
            khButton
            type="button"
            size="sm"
            [disabled]="!dirty() || busy()"
            (click)="saveDraft()"
          >
            {{ busy() ? 'Saving…' : 'Save draft' }}
          </button>
          @if (canPublish()) {
            <button
              khButton
              type="button"
              size="sm"
              variant="primary"
              [disabled]="busy()"
              (click)="confirming.set('publish')"
            >
              Publish
            </button>
          }
        }
      }
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="The home page could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="26rem" />
    } @else if (!page()) {
      <kh-alert tone="info" heading="There is no home page yet">
        Choosing a template creates one.
        <a khButton size="sm" routerLink="/storefront/templates">Change the look</a>
      </kh-alert>
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

      @if (dirty()) {
        <kh-alert tone="warning" [heading]="isLive() ? 'This page is live' : 'Unsaved changes'">
          @if (isLive()) {
            Shoppers are seeing this page now. These changes reach them only when you save, and saving
            updates the live page for everybody.
          } @else {
            Nothing here reaches the storefront until you save it.
          }
        </kh-alert>
      }

      @if (removed(); as gone) {
        <kh-alert tone="info" role="status">
          Removed {{ labelOf(gone.draft) }}.
          <button khButton type="button" size="sm" variant="tertiary" (click)="undoRemove()">Undo</button>
        </kh-alert>
      }

      <div class="layout">
        <section class="stage" aria-label="Home page">
          @if (previewError(); as message) {
            <kh-alert tone="warning" heading="The preview is paused">
              {{ message }} The page below is the last one that drew; your edits are kept.
            </kh-alert>
          }

          <div class="canvas" [attr.aria-busy]="previewing()">
            <kh-storefront-canvas
              [frames]="frames()"
              [preset]="branding()?.presetId ?? null"
              [headerStyle]="branding()?.headerStyle ?? 'inline'"
              [footerStyle]="branding()?.footerStyle ?? 'columns'"
              [storeName]="branding()?.storeName ?? 'Klara Home'"
              [viewport]="viewport()"
              [editable]="true"
              [selectedId]="selectedId()"
              title="The home page, as shoppers will see it"
              (blockSelected)="select($event)"
              (insertRequested)="inserting.set($event)"
              (blockKey)="onKey($event)"
            />
          </div>
          <p class="hint">
            Click a block to edit it. With a block focused, the arrow keys move it and Delete removes it.
          </p>
        </section>

        <aside class="side" aria-label="Block editor">
          <section class="kh-panel">
            <h2>Blocks</h2>
            @if (draft.blocks().length === 0) {
              <p class="hint">The home page has no blocks yet.</p>
            }
            <ol class="blocks">
              @for (block of draft.blocks(); track block.id; let first = $first; let last = $last) {
                <li [class.current]="block.id === selectedId()">
                  <button
                    khButton
                    type="button"
                    size="sm"
                    variant="tertiary"
                    class="pick"
                    [attr.aria-pressed]="block.id === selectedId()"
                    (click)="select(block.id)"
                  >
                    {{ labelOf(block) }}
                    @if (!block.isVisible) {
                      <span class="muted">(hidden)</span>
                    }
                  </button>
                  <span class="moves">
                    <button
                      khButton
                      type="button"
                      size="sm"
                      variant="tertiary"
                      [disabled]="first"
                      [attr.aria-label]="'Move ' + labelOf(block) + ' up'"
                      (click)="move(block.id, -1)"
                    >
                      Up
                    </button>
                    <button
                      khButton
                      type="button"
                      size="sm"
                      variant="tertiary"
                      [disabled]="last"
                      [attr.aria-label]="'Move ' + labelOf(block) + ' down'"
                      (click)="move(block.id, 1)"
                    >
                      Down
                    </button>
                  </span>
                </li>
              }
            </ol>
            <button khButton type="button" size="sm" (click)="inserting.set(draft.blocks().length)">
              Add a block
            </button>
          </section>

          @if (selected(); as block) {
            <section class="kh-panel" [attr.aria-label]="labelOf(block)">
              <header class="panel-head">
                <h2>{{ typeLabel(block.type) }}</h2>
                <button khButton type="button" size="sm" variant="tertiary" (click)="selectedId.set(null)">
                  Close
                </button>
              </header>

              @if (blockTypesError(); as message) {
                <kh-alert tone="danger" heading="The fields could not be loaded">{{ message }}</kh-alert>
              }

              <kh-block-form
                [draft]="block"
                [schema]="schemaFor(block.type)"
                (fieldChanged)="setField(block.id, $event)"
                (itemFieldChanged)="setItemField(block.id, $event)"
                (itemAdded)="draft.addItem(block.id)"
                (itemRemoved)="draft.removeItem(block.id, $event)"
                (itemMoved)="draft.moveItem(block.id, $event.index, $event.delta)"
                (referenceRemoved)="removeReference(block.id, $event.field.name, $event.field.isList, $event.id)"
                (browseRequested)="browsing.set({ blockId: block.id, ...$event })"
              />

              <kh-checkbox
                label="Visible to shoppers"
                [inputId]="block.id + '-visible'"
                [checked]="block.isVisible"
                (checkedChange)="draft.setVisible(block.id, $event)"
              />

              <div class="danger-zone">
                <button khButton type="button" size="sm" variant="danger" (click)="remove(block.id)">
                  Remove this block
                </button>
              </div>
            </section>
          } @else {
            <p class="hint">Select a block on the page, or from the list, to change what it says.</p>
          }
        </aside>
      </div>
    }

    <kh-modal [open]="inserting() !== null" heading="Add a block" width="36rem" (closed)="inserting.set(null)">
      @if (blockTypesError(); as message) {
        <kh-alert tone="danger" heading="The block library could not be loaded">{{ message }}</kh-alert>
      }
      <ul class="library">
        @for (type of libraryTypes(); track type.type) {
          <li>
            <button khButton type="button" (click)="add(type)">
              <span class="library-name">{{ type.label }}</span>
              <span class="library-description">{{ type.description }}</span>
            </button>
          </li>
        }
      </ul>
    </kh-modal>

    <!-- Finding what a block points at. Media has its own library dialog; everything else is a search. -->
    <kh-entity-multi-picker
      [open]="browseKind() !== null && browseKind() !== 'MediaRef'"
      [heading]="browseHeading()"
      [noun]="browseNoun()"
      [search]="browseSearch()"
      [existingIds]="browseExisting()"
      [limit]="browseLimit()"
      (confirmed)="applyBrowse($event)"
      (closed)="browsing.set(null)"
    />

    <kh-media-picker
      [open]="browseKind() === 'MediaRef'"
      [multiple]="browsing()?.mode !== 'single'"
      ownerType="ContentPage"
      [ownerId]="pageId()"
      (picked)="applyBrowse(references.mediaOptions($event))"
      (closed)="browsing.set(null)"
    />

    <kh-confirm-dialog
      [open]="confirming() === 'update-live'"
      heading="Update the live site?"
      message="This page is live. Saving will update it for shoppers right away."
      confirmLabel="Save & update"
      tone="warning"
      [busy]="busy()"
      (confirmed)="confirmUpdateLive()"
      (cancelled)="confirming.set(null)"
    />

    <kh-confirm-dialog
      [open]="confirming() === 'publish'"
      heading="Publish the home page?"
      message="Shoppers will see this version right away. Any unsaved edits are saved first."
      confirmLabel="Publish"
      tone="warning"
      [busy]="busy()"
      (confirmed)="confirmPublish()"
      (cancelled)="confirming.set(null)"
    />
  `,
  styles: `
    :host {
      display: block;
    }

    kh-alert {
      display: block;
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

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: minmax(0, 1fr) 24rem;
        align-items: start;
      }
    }

    .stage {
      min-width: 0;
    }

    .canvas {
      overflow: hidden;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface);
      box-shadow: var(--shadow-card);
    }

    .canvas[aria-busy='true'] {
      opacity: 0.85;
    }

    .side {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
      min-width: 0;
    }

    @media (min-width: 1024px) {
      .side {
        position: sticky;
        inset-block-start: var(--space-4);
        max-height: calc(100vh - var(--header-height) - var(--space-8));
        overflow-y: auto;
      }
    }

    .hint {
      margin: var(--space-2) 0 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .blocks {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      margin: 0 0 var(--space-3);
      padding: 0;
      list-style: none;
    }

    .blocks li {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
      border-radius: var(--radius-md);
    }

    .blocks li.current {
      background: var(--color-primary-subtle);
    }

    .pick {
      flex: 1;
      justify-content: flex-start;
      min-width: 0;
      text-align: start;
    }

    .moves {
      display: inline-flex;
      gap: var(--space-1);
    }

    .muted {
      color: var(--color-text-muted);
    }

    .panel-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
      margin-block-end: var(--space-3);
    }

    .panel-head h2 {
      margin: 0;
    }

    kh-block-form {
      margin-block-end: var(--space-3);
    }

    .danger-zone {
      margin-block-start: var(--space-4);
      padding-block-start: var(--space-3);
      border-block-start: 1px solid var(--color-border);
    }

    .library {
      display: grid;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .library button {
      display: grid;
      inline-size: 100%;
      justify-items: start;
      gap: var(--space-1);
      text-align: start;
    }

    .library-name {
      font-weight: var(--weight-medium);
    }

    .library-description {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
      font-weight: var(--weight-regular);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StorefrontEditorPage implements HasUnsavedChanges {
  private readonly content = inject(ContentAdminService);
  private readonly studio = inject(StorefrontStudio);
  private readonly toasts = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly references = inject(ReferenceDirectory);

  /** The blocks being edited, and whether they differ from what is saved. */
  protected readonly draft = new HomeDraft();
  protected readonly dirty = this.draft.dirty;

  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly page = signal<PageResponse | null>(null);
  protected readonly branding = signal<Branding | null>(null);
  protected readonly blockTypes = signal<readonly BlockTypeResponse[]>([]);
  protected readonly blockTypesError = signal<string | null>(null);

  protected readonly viewport = signal<CanvasViewport>('desktop');
  protected readonly selectedId = signal<string | null>(null);
  protected readonly inserting = signal<number | null>(null);
  protected readonly browsing = signal<BrowseTarget | null>(null);
  protected readonly confirming = signal<Confirming | null>(null);
  protected readonly removed = signal<{ readonly draft: BlockDraft; readonly index: number } | null>(null);

  protected readonly busy = signal(false);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly previewing = signal(false);
  protected readonly previewError = signal<string | null>(null);
  /** The latest render of each block, by the id of the draft it was rendered for. */
  private readonly views = signal<ReadonlyMap<string, CmsBlockView>>(new Map());

  protected readonly pageId = computed(() => this.page()?.id ?? '');
  protected readonly isLive = computed(() => this.page()?.status === 'Published');
  protected readonly canPublish = computed(() => this.page()?.allowedTransitions.includes('Published') ?? false);
  protected readonly selected = computed(
    () => this.draft.blocks().find((block) => block.id === this.selectedId()) ?? null,
  );

  protected readonly subtitle = computed(() => {
    const current = this.page();
    return current ? `/${current.slug} · v${current.version}` : null;
  });

  /** The block library: everything the server offers except privileged types, which Advanced keeps. */
  protected readonly libraryTypes = computed(() => this.blockTypes().filter((type) => !type.isPrivileged));

  protected readonly frames = computed<readonly CanvasFrame[]>(() => {
    const views = this.views();
    return this.draft.blocks().map((block) => ({
      id: block.id,
      label: this.labelOf(block),
      view: block.isVisible ? (views.get(block.id) ?? null) : null,
      note: block.isVisible
        ? this.previewing()
          ? 'Drawing this block…'
          : 'Nothing to show for this block yet.'
        : 'Hidden: shoppers do not see this block.',
    }));
  });

  // ---- The re-render pipeline ---------------------------------------------------------------------

  private readonly render$ = new Subject<boolean>();
  /** Set by a load or save: the next render should not wait out the debounce. */
  private immediateNext = false;

  constructor() {
    this.load();

    this.render$
      .pipe(
        debounce((immediate) => timer(immediate ? 0 : PREVIEW_DELAY_MS)),
        switchMap(() => this.renderDraft()),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();

    // Any change to the list asks for a render; the debounce coalesces a burst of typing into one.
    effect(() => {
      this.draft.blocks();
      untracked(() => {
        if (!this.page()) return;
        this.render$.next(this.immediateNext);
        this.immediateNext = false;
      });
    });

    // Names for the ids the blocks point at, whenever the blocks or their schemas change.
    effect(() => {
      const blocks = this.draft.blocks();
      const types = this.blockTypes();
      untracked(() => this.references.resolve(blocks, (type) => types.find((entry) => entry.type === type)));
    });
  }

  /** Read by the route's `unsavedChangesGuard`; see `navigation.ts`. */
  hasUnsavedChanges(): boolean {
    return this.draft.dirty();
  }

  // ---- Labels -------------------------------------------------------------------------------------

  protected typeLabel(type: string): string {
    return this.blockTypes().find((entry) => entry.type === type)?.label ?? type;
  }

  protected schemaFor(type: string): BlockTypeResponse | undefined {
    return this.blockTypes().find((entry) => entry.type === type);
  }

  /** "Hero: Handwoven for the home" - the type, then the first words it carries. */
  protected labelOf(block: BlockDraft): string {
    const type = this.typeLabel(block.type);
    const heading = block.config['headline'] ?? block.config['heading'];
    return typeof heading === 'string' && heading.trim() ? `${type}: ${heading.trim().slice(0, 40)}` : type;
  }

  // ---- Selecting, adding, moving, removing ----------------------------------------------------------

  protected select(id: string): void {
    this.selectedId.set(id);
  }

  protected onKey(event: CanvasKey): void {
    if (event.key === 'ArrowUp') this.move(event.id, -1);
    else if (event.key === 'ArrowDown') this.move(event.id, 1);
    else if (event.key === 'Delete' || event.key === 'Backspace') this.remove(event.id);
  }

  protected move(id: string, delta: -1 | 1): void {
    this.draft.move(id, delta);
  }

  protected remove(id: string): void {
    const blocks = this.draft.blocks();
    const index = blocks.findIndex((block) => block.id === id);
    const gone = blocks[index];
    if (!gone) return;

    this.draft.remove(id);
    this.removed.set({ draft: gone, index });
    if (this.selectedId() === id) this.selectedId.set(null);
  }

  protected undoRemove(): void {
    const gone = this.removed();
    if (!gone) return;
    this.draft.restore(gone.draft, gone.index);
    this.selectedId.set(gone.draft.id);
    this.removed.set(null);
  }

  protected add(type: BlockTypeResponse): void {
    const id = this.draft.add(type, this.inserting() ?? undefined);
    this.inserting.set(null);
    this.selectedId.set(id);
  }

  protected setField(id: string, change: FieldChange): void {
    this.draft.setField(id, change.field, change.value);
  }

  protected setItemField(id: string, change: ItemChange): void {
    this.draft.setItemField(id, change.index, change.field, change.value);
  }

  protected removeReference(blockId: string, field: string, isList: boolean, id: string): void {
    if (!isList) {
      this.draft.setField(blockId, field, null);
      return;
    }
    const block = this.draft.blocks().find((candidate) => candidate.id === blockId);
    const remaining = idsOf(block?.config[field]).filter((entry) => entry !== id);
    this.draft.setField(blockId, field, remaining.length > 0 ? remaining : null);
  }

  // ---- Browsing for references ---------------------------------------------------------------------

  protected readonly browseKind = computed(() => this.browsing()?.field.kind ?? null);

  protected readonly browseNoun = computed(() => nounFor(this.browseKind() ?? '', true));

  protected readonly browseHeading = computed(() => {
    const target = this.browsing();
    if (!target) return '';
    return target.mode === 'single'
      ? `Choose ${nounFor(target.field.kind, false)}`
      : `Add ${nounFor(target.field.kind, true)}`;
  });

  protected readonly browseSearch = computed(() => this.references.searchFor(this.browseKind() ?? ''));

  /** What the target already holds, shown ticked-and-disabled so it cannot be added twice. */
  protected readonly browseExisting = computed<readonly string[]>(() => {
    const target = this.browsing();
    const block = target && this.draft.blocks().find((candidate) => candidate.id === target.blockId);
    if (!target || !block) return [];

    if (target.mode === 'list') return idsOf(block.config[target.field.name]);
    if (target.mode === 'items') {
      return this.draft
        .itemsOf(block.id)
        .map((item) => item[target.field.name])
        .filter((value): value is string => typeof value === 'string' && value.length > 0);
    }
    // Choosing a single value replaces it, so the current one is not in the way.
    return [];
  });

  protected readonly browseLimit = computed<number | null>(() => {
    const target = this.browsing();
    if (!target) return null;
    if (target.mode === 'single') return 1;

    const held = this.browseExisting().length;
    const cap = target.mode === 'list' ? target.field.maxLength : (target.maxItems ?? 0);
    return cap > 0 ? Math.max(0, cap - held) : null;
  });

  /** Writes what was chosen into the block, in the shape the target field holds. */
  protected applyBrowse(options: readonly EntityOption[]): void {
    const target = this.browsing();
    // Read before the dialog closes: what the target holds is derived from `browsing`.
    const current = this.browseExisting();
    this.browsing.set(null);
    if (!target || options.length === 0) return;

    this.references.remember(target.field.kind, options);

    const name = target.field.name;
    const ids = options.map((option) => option.id);

    switch (target.mode) {
      case 'single':
        if (target.itemIndex === undefined) this.draft.setField(target.blockId, name, ids[0]);
        else this.draft.setItemField(target.blockId, target.itemIndex, name, ids[0]);
        break;

      case 'list': {
        const merged = [...current, ...ids.filter((id) => !current.includes(id))];
        const cap = target.field.maxLength;
        this.draft.setField(target.blockId, name, cap > 0 ? merged.slice(0, cap) : merged);
        break;
      }

      case 'items': {
        const held = new Set(current);
        const added = ids.filter((id) => !held.has(id)).map((id) => ({ [name]: id }));
        const items = [...this.draft.itemsOf(target.blockId), ...added];
        this.draft.setField(target.blockId, 'items', target.maxItems ? items.slice(0, target.maxItems) : items);
        break;
      }
    }

    this.toasts.success(
      ids.length === 1 ? `Added ${options[0].label}.` : `Added ${ids.length} ${nounFor(target.field.kind, true)}.`,
    );
  }

  // ---- Rendering ----------------------------------------------------------------------------------

  /**
   * Posts the draft as it is now and, when the answer comes back, draws each block for the draft it
   * was rendered for. `snapshot` is taken at the moment of the request, so a block moved while the
   * answer was in flight keeps its picture rather than taking its neighbour's.
   */
  private renderDraft() {
    const id = this.pageId();
    const snapshot = this.draft.blocks();
    if (!id) return EMPTY;

    if (snapshot.length === 0) {
      this.views.set(new Map());
      this.previewError.set(null);
      return EMPTY;
    }

    this.previewing.set(true);
    return this.content
      .previewDraft(id, snapshot.map(toBody))
      .pipe(
        map((rendered) => {
          const aligned = alignPreview(snapshot, rendered.blocks);
          const views = new Map<string, CmsBlockView>();
          snapshot.forEach((block, index) => {
            const match = aligned[index];
            if (match) views.set(block.id, this.studio.view(match));
          });
          this.views.set(views);
          this.previewError.set(null);
          this.previewing.set(false);
        }),
        catchError((error: unknown) => {
          const problems = fieldErrors(error);
          this.previewError.set(
            problems
              ? Object.values(problems).flatMap((entries) => [...entries])[0]
              : describeError(error, 'The preview could not be drawn.'),
          );
          this.previewing.set(false);
          return EMPTY;
        }),
      );
  }

  // ---- Saving and publishing ------------------------------------------------------------------------

  protected saveDraft(): void {
    this.performSave();
  }

  protected confirmUpdateLive(): void {
    this.confirming.set(null);
    this.performSave();
  }

  protected confirmPublish(): void {
    this.confirming.set(null);
    if (this.draft.dirty()) this.performSave(() => this.publish());
    else this.publish();
  }

  private performSave(then?: () => void): void {
    const current = this.page();
    if (!current || this.busy()) return;

    this.busy.set(true);
    this.summary.set([]);
    const wasLive = this.isLive();

    this.content.updatePage(current.id, saveBody(current, this.draft.toBodies())).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.adopt(saved);
        this.toasts.success(wasLive ? 'Live site updated.' : 'Draft saved.');
        then?.();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        const problems = fieldErrors(error);
        this.summary.set(
          problems
            ? Object.values(problems).flatMap((entries) => [...entries])
            : [describeError(error, 'It could not be saved.')],
        );
      },
    });
  }

  private publish(): void {
    const current = this.page();
    if (!current) return;

    this.busy.set(true);
    this.content.transitionPage(current.id, { status: 'Published', scheduledAt: null, note: null }).subscribe({
      next: (published) => {
        this.busy.set(false);
        this.page.set(published);
        this.toasts.success('Published. The home page is live.');
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.summary.set([describeError(error, 'It could not be published.')]);
      },
    });
  }

  /**
   * Takes the server's answer to a save as the new baseline.
   *
   * Blocks added in this session get real ids in the answer, so the list is replaced with it. The
   * pictures and the selection are carried across by position (the server keeps the order we sent),
   * so nothing flickers and the panel does not close.
   */
  private adopt(saved: PageResponse): void {
    const before = this.draft.blocks();
    const selectedIndex = before.findIndex((block) => block.id === this.selectedId());
    const views = this.views();

    this.page.set(saved);
    this.draft.load(saved.blocks);
    const after = this.draft.blocks();

    if (after.length === before.length) {
      const carried = new Map<string, CmsBlockView>();
      before.forEach((block, index) => {
        const view = views.get(block.id);
        if (view) carried.set(after[index].id, view);
      });
      this.views.set(carried);
      if (selectedIndex >= 0) this.selectedId.set(after[selectedIndex].id);
    } else {
      this.views.set(new Map());
      this.selectedId.set(null);
    }
    this.immediateNext = true;
  }

  // ---- Loading ------------------------------------------------------------------------------------

  private load(): void {
    this.content.blockTypes().subscribe({
      next: (types) => this.blockTypes.set(types),
      error: (error: unknown) =>
        this.blockTypesError.set(describeError(error, 'No block can be added or edited until it loads.')),
    });

    forkJoin({
      branding: this.studio.branding().pipe(catchError(() => of(null))),
      home: this.content.homePage(),
    })
      .pipe(
        switchMap(({ branding, home }) => {
          this.branding.set(branding);
          return home ? this.content.page(home.id) : of(null as PageResponse | null);
        }),
      )
      .subscribe({
        next: (loaded) => {
          this.loading.set(false);
          if (!loaded) return;
          this.immediateNext = true;
          this.page.set(loaded);
          this.draft.load(loaded.blocks);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.loadError.set(describeError(error, 'Please try again.'));
        },
      });
  }
}

/**
 * The body of a save: the page as it was loaded, with only the blocks replaced.
 *
 * The designer edits blocks and nothing else, so every other field is sent back as it came. In
 * particular the SEO fields are mapped across whole - the composer's form only holds some of them
 * and writes the rest as empty, which would wipe the others from a page this screen has no form for.
 */
export function saveBody(page: PageResponse, blocks: UpdatePageBody['blocks']): UpdatePageBody {
  const seo: SeoBody = {
    metaTitle: page.seo.metaTitle,
    metaDescription: page.seo.metaDescription,
    metaKeywords: page.seo.metaKeywords,
    canonicalUrl: page.seo.canonicalUrl,
    ogTitle: page.seo.ogTitle,
    ogDescription: page.seo.ogDescription,
    ogImageFileId: page.seo.ogImage?.fileId ?? null,
    ogType: page.seo.ogType,
    noIndex: page.seo.noIndex,
    noFollow: page.seo.noFollow,
    sitemapPriority: page.seo.sitemapPriority,
  };

  return {
    slug: page.slug,
    title: page.title,
    summary: page.summary,
    seo,
    coverImageFileId: page.coverImage?.fileId ?? null,
    author: page.author,
    tags: page.tags,
    blocks,
  };
}
