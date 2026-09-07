import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import {
  BlockBody,
  BlockFieldResponse,
  BlockResponse,
  BlockTypeResponse,
  ContentAdminService,
  MediaFileResponse,
  PageResponse,
  PageVersionSummaryResponse,
  StorePageResponse,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  ConfirmDialog,
  HasUnsavedChanges,
  Modal,
  PageHeader,
  ReorderItem,
  ReorderList,
  StatusBadge,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';
import { MediaPicker } from '../catalog/media-picker';
import { TRANSITION_LABELS } from './content-vocabulary';

/** One block as the composer holds it: the API's record, plus the config being edited. */
interface BlockDraft {
  readonly id: string;
  readonly type: string;
  config: Record<string, unknown>;
  isVisible: boolean;
  startsAt: string;
  endsAt: string;
}

/**
 * The page composer.
 *
 * **Every form on this screen is drawn from a schema the server sent.** `GET
 * /admin/content/block-types` answers each block type with its fields — name, kind, required,
 * maximum length, the choices it accepts — and this component renders a control per field from
 * that. Step 20 made those schemas data for exactly this reason: the form that collects a block's
 * configuration and the validator that judges it are then one declaration rather than two, and a
 * block type added on the server appears in the library here with no change to this file. There is
 * no `switch (block.type)` anywhere below, and there must never be one.
 *
 * **The order is the data.** Blocks carry a position, so reordering is a write, not a view
 * preference — and the whole block list goes back on save, because moving one block changes the
 * position of its siblings and three requests to express one drag would leave the page briefly
 * wrong in a way a visitor could see. `kh-reorder-list` gives keyboard moves as well as dragging
 * for the same reason it does in the menu editor.
 *
 * **Publishing is a transition, and the page says which ones it will accept.**
 * `allowedTransitions` comes off the server's own table, so the buttons here are the edges that
 * exist rather than a second copy of the lifecycle. Scheduling is one of those edges carrying a
 * time; nothing on this screen publishes a scheduled page, because only the clock does.
 *
 * **Preview renders the stored version, not this form.** It asks the API what the storefront would
 * serve, which is the only preview worth having — a client-side render of the draft would be a
 * second implementation of the storefront's block renderer, and the two would drift on the day one
 * of them was fixed.
 *
 * The unsaved-changes guard is declared on the route (`navigation.ts`) rather than here, and this
 * component answers it through `HasUnsavedChanges`.
 */
@Component({
  selector: 'kh-page-composer-page',
  imports: [
    Alert,
    Badge,
    Button,
    Checkbox,
    ConfirmDialog,
    Control,
    Field,
    HasPermission,
    Icon,
    MediaPicker,
    Modal,
    PageHeader,
    ReorderList,
    Skeleton,
    StatusBadge,
  ],
  template: `
    <kh-page-header
      [heading]="page()?.title ?? 'Page'"
      [description]="subtitle()"
      [crumbs]="[{ label: 'Pages', path: '/content/pages' }]"
    >
      @if (page(); as current) {
        <kh-status-badge [status]="current.status" />

        <ng-container *khHasPermission="'content.content.manage'">
          <button khButton type="button" size="sm" [disabled]="!dirty() || busy()" (click)="save()">
            {{ busy() ? 'Saving…' : 'Save draft' }}
          </button>
          <button khButton type="button" size="sm" variant="tertiary" (click)="openPreview()">Preview</button>

          @for (status of current.allowedTransitions; track status) {
            <button
              khButton
              type="button"
              size="sm"
              [variant]="status === 'Published' ? 'primary' : 'tertiary'"
              [disabled]="busy()"
              (click)="startTransition(status)"
            >
              {{ transitionLabel(status) }}
            </button>
          }

          <button khButton type="button" size="sm" variant="danger" (click)="deleting.set(true)">
            Delete
          </button>
        </ng-container>
      }
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="This page could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="24rem" />
    } @else if (page(); as current) {
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
        <kh-alert tone="warning" heading="Unsaved changes">
          Nothing on this screen reaches the storefront until it is saved, and a published page keeps serving
          its last published version until it is published again.
        </kh-alert>
      }

      <div class="layout">
        <section>
          <h2>Blocks</h2>
          <p class="hint">The order here is the order on the page. Drag a block, or use the move buttons.</p>

          <kh-reorder-list
            label="Blocks on this page"
            [items]="blockItems()"
            [removable]="true"
            emptyMessage="This page has no blocks yet. Add one from the library."
            (reordered)="reorder($event)"
            (removed)="removeBlock($event)"
          />

          <div class="library">
            <h3>Add a block</h3>
            @if (blockTypesError(); as message) {
              <kh-alert tone="danger" heading="The block library could not be loaded">{{ message }}</kh-alert>
            }
            <div class="chips">
              @for (type of blockTypes(); track type.type) {
                <button khButton type="button" size="sm" [title]="type.description" (click)="addBlock(type)">
                  <kh-icon name="plus" size="sm" />
                  {{ type.label }}
                </button>
              }
            </div>
          </div>

          @for (draft of blocks(); track draft.id) {
            <article class="block">
              <header>
                <h3>{{ labelFor(draft.type) }}</h3>
                <kh-badge tone="neutral">{{ draft.type }}</kh-badge>
              </header>

              @if (schemaFor(draft.type); as schema) {
                @for (field of schema.fields; track field.name) {
                  <kh-field
                    [label]="humanise(field.name)"
                    [for]="draft.id + '-' + field.name"
                    [optional]="!field.isRequired"
                    [hint]="fieldHint(field)"
                  >
                    @if (field.choices && field.choices.length > 0) {
                      <select
                        khControl
                        [id]="draft.id + '-' + field.name"
                        [value]="text(draft.config[field.name])"
                        (change)="setConfig(draft.id, field.name, $any($event.target).value)"
                      >
                        <option value=""></option>
                        @for (choice of field.choices; track choice) {
                          <option [value]="choice">{{ choice }}</option>
                        }
                      </select>
                    } @else if (field.kind === 'Boolean') {
                      <input
                        khControl
                        [id]="draft.id + '-' + field.name"
                        type="checkbox"
                        [checked]="draft.config[field.name] === true"
                        (change)="setConfigBoolean(draft.id, field.name, $any($event.target).checked)"
                      />
                    } @else if (field.kind === 'Integer') {
                      <input
                        khControl
                        [id]="draft.id + '-' + field.name"
                        type="number"
                        [value]="text(draft.config[field.name])"
                        (input)="setConfigNumber(draft.id, field.name, $any($event.target).value)"
                      />
                    } @else if (isLongText(field)) {
                      <textarea
                        khControl
                        [id]="draft.id + '-' + field.name"
                        rows="3"
                        [value]="text(draft.config[field.name])"
                        (input)="setConfigText(draft.id, field, $any($event.target).value)"
                      ></textarea>
                    } @else {
                      <input
                        khControl
                        [id]="draft.id + '-' + field.name"
                        type="text"
                        [attr.maxlength]="field.maxLength > 0 ? field.maxLength : null"
                        [value]="text(draft.config[field.name])"
                        (input)="setConfig(draft.id, field.name, $any($event.target).value)"
                      />
                    }
                  </kh-field>
                }

                @if (schema.itemFields && schema.itemFields.length > 0) {
                  <p class="hint">
                    This block also holds up to {{ schema.maxItems }} items, which are edited as JSON until a
                    repeater is built for them.
                  </p>
                  <kh-field
                    label="Items (JSON)"
                    [for]="draft.id + '-items'"
                    [optional]="true"
                    [hint]="itemsHint(schema)"
                  >
                    <textarea
                      khControl
                      [id]="draft.id + '-items'"
                      rows="6"
                      [value]="itemsJson(draft)"
                      (input)="setItems(draft.id, $any($event.target).value)"
                    ></textarea>
                  </kh-field>
                }
              } @else {
                <kh-alert tone="warning" heading="Unknown block type">
                  The server does not declare <code>{{ draft.type }}</code> any more. It is kept as it is and
                  will be sent back unchanged; remove it if it is no longer wanted.
                </kh-alert>
              }

              <kh-checkbox
                label="Visible"
                [inputId]="draft.id + '-visible'"
                [checked]="draft.isVisible"
                (checkedChange)="setVisible(draft.id, $event)"
              />

              <div class="row">
                <kh-field label="Shows from" [for]="draft.id + '-starts'" [optional]="true">
                  <input
                    khControl
                    [id]="draft.id + '-starts'"
                    type="datetime-local"
                    [value]="draft.startsAt"
                    (input)="setWindow(draft.id, 'startsAt', $any($event.target).value)"
                  />
                </kh-field>
                <kh-field label="Shows until" [for]="draft.id + '-ends'" [optional]="true">
                  <input
                    khControl
                    [id]="draft.id + '-ends'"
                    type="datetime-local"
                    [value]="draft.endsAt"
                    (input)="setWindow(draft.id, 'endsAt', $any($event.target).value)"
                  />
                </kh-field>
              </div>
            </article>
          }
        </section>

        <aside>
          <section class="panel">
            <h2>The page</h2>

            <kh-field label="Title" for="page-title">
              <input
                khControl
                id="page-title"
                type="text"
                maxlength="200"
                [value]="title()"
                (input)="setTitle($any($event.target).value)"
              />
            </kh-field>

            <kh-field label="URL slug" for="page-slug">
              <input
                khControl
                id="page-slug"
                type="text"
                [value]="slug()"
                (input)="setSlug($any($event.target).value)"
              />
            </kh-field>

            <kh-field label="Summary" for="page-summary" [optional]="true">
              <textarea
                khControl
                id="page-summary"
                rows="2"
                [value]="pageSummary()"
                (input)="setSummary($any($event.target).value)"
              ></textarea>
            </kh-field>

            <kh-field label="Author" for="page-author" [optional]="true">
              <input
                khControl
                id="page-author"
                type="text"
                [value]="author()"
                (input)="setAuthor($any($event.target).value)"
              />
            </kh-field>

            <kh-field label="Tags" for="page-tags" [optional]="true" hint="Comma separated.">
              <input
                khControl
                id="page-tags"
                type="text"
                [value]="tags()"
                (input)="setTags($any($event.target).value)"
              />
            </kh-field>

            <div class="cover">
              <span class="cover-label">Cover image</span>
              <span class="note">{{ coverFileId() ? 'Chosen' : 'None' }}</span>
              <div class="cover-actions">
                <button khButton type="button" size="sm" (click)="pickerOpen.set(true)">Choose</button>
                @if (coverFileId()) {
                  <button khButton type="button" size="sm" variant="tertiary" (click)="setCover(null)">
                    Remove
                  </button>
                }
              </div>
            </div>
          </section>

          <section class="panel">
            <h2>Search engines</h2>
            <p class="hint">Left blank, the storefront falls back to the title and the summary.</p>

            <kh-field label="Meta title" for="seo-title" [optional]="true">
              <input
                khControl
                id="seo-title"
                type="text"
                maxlength="200"
                [value]="metaTitle()"
                (input)="setMetaTitle($any($event.target).value)"
              />
            </kh-field>

            <kh-field label="Meta description" for="seo-description" [optional]="true">
              <textarea
                khControl
                id="seo-description"
                rows="3"
                [value]="metaDescription()"
                (input)="setMetaDescription($any($event.target).value)"
              ></textarea>
            </kh-field>

            <kh-field label="Canonical URL" for="seo-canonical" [optional]="true">
              <input
                khControl
                id="seo-canonical"
                type="url"
                [value]="canonicalUrl()"
                (input)="setCanonical($any($event.target).value)"
              />
            </kh-field>

            <kh-checkbox
              label="Ask search engines not to index this page"
              inputId="seo-noindex"
              [checked]="noIndex()"
              (checkedChange)="setNoIndex($event)"
            />
          </section>

          <section class="panel">
            <h2>Version history</h2>
            <p class="hint">
              A snapshot is taken on every publish. Restoring one replaces the draft; the snapshot itself is
              untouched.
            </p>

            @if (versions().length === 0) {
              <p class="note">This page has never been published.</p>
            } @else {
              <ul class="versions">
                @for (version of versions(); track version.version) {
                  <li>
                    <div>
                      <span class="version">v{{ version.version }}</span>
                      <span class="note">
                        {{ dateTime(version.createdAt) }}
                        @if (version.createdBy) {
                          · {{ version.createdBy }}
                        }
                        @if (version.restoredFrom) {
                          · restored from v{{ version.restoredFrom }}
                        }
                      </span>
                    </div>
                    <div class="version-actions">
                      <button
                        khButton
                        type="button"
                        size="sm"
                        variant="tertiary"
                        (click)="preview(version.version)"
                      >
                        Preview
                      </button>
                      <button
                        khButton
                        type="button"
                        size="sm"
                        variant="tertiary"
                        *khHasPermission="'content.content.manage'"
                        (click)="rollingBackTo.set(version.version)"
                      >
                        Restore
                      </button>
                    </div>
                  </li>
                }
              </ul>
            }
          </section>
        </aside>
      </div>
    }

    <kh-media-picker
      [open]="pickerOpen()"
      [multiple]="false"
      ownerType="ContentPage"
      [ownerId]="id"
      (picked)="chooseCover($event)"
      (closed)="pickerOpen.set(false)"
    />

    <kh-modal
      [open]="previewOpen()"
      heading="What the storefront would serve"
      width="48rem"
      (closed)="previewOpen.set(false)"
    >
      @if (previewError(); as message) {
        <kh-alert tone="danger" heading="It could not be previewed">{{ message }}</kh-alert>
      } @else if (previewing()) {
        <kh-skeleton height="12rem" />
      } @else if (previewed(); as rendered) {
        <p class="note">
          {{ rendered.blocks.length }} block{{ rendered.blocks.length === 1 ? '' : 's' }} · /{{
            rendered.slug
          }}
        </p>
        <ol class="preview">
          @for (block of rendered.blocks; track $index) {
            <li>
              <strong>{{ block.type }}</strong>
              <pre>{{ pretty(block.config) }}</pre>
            </li>
          }
        </ol>
      }
    </kh-modal>

    <kh-modal
      [open]="transitioning() !== null"
      [heading]="transitionHeading()"
      (closed)="transitioning.set(null)"
    >
      @if (transitionError(); as message) {
        <kh-alert tone="danger" heading="That did not take">{{ message }}</kh-alert>
      }

      @if (transitioning() === 'Scheduled') {
        <kh-field
          label="Publish at"
          for="transition-at"
          hint="The clock publishes it; nobody has to be here."
        >
          <input
            khControl
            id="transition-at"
            type="datetime-local"
            [value]="scheduledAt()"
            (input)="scheduledAt.set($any($event.target).value)"
          />
        </kh-field>
      }

      <kh-field label="Note" for="transition-note" [optional]="true" hint="Recorded on the version snapshot.">
        <input
          khControl
          id="transition-note"
          type="text"
          [value]="transitionNote()"
          (input)="transitionNote.set($any($event.target).value)"
        />
      </kh-field>

      <div slot="footer">
        <button khButton type="button" variant="tertiary" (click)="transitioning.set(null)">Cancel</button>
        <button khButton type="button" variant="primary" [disabled]="busy()" (click)="transition()">
          {{ transitionHeading() }}
        </button>
      </div>
    </kh-modal>

    <kh-confirm-dialog
      [open]="rollingBackTo() !== null"
      heading="Restore this version"
      message="The current draft is replaced by the snapshot. Anything unsaved on this screen is lost."
      confirmLabel="Restore"
      tone="warning"
      [busy]="busy()"
      (confirmed)="rollback()"
      (cancelled)="rollingBackTo.set(null)"
    />

    <kh-confirm-dialog
      [open]="deleting()"
      heading="Delete this page"
      message="The page and every version of it go. Taking it down instead keeps both."
      confirmLabel="Delete"
      [confirmPhrase]="page()?.slug ?? null"
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

    @media (min-width: 64rem) {
      .layout {
        grid-template-columns: minmax(0, 3fr) minmax(20rem, 2fr);
        align-items: start;
      }
    }

    h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
    }

    h3 {
      margin: 0;
      font-size: var(--text-base);
    }

    aside {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    .panel,
    .block,
    .library {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .block,
    .library {
      margin-block-start: var(--space-4);
    }

    .block header {
      display: flex;
      gap: var(--space-2);
      align-items: baseline;
      margin-block-end: var(--space-3);
      padding-block-end: var(--space-2);
      border-block-end: 1px solid var(--color-border);
    }

    .chips {
      display: flex;
      gap: var(--space-2);
      flex-wrap: wrap;
      margin-block-start: var(--space-2);
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

    .row {
      display: flex;
      gap: var(--space-3);
    }

    .row > kh-field {
      flex: 1;
    }

    .cover {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      flex-wrap: wrap;
      margin-block-start: var(--space-3);
    }

    .cover-label {
      font-weight: var(--weight-medium);
    }

    .cover-actions {
      display: flex;
      gap: var(--space-2);
      margin-inline-start: auto;
    }

    .versions {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .versions li {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      justify-content: space-between;
      padding-block: var(--space-2);
      border-block-end: 1px solid var(--color-border);
    }

    .version {
      display: block;
      font-weight: var(--weight-medium);
    }

    .version-actions {
      display: flex;
      gap: var(--space-1);
    }

    .preview {
      margin: 0;
      padding-inline-start: var(--space-5);
    }

    .preview pre {
      overflow-x: auto;
      margin: var(--space-1) 0 var(--space-3);
      padding: var(--space-2);
      border-radius: var(--radius-sm);
      background: var(--color-surface);
      font-family: var(--font-mono);
      font-size: var(--text-xs);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PageComposerPage implements HasUnsavedChanges {
  private readonly content = inject(ContentAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly dateTime = tableDateTime;
  protected readonly id = this.route.snapshot.paramMap.get('id') ?? '';

  protected readonly page = signal<PageResponse | null>(null);
  protected readonly blockTypes = signal<readonly BlockTypeResponse[]>([]);
  protected readonly blockTypesError = signal<string | null>(null);
  protected readonly versions = signal<readonly PageVersionSummaryResponse[]>([]);

  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly summary = signal<readonly string[]>([]);
  protected readonly dirty = signal(false);

  protected readonly blocks = signal<readonly BlockDraft[]>([]);
  protected readonly title = signal('');
  protected readonly slug = signal('');
  protected readonly pageSummary = signal('');
  protected readonly author = signal('');
  protected readonly tags = signal('');
  protected readonly coverFileId = signal<string | null>(null);
  protected readonly metaTitle = signal('');
  protected readonly metaDescription = signal('');
  protected readonly canonicalUrl = signal('');
  protected readonly noIndex = signal(false);

  protected readonly pickerOpen = signal(false);
  protected readonly deleting = signal(false);
  protected readonly rollingBackTo = signal<number | null>(null);

  protected readonly transitioning = signal<string | null>(null);
  protected readonly transitionNote = signal('');
  protected readonly transitionError = signal<string | null>(null);
  protected readonly scheduledAt = signal('');

  protected readonly previewOpen = signal(false);
  protected readonly previewing = signal(false);
  protected readonly previewError = signal<string | null>(null);
  protected readonly previewed = signal<StorePageResponse | null>(null);

  protected readonly subtitle = computed(() => {
    const current = this.page();
    if (!current) return null;
    const published = current.publishedAt
      ? `published ${tableDateTime(current.publishedAt)}`
      : 'never published';
    return `/${current.slug} · ${current.type} · v${current.version} · ${published}`;
  });

  protected readonly transitionHeading = computed(() => this.transitionLabel(this.transitioning() ?? ''));

  protected readonly blockItems = computed<readonly ReorderItem[]>(() =>
    this.blocks().map((draft) => ({
      id: draft.id,
      label: this.labelFor(draft.type),
      sublabel: draft.isVisible ? null : 'Hidden',
      meta: draft.type,
    })),
  );

  constructor() {
    this.load();
    this.loadBlockTypes();
    this.loadVersions();
  }

  /** Read by the route's `unsavedChangesGuard`; see `navigation.ts`. */
  hasUnsavedChanges(): boolean {
    return this.dirty();
  }

  // ---- Schema-driven rendering -------------------------------------------------------------------

  protected schemaFor(type: string): BlockTypeResponse | undefined {
    return this.blockTypes().find((entry) => entry.type === type);
  }

  protected labelFor(type: string): string {
    return this.schemaFor(type)?.label ?? type;
  }

  /**
   * Whether the field wants a textarea.
   *
   * A list is one value per line; rich text and raw markup are paragraphs; and anything the schema
   * allows more than four hundred characters of is a paragraph in practice whatever it is called.
   */
  protected isLongText(field: BlockFieldResponse): boolean {
    return field.isList || field.kind === 'RichText' || field.kind === 'Html' || field.maxLength > 400;
  }

  /**
   * What to tell the editor about one field.
   *
   * The reference kinds are the ones worth naming: `MediaRef`, `ProductRef`, `CategoryRef` and
   * `CollectionRef` are identifiers resolved at render time, and a field that asks for one without
   * saying so reads as a free-text caption.
   */
  protected fieldHint(field: BlockFieldResponse): string {
    const parts: string[] = [];
    if (field.isList) parts.push(`One per line, up to ${field.maxLength}`);
    else if (field.maxLength > 0) parts.push(`Up to ${field.maxLength} characters`);

    const reference = REFERENCE_HINTS[field.kind];
    if (reference) parts.push(reference);
    if (field.kind === 'Html') parts.push('Raw markup — only on a block that permits it');
    return parts.join(' · ');
  }

  protected itemsHint(schema: BlockTypeResponse): string {
    return `Fields: ${(schema.itemFields ?? []).map((field) => field.name).join(', ')}`;
  }

  protected humanise(name: string): string {
    const spaced = name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
  }

  protected text(value: unknown): string {
    if (value === null || value === undefined) return '';
    if (Array.isArray(value)) return value.map((entry) => String(entry)).join('\n');
    if (typeof value === 'object') return JSON.stringify(value, null, 2);
    return String(value);
  }

  protected pretty(value: unknown): string {
    return JSON.stringify(value ?? {}, null, 2);
  }

  protected itemsJson(draft: BlockDraft): string {
    const items = draft.config['items'];
    return items === undefined ? '[]' : JSON.stringify(items, null, 2);
  }

  // ---- Editing ------------------------------------------------------------------------------------

  protected setConfig(blockId: string, field: string, value: string): void {
    this.patchConfig(blockId, field, value === '' ? null : value);
  }

  protected setConfigBoolean(blockId: string, field: string, value: boolean): void {
    this.patchConfig(blockId, field, value);
  }

  protected setConfigNumber(blockId: string, field: string, value: string): void {
    const parsed = Number(value);
    this.patchConfig(blockId, field, value === '' || !Number.isFinite(parsed) ? null : parsed);
  }

  /** A list field is a textarea of lines; a long text field is the same textarea holding one value. */
  protected setConfigText(blockId: string, field: BlockFieldResponse, value: string): void {
    if (!field.isList) {
      this.patchConfig(blockId, field.name, value === '' ? null : value);
      return;
    }

    const entries = value
      .split('\n')
      .map((entry) => entry.trim())
      .filter((entry) => entry.length > 0);
    this.patchConfig(blockId, field.name, entries);
  }

  /**
   * The repeater's stand-in.
   *
   * A block with `itemFields` holds a list of sub-records, and the honest thing until a repeater
   * exists is to edit them as JSON rather than to pretend a single text box is a list of cards.
   * Invalid JSON is left on screen and not written into the draft, so a half-typed bracket does
   * not silently blank the block's items.
   */
  protected setItems(blockId: string, value: string): void {
    try {
      const parsed: unknown = JSON.parse(value);
      if (!Array.isArray(parsed)) return;
      this.patchConfig(blockId, 'items', parsed);
    } catch {
      // Deliberately silent: a keystroke mid-edit is not an error worth interrupting for.
    }
  }

  /** The one place a block's configuration is written, so `dirty` cannot be forgotten. */
  private patchConfig(blockId: string, field: string, value: unknown): void {
    this.blocks.update((current) =>
      current.map((draft) =>
        draft.id === blockId ? { ...draft, config: { ...draft.config, [field]: value } } : draft,
      ),
    );
    this.dirty.set(true);
  }

  protected setVisible(blockId: string, value: boolean): void {
    this.blocks.update((current) =>
      current.map((draft) => (draft.id === blockId ? { ...draft, isVisible: value } : draft)),
    );
    this.dirty.set(true);
  }

  protected setWindow(blockId: string, field: 'startsAt' | 'endsAt', value: string): void {
    this.blocks.update((current) =>
      current.map((draft) => (draft.id === blockId ? { ...draft, [field]: value } : draft)),
    );
    this.dirty.set(true);
  }

  protected addBlock(type: BlockTypeResponse): void {
    this.blocks.update((current) => [
      ...current,
      {
        // Local only, and replaced by the server's id on the next load. `BlockBody.id` is null for
        // a block the server has not seen, which is what tells it this one is new.
        id: `new-${crypto.randomUUID()}`,
        type: type.type,
        config: {},
        isVisible: true,
        startsAt: '',
        endsAt: '',
      },
    ]);
    this.dirty.set(true);
  }

  protected removeBlock(blockId: string): void {
    this.blocks.update((current) => current.filter((draft) => draft.id !== blockId));
    this.dirty.set(true);
  }

  protected reorder(ids: readonly string[]): void {
    const byId = new Map(this.blocks().map((draft) => [draft.id, draft]));
    this.blocks.set(
      ids.map((id) => byId.get(id)).filter((draft): draft is BlockDraft => draft !== undefined),
    );
    this.dirty.set(true);
  }

  protected setTitle(value: string): void {
    this.title.set(value);
    this.dirty.set(true);
  }

  protected setSlug(value: string): void {
    this.slug.set(value);
    this.dirty.set(true);
  }

  protected setSummary(value: string): void {
    this.pageSummary.set(value);
    this.dirty.set(true);
  }

  protected setAuthor(value: string): void {
    this.author.set(value);
    this.dirty.set(true);
  }

  protected setTags(value: string): void {
    this.tags.set(value);
    this.dirty.set(true);
  }

  protected setMetaTitle(value: string): void {
    this.metaTitle.set(value);
    this.dirty.set(true);
  }

  protected setMetaDescription(value: string): void {
    this.metaDescription.set(value);
    this.dirty.set(true);
  }

  protected setCanonical(value: string): void {
    this.canonicalUrl.set(value);
    this.dirty.set(true);
  }

  protected setNoIndex(value: boolean): void {
    this.noIndex.set(value);
    this.dirty.set(true);
  }

  protected chooseCover(files: readonly MediaFileResponse[]): void {
    this.pickerOpen.set(false);
    const file = files[0];
    if (file) this.setCover(file.id);
  }

  protected setCover(fileId: string | null): void {
    this.coverFileId.set(fileId);
    this.dirty.set(true);
  }

  // ---- Saving and the lifecycle -------------------------------------------------------------------

  protected save(): void {
    if (this.busy()) return;

    this.busy.set(true);
    this.summary.set([]);

    const blocks: BlockBody[] = this.blocks().map((draft) => ({
      // A locally-added block has no server id yet, and null is how the API is told so.
      id: draft.id.startsWith('new-') ? null : draft.id,
      type: draft.type,
      config: draft.config,
      isVisible: draft.isVisible,
      startsAt: draft.startsAt ? new Date(draft.startsAt).toISOString() : null,
      endsAt: draft.endsAt ? new Date(draft.endsAt).toISOString() : null,
    }));

    this.content
      .updatePage(this.id, {
        slug: this.slug() || null,
        title: this.title() || null,
        summary: this.pageSummary() || null,
        seo: {
          metaTitle: this.metaTitle() || null,
          metaDescription: this.metaDescription() || null,
          metaKeywords: null,
          canonicalUrl: this.canonicalUrl() || null,
          ogTitle: null,
          ogDescription: null,
          ogImageFileId: null,
          ogType: null,
          noIndex: this.noIndex(),
          noFollow: false,
          sitemapPriority: null,
        },
        coverImageFileId: this.coverFileId(),
        author: this.author() || null,
        tags: this.tags()
          .split(',')
          .map((tag) => tag.trim())
          .filter((tag) => tag.length > 0),
        blocks,
      })
      .subscribe({
        next: (saved) => {
          this.busy.set(false);
          this.dirty.set(false);
          this.fill(saved);
          this.toasts.success('Draft saved.');
        },
        error: (error: unknown) => {
          this.busy.set(false);
          const errors = fieldErrors(error);
          const messages = errors
            ? Object.values(errors).flatMap((entries) => [...entries])
            : [describeError(error, 'It could not be saved.')];
          this.summary.set(messages);
        },
      });
  }

  protected transitionLabel(status: string): string {
    return TRANSITION_LABELS[status] ?? status;
  }

  protected startTransition(status: string): void {
    this.transitionError.set(null);
    this.transitionNote.set('');
    this.scheduledAt.set('');
    this.transitioning.set(status);
  }

  protected transition(): void {
    const status = this.transitioning();
    if (!status || this.busy()) return;

    this.busy.set(true);
    this.transitionError.set(null);

    this.content
      .transitionPage(this.id, {
        status,
        scheduledAt:
          status === 'Scheduled' && this.scheduledAt() ? new Date(this.scheduledAt()).toISOString() : null,
        note: this.transitionNote() || null,
      })
      .subscribe({
        next: (saved) => {
          this.busy.set(false);
          this.transitioning.set(null);
          this.page.set(saved);
          this.loadVersions();
          this.toasts.success(`${this.transitionLabel(status)} — done.`);
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.transitionError.set(describeError(error, 'That transition was refused.'));
        },
      });
  }

  protected openPreview(): void {
    this.preview(undefined);
  }

  protected preview(version?: number): void {
    this.previewOpen.set(true);
    this.previewing.set(true);
    this.previewError.set(null);
    this.previewed.set(null);

    this.content.previewPage(this.id, version).subscribe({
      next: (rendered) => {
        this.previewing.set(false);
        this.previewed.set(rendered);
      },
      error: (error: unknown) => {
        this.previewing.set(false);
        this.previewError.set(describeError(error, 'The preview could not be rendered.'));
      },
    });
  }

  protected rollback(): void {
    const version = this.rollingBackTo();
    if (version === null) return;

    this.busy.set(true);
    this.content.rollbackPage(this.id, version).subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.rollingBackTo.set(null);
        this.dirty.set(false);
        this.fill(saved);
        this.page.set(saved);
        this.toasts.success(`Restored version ${version}.`);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.rollingBackTo.set(null);
        this.summary.set([describeError(error, 'That version could not be restored.')]);
      },
    });
  }

  protected remove(): void {
    this.busy.set(true);
    this.content.deletePage(this.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.deleting.set(false);
        this.dirty.set(false);
        this.toasts.success('Page deleted.');
        void this.router.navigate(['/content/pages']);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.deleting.set(false);
        this.summary.set([describeError(error, 'It could not be deleted.')]);
      },
    });
  }

  // ---- Loading ------------------------------------------------------------------------------------

  private load(): void {
    this.loading.set(true);
    this.content.page(this.id).subscribe({
      next: (loaded) => {
        this.loading.set(false);
        this.page.set(loaded);
        this.fill(loaded);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'That page could not be loaded.'));
      },
    });
  }

  private loadBlockTypes(): void {
    this.content.blockTypes().subscribe({
      next: (types) => this.blockTypes.set(types),
      error: (error: unknown) =>
        this.blockTypesError.set(describeError(error, 'No block can be added until it loads.')),
    });
  }

  private loadVersions(): void {
    this.content.pageVersions(this.id).subscribe({
      next: (versions) => this.versions.set(versions),
      // Not fatal: a page with no readable history is still editable.
      error: () => this.versions.set([]),
    });
  }

  private fill(loaded: PageResponse): void {
    this.page.set(loaded);
    this.title.set(loaded.title);
    this.slug.set(loaded.slug);
    this.pageSummary.set(loaded.summary ?? '');
    this.author.set(loaded.author ?? '');
    this.tags.set(loaded.tags.join(', '));
    this.coverFileId.set(loaded.coverImage?.fileId ?? null);
    this.metaTitle.set(loaded.seo.metaTitle ?? '');
    this.metaDescription.set(loaded.seo.metaDescription ?? '');
    this.canonicalUrl.set(loaded.seo.canonicalUrl ?? '');
    this.noIndex.set(loaded.seo.noIndex);
    this.blocks.set(loaded.blocks.map(toDraft));
  }
}

/** What the identifier-shaped field kinds actually want typed into them. */
const REFERENCE_HINTS: Readonly<Record<string, string>> = {
  MediaRef: 'A media file id',
  ProductRef: 'A product id',
  CategoryRef: 'A category id',
  CollectionRef: "A collection's slug",
  Link: 'An absolute URL, or a path beginning with /',
};

function toDraft(block: BlockResponse): BlockDraft {
  return {
    id: block.id,
    type: block.type,
    config: isRecord(block.config) ? { ...block.config } : {},
    isVisible: block.isVisible,
    startsAt: localInput(block.startsAt),
    endsAt: localInput(block.endsAt),
  };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function localInput(value: string | null): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}
