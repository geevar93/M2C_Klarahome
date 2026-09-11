import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  BannerAudience,
  BannerBody,
  BannerFilters,
  BannerPlacement,
  BannerResponse,
  ContentAdminService,
  MediaFileResponse,
} from '@klarahome/data-access-admin';
import {
  CellTemplate,
  ConfirmDialog,
  DataTable,
  DataTableColumn,
  EntityDrawer,
  FilterBar,
  FilterDefinition,
  FilterValues,
  FormShell,
  PageHeader,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Control, Field, Icon, ProductImage } from '@klarahome/ui-primitives';
import { ImageUrls, ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';
import { MediaPicker } from '../catalog/media-picker';
import { BANNER_AUDIENCES, BANNER_PLACEMENTS } from './content-vocabulary';

/**
 * Banners — the promotional furniture, on its own timetable.
 *
 * **`isActive` and `isLive` are both on the row, and they are not the same thing.** A banner is
 * active because somebody switched it on and live because the clock is inside its window; the API
 * computes the second, so this screen shows it rather than deriving a different answer. The
 * commonest support question a merchandiser gets — "why can I not see my banner" — is almost
 * always one of those two being false, and the column says which.
 *
 * **The announcement bar is a banner too.** It is a placement, not a feature: the strip above the
 * header is the same record with the same window and the same audience rule, which is why there is
 * no separate screen for it.
 *
 * A mobile image is offered separately because a hero cropped for a desktop viewport is unreadable
 * on a phone, and the storefront is mobile-first — the alternative is one image that is wrong on
 * the device most people use.
 */
@Component({
  selector: 'kh-banners-page',
  imports: [
    Alert,
    Badge,
    Button,
    CellTemplate,
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
    <kh-page-header heading="Banners" description="What appears where, to whom, and between when.">
      <button khButton type="button" variant="primary" (click)="startCreate()">
        <kh-icon name="plus" size="sm" />
        New banner
      </button>
    </kh-page-header>

    @if (list.error(); as message) {
      <kh-alert tone="danger" heading="Banners could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not take">{{ message }}</kh-alert>
    }

    <kh-data-table
      label="Banners"
      [columns]="columns"
      [rows]="list.rows()"
      [rowKey]="rowKey"
      [rowLabel]="rowLabel"
      [loading]="list.loading()"
      [page]="page()"
      [configurable]="true"
      storageKey="content-banners"
      exportMode="page"
      emptyMessage="No banner matches these filters."
      (nextPage)="list.next()"
      (previousPage)="list.previous()"
    >
      <kh-filter-bar
        slot="filters"
        [filters]="filters"
        [values]="values()"
        [searchable]="false"
        (changed)="applyFilters($event)"
      />

      <ng-template khCell="name" let-row>
        <button type="button" class="link" (click)="startEdit(row)">{{ row.name }}</button>
        <span class="note">{{ placementLabel(row.placement) }}</span>
      </ng-template>

      <ng-template khCell="state" let-row>
        <kh-badge [tone]="row.isLive ? 'success' : row.isActive ? 'warning' : 'neutral'">
          {{ row.isLive ? 'Live' : row.isActive ? 'On, out of window' : 'Off' }}
        </kh-badge>
        <span class="note">{{ windowLabel(row) }}</span>
      </ng-template>

      <ng-template khCell="actions" let-row>
        <button
          khButton
          type="button"
          size="sm"
          variant="tertiary"
          [disabled]="busyId() === row.id"
          (click)="toggle(row)"
        >
          {{ row.isActive ? 'Switch off' : 'Switch on' }}
        </button>
      </ng-template>
    </kh-data-table>

    @if (drawerOpen()) {
      <kh-entity-drawer
        [heading]="editing() ? 'Edit banner' : 'New banner'"
        [subtitle]="editing()?.placement ?? null"
        (closed)="drawerOpen.set(false)"
      >
        <kh-form-shell
          heading="Banner"
          [summary]="summary()"
          [saving]="saving()"
          [dirty]="true"
          [submitLabel]="editing() ? 'Save' : 'Create'"
          (submitted)="save()"
          (cancelled)="drawerOpen.set(false)"
        >
          <kh-field
            label="Name"
            for="banner-name"
            [error]="form.fields.name.error()"
            hint="For your own reference; not shown to shoppers."
          >
            <input
              khControl
              id="banner-name"
              type="text"
              maxlength="120"
              [value]="form.fields.name.value()"
              (input)="form.fields.name.set($any($event.target).value)"
              (touched)="form.fields.name.markTouched()"
            />
          </kh-field>

          <kh-field label="Placement" for="banner-placement" [hint]="placementHint()">
            <select
              khControl
              id="banner-placement"
              [value]="placement()"
              (change)="placement.set($any($event.target).value)"
            >
              @for (choice of placements; track choice.value) {
                <option [value]="choice.value">{{ choice.label }}</option>
              }
            </select>
          </kh-field>

          <kh-field
            label="Message"
            for="banner-message"
            [optional]="true"
            hint="The only content an announcement bar has."
          >
            <textarea
              khControl
              id="banner-message"
              rows="2"
              [value]="form.fields.message.value()"
              (input)="form.fields.message.set($any($event.target).value)"
            ></textarea>
          </kh-field>

          <div class="images">
            <div class="image">
              <span class="image-label">Image</span>
              <kh-product-image [source]="imageSource()" placeholder="No image" />
              <div class="image-actions">
                <button khButton type="button" size="sm" (click)="openPicker('desktop')">Choose</button>
                @if (mediaFileId()) {
                  <button khButton type="button" size="sm" variant="tertiary" (click)="mediaFileId.set(null)">
                    Remove
                  </button>
                }
              </div>
            </div>

            <div class="image">
              <span class="image-label">Image for phones</span>
              <kh-product-image [source]="mobileSource()" placeholder="Falls back to the image above" />
              <div class="image-actions">
                <button khButton type="button" size="sm" (click)="openPicker('mobile')">Choose</button>
                @if (mobileMediaFileId()) {
                  <button
                    khButton
                    type="button"
                    size="sm"
                    variant="tertiary"
                    (click)="mobileMediaFileId.set(null)"
                  >
                    Remove
                  </button>
                }
              </div>
            </div>
          </div>

          <kh-field
            label="Alt text"
            for="banner-alt"
            [optional]="true"
            hint="What the image says, for anybody who cannot see it."
          >
            <input
              khControl
              id="banner-alt"
              type="text"
              maxlength="200"
              [value]="form.fields.altText.value()"
              (input)="form.fields.altText.set($any($event.target).value)"
            />
          </kh-field>

          <div class="row">
            <kh-field
              label="Links to"
              for="banner-link"
              [optional]="true"
              hint="A path such as /collections/sale."
            >
              <input
                khControl
                id="banner-link"
                type="text"
                [value]="form.fields.link.value()"
                (input)="form.fields.link.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Button label" for="banner-cta" [optional]="true">
              <input
                khControl
                id="banner-cta"
                type="text"
                maxlength="40"
                [value]="form.fields.ctaLabel.value()"
                (input)="form.fields.ctaLabel.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <div class="row">
            <kh-field label="Shows from" for="banner-starts" [optional]="true">
              <input
                khControl
                id="banner-starts"
                type="datetime-local"
                [value]="startsAt()"
                (input)="startsAt.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Shows until" for="banner-ends" [optional]="true">
              <input
                khControl
                id="banner-ends"
                type="datetime-local"
                [value]="endsAt()"
                (input)="endsAt.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <div class="row">
            <kh-field label="Who sees it" for="banner-audience">
              <select
                khControl
                id="banner-audience"
                [value]="audience()"
                (change)="audience.set($any($event.target).value)"
              >
                @for (choice of audiences; track choice.value) {
                  <option [value]="choice.value">{{ choice.label }}</option>
                }
              </select>
            </kh-field>
            <kh-field label="Priority" for="banner-priority" hint="Lower shows first in a placement.">
              <input
                khControl
                id="banner-priority"
                type="number"
                min="0"
                [value]="form.fields.priority.value()"
                (input)="form.fields.priority.set($any($event.target).value)"
              />
            </kh-field>
          </div>
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
      [open]="pickerFor() !== null"
      [multiple]="false"
      ownerType="Banner"
      [ownerId]="editing()?.id ?? null"
      (picked)="chooseImage($event)"
      (closed)="pickerFor.set(null)"
    />

    <kh-confirm-dialog
      [open]="deleting()"
      heading="Delete this banner"
      message="Switching it off takes it down and keeps the record. Deleting does not."
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

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    /* Two fields side by side — one row here is a pair of \`datetime-local\` inputs, each with a
       large browser-drawn intrinsic minimum width. Wraps to one per line rather than overflowing a
       360px screen. */
    .row {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-3);
    }

    .row > kh-field {
      flex: 1 1 12rem;
      min-inline-size: 0;
    }

    .images {
      display: flex;
      gap: var(--space-4);
      flex-wrap: wrap;
      margin-block: var(--space-4);
    }

    .image {
      flex: 1 1 12rem;
    }

    .image-label {
      display: block;
      margin-block-end: var(--space-1);
      font-weight: var(--weight-medium);
      font-size: var(--text-sm);
    }

    .image-actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin-block-start: var(--space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BannersPage {
  private readonly content = inject(ContentAdminService);
  private readonly images = inject(ImageUrls);
  private readonly toasts = inject(ToastService);

  protected readonly placements = BANNER_PLACEMENTS;
  protected readonly audiences = BANNER_AUDIENCES;

  protected readonly list = this.content.banners();
  protected readonly values = signal<FilterValues>({});
  protected readonly saving = signal(false);
  protected readonly busyId = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly drawerOpen = signal(false);
  protected readonly deleting = signal(false);
  protected readonly editing = signal<BannerResponse | null>(null);
  protected readonly placement = signal<BannerPlacement>('HomeHero');
  protected readonly audience = signal<BannerAudience>('None');
  protected readonly startsAt = signal('');
  protected readonly endsAt = signal('');
  protected readonly mediaFileId = signal<string | null>(null);
  protected readonly mobileMediaFileId = signal<string | null>(null);
  protected readonly pickerFor = signal<'desktop' | 'mobile' | null>(null);

  private readonly imageUrl = signal<string | null>(null);
  private readonly mobileUrl = signal<string | null>(null);
  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('A name')], this.submitted),
    message: formField('', [], this.submitted),
    altText: formField('', [], this.submitted),
    link: formField('', [], this.submitted),
    ctaLabel: formField('', [], this.submitted),
    priority: formField('100', [], this.submitted),
  });

  protected readonly page = computed(() => ({
    nextCursor: this.list.nextCursor(),
    hasPrevious: this.list.hasPrevious(),
    size: this.list.size(),
    total: this.list.total(),
  }));

  protected readonly placementHint = computed(
    () => this.placements.find((choice) => choice.value === this.placement())?.hint ?? '',
  );

  protected readonly imageSource = computed(() =>
    this.images.sourceForImage({ url: this.imageUrl(), fileId: this.mediaFileId() }, 'Banner image'),
  );

  protected readonly mobileSource = computed(() =>
    this.images.sourceForImage(
      { url: this.mobileUrl(), fileId: this.mobileMediaFileId() },
      'Banner image for phones',
    ),
  );

  protected readonly rowKey = (row: BannerResponse) => row.id;
  protected readonly rowLabel = (row: BannerResponse) => row.name;

  protected readonly columns: readonly DataTableColumn<BannerResponse>[] = [
    { key: 'name', label: 'Banner', kind: 'custom' },
    { key: 'state', label: 'State', kind: 'custom', width: '16rem' },
    {
      key: 'audience',
      label: 'Audience',
      value: (row) => this.audienceLabel(row.audience),
      width: '12rem',
    },
    { key: 'priority', label: 'Priority', kind: 'number', value: (row) => row.priority, width: '6rem' },
    { key: 'link', label: 'Links to', value: (row) => row.link ?? '—', hiddenByDefault: true },
    {
      key: 'updatedAt',
      label: 'Edited',
      kind: 'date',
      value: (row) => tableDateTime(row.updatedAt),
      hiddenByDefault: true,
    },
    { key: 'actions', label: '', kind: 'custom', width: '8rem' },
  ];

  protected readonly filters: readonly FilterDefinition[] = [
    {
      key: 'placement',
      label: 'Placement',
      kind: 'select',
      options: BANNER_PLACEMENTS.map((entry) => ({ value: entry.value, label: entry.label })),
    },
    {
      key: 'activeOnly',
      label: 'State',
      kind: 'select',
      options: [{ value: 'true', label: 'Switched on only' }],
    },
  ];

  constructor() {
    this.list.load();
  }

  protected placementLabel(value: string): string {
    return this.placements.find((choice) => choice.value === value)?.label ?? value;
  }

  protected audienceLabel(value: string): string {
    return this.audiences.find((choice) => choice.value === value)?.label ?? value;
  }

  protected windowLabel(row: BannerResponse): string {
    if (!row.startsAt && !row.endsAt) return 'No window';
    const from = row.startsAt ? tableDateTime(row.startsAt) : 'always';
    const to = row.endsAt ? tableDateTime(row.endsAt) : 'no end';
    return `${from} → ${to}`;
  }

  protected applyFilters(values: FilterValues): void {
    this.values.set(values);
    const filters: BannerFilters = {
      placement: (values['placement'] as BannerPlacement) || undefined,
      activeOnly: values['activeOnly'] === 'true' ? true : undefined,
    };
    this.list.setFilters(filters);
  }

  protected openPicker(which: 'desktop' | 'mobile'): void {
    this.pickerFor.set(which);
  }

  protected chooseImage(files: readonly MediaFileResponse[]): void {
    const which = this.pickerFor();
    this.pickerFor.set(null);
    const file = files[0];
    if (!file || !which) return;

    if (which === 'desktop') {
      this.mediaFileId.set(file.id);
      this.imageUrl.set(file.url);
    } else {
      this.mobileMediaFileId.set(file.id);
      this.mobileUrl.set(file.url);
    }
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.placement.set('HomeHero');
    this.audience.set('None');
    this.startsAt.set('');
    this.endsAt.set('');
    this.mediaFileId.set(null);
    this.mobileMediaFileId.set(null);
    this.imageUrl.set(null);
    this.mobileUrl.set(null);
    this.summary.set([]);
    this.form.reset({ name: '', message: '', altText: '', link: '', ctaLabel: '', priority: '100' });
    this.drawerOpen.set(true);
  }

  protected startEdit(row: BannerResponse): void {
    this.editing.set(row);
    this.placement.set(row.placement);
    this.audience.set(row.audience);
    this.startsAt.set(localInput(row.startsAt));
    this.endsAt.set(localInput(row.endsAt));
    this.mediaFileId.set(row.image?.fileId ?? null);
    this.mobileMediaFileId.set(row.mobileImage?.fileId ?? null);
    this.imageUrl.set(row.image?.url ?? null);
    this.mobileUrl.set(row.mobileImage?.url ?? null);
    this.summary.set([]);
    this.form.reset({
      name: row.name,
      message: row.message ?? '',
      altText: row.altText ?? '',
      link: row.link ?? '',
      ctaLabel: row.ctaLabel ?? '',
      priority: String(row.priority),
    });
    this.drawerOpen.set(true);
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    const body: BannerBody = {
      name: values.name,
      placement: this.placement(),
      mediaFileId: this.mediaFileId(),
      mobileMediaFileId: this.mobileMediaFileId(),
      message: values.message || null,
      altText: values.altText || null,
      link: values.link || null,
      ctaLabel: values.ctaLabel || null,
      priority: Number(values.priority) || 0,
      startsAt: this.startsAt() ? new Date(this.startsAt()).toISOString() : null,
      endsAt: this.endsAt() ? new Date(this.endsAt()).toISOString() : null,
      audience: this.audience(),
      // A new banner is created switched on; an edit keeps whatever the row already says, because
      // switching one off is its own endpoint and this form must not undo it.
      isActive: this.editing()?.isActive ?? true,
    };

    this.saving.set(true);
    this.summary.set([]);

    const existing = this.editing();
    const request = existing ? this.content.updateBanner(existing.id, body) : this.content.createBanner(body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.drawerOpen.set(false);
        this.toasts.success(existing ? 'Banner saved.' : 'Banner created.');
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

  protected toggle(row: BannerResponse): void {
    this.busyId.set(row.id);
    this.actionError.set(null);

    this.content.setBannerActive(row.id, !row.isActive).subscribe({
      next: () => {
        this.busyId.set(null);
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.busyId.set(null);
        this.actionError.set(describeError(error, 'That banner could not be changed.'));
      },
    });
  }

  protected remove(): void {
    const existing = this.editing();
    if (!existing) return;

    this.saving.set(true);
    this.content.deleteBanner(existing.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.deleting.set(false);
        this.drawerOpen.set(false);
        this.toasts.success('Banner deleted.');
        this.list.refresh();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.deleting.set(false);
        this.summary.set([describeError(error, 'It could not be deleted.')]);
      },
    });
  }
}

function localInput(value: string | null): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}
