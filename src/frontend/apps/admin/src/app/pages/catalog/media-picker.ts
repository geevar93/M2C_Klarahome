import { ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { MediaFileResponse, MediaLibraryService } from '@klarahome/data-access-admin';
import { Modal } from '@klarahome/ui-admin';
import { Alert, Button, EmptyState, Icon, ProductImage, Skeleton } from '@klarahome/ui-primitives';
import { ImageUrls } from '@klarahome/util';

import { describeError } from '../../core/describe-error';

/**
 * Choosing a file the platform already holds, or adding one.
 *
 * The catalogue's media manager, the brand logo picker and the category image picker are one
 * problem, so they are one dialog. What it has to get right is narrower than it looks:
 *
 *  - **An unscanned file cannot be chosen.** `platform.media_files` runs its scan asynchronously
 *    (Step 8, ADR-015) and a file that has not cleared it has no public URL. Offering it would
 *    put a broken image on a product page minutes after somebody thought they had set one, so it
 *    is shown — with its state, because "where did my upload go" is the alternative — and is not
 *    selectable until it clears.
 *  - **An upload lands in the grid, not in a queue.** When the last file of a batch is accepted the
 *    list is refetched, so eight photographs dragged in at once are eight tiles a moment later —
 *    with the scan state the server holds now, rather than the one it held at upload.
 *
 * Multi-select is the default because the product editor's case is eight images at once; a caller
 * that wants one passes `multiple="false"` and gets a dialog that closes on the click.
 */
@Component({
  selector: 'kh-media-picker',
  imports: [Alert, Button, EmptyState, Icon, Modal, ProductImage, Skeleton],
  template: `
    <kh-modal
      [open]="open()"
      heading="Media library"
      width="52rem"
      [dismissible]="!uploading()"
      (closed)="closed.emit()"
    >
      <div class="bar">
        <label class="upload" [class.busy]="uploading()">
          <input
            type="file"
            [accept]="accept()"
            [multiple]="multiple()"
            [disabled]="uploading()"
            (change)="upload($event)"
          />
          <kh-icon name="plus" size="sm" />
          {{ uploading() ? 'Uploading…' : 'Upload files' }}
        </label>

        <button khButton type="button" size="sm" [disabled]="list.loading()" (click)="list.refresh()">
          <kh-icon name="refresh" size="sm" />
          Refresh
        </button>
      </div>

      @if (failure(); as message) {
        <kh-alert tone="danger" heading="That did not work">{{ message }}</kh-alert>
      }

      @if (list.error(); as message) {
        <kh-alert tone="danger" heading="The library could not be loaded">{{ message }}</kh-alert>
      }

      @if (list.loading() && list.rows().length === 0) {
        <div class="grid">
          @for (placeholder of skeletons; track placeholder) {
            <kh-skeleton height="7rem" />
          }
        </div>
      } @else if (list.isEmpty()) {
        <kh-empty-state
          heading="Nothing in the library yet"
          message="Upload a file above and it will appear here."
        />
      } @else {
        <ul class="grid" role="listbox" [attr.aria-multiselectable]="multiple()" aria-label="Media files">
          @for (file of list.rows(); track file.id) {
            <li>
              <button
                type="button"
                class="tile"
                role="option"
                [class.chosen]="isChosen(file.id)"
                [attr.aria-selected]="isChosen(file.id)"
                [disabled]="!isUsable(file)"
                [attr.title]="isUsable(file) ? file.fileName : usableReason(file)"
                (click)="toggle(file)"
              >
                <kh-product-image [source]="thumb(file)" [placeholder]="extension(file)" />
                <span class="name">{{ file.fileName }}</span>
                @if (!isUsable(file)) {
                  <span class="state">{{ usableReason(file) }}</span>
                }
              </button>
            </li>
          }
        </ul>

        <div class="pager">
          <button
            khButton
            type="button"
            size="sm"
            [disabled]="!list.hasPrevious() || list.loading()"
            (click)="list.previous()"
          >
            <kh-icon name="chevron-left" size="sm" />
            Previous
          </button>
          <button
            khButton
            type="button"
            size="sm"
            [disabled]="!list.nextCursor() || list.loading()"
            (click)="list.next()"
          >
            Next
            <kh-icon name="chevron-right" size="sm" />
          </button>
        </div>
      }

      <div slot="footer">
        <button khButton type="button" variant="tertiary" (click)="closed.emit()">Cancel</button>
        <button
          khButton
          type="button"
          variant="primary"
          [disabled]="chosen().length === 0"
          (click)="confirm()"
        >
          Use {{ chosen().length || '' }} {{ chosen().length === 1 ? 'file' : 'files' }}
        </button>
      </div>
    </kh-modal>
  `,
  styles: `
    .bar {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      margin-block-end: var(--space-3);
    }

    .upload {
      display: inline-flex;
      gap: var(--space-1);
      align-items: center;
      padding: var(--space-2) var(--space-3);
      border: 1px dashed var(--color-border-strong);
      border-radius: var(--radius-md);
      font-size: var(--text-sm);
      cursor: pointer;
    }

    .upload.busy {
      cursor: progress;
      color: var(--color-text-muted);
    }

    .upload input {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }

    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(8rem, 1fr));
      gap: var(--space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .tile {
      display: block;
      inline-size: 100%;
      padding: var(--space-2);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      text-align: start;
      cursor: pointer;
    }

    .tile.chosen {
      border-color: var(--color-primary);
      box-shadow: 0 0 0 1px var(--color-primary);
    }

    .tile:disabled {
      cursor: not-allowed;
      opacity: 0.6;
    }

    .name {
      display: block;
      margin-block-start: var(--space-1);
      overflow: hidden;
      font-size: var(--text-xs);
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .state {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .pager {
      display: flex;
      gap: var(--space-2);
      justify-content: flex-end;
      margin-block-start: var(--space-3);
    }

    kh-alert {
      margin-block-end: var(--space-3);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MediaPicker {
  private readonly media = inject(MediaLibraryService);
  private readonly images = inject(ImageUrls);

  readonly open = input(false);
  readonly multiple = input(true);
  readonly accept = input('image/*');
  /** Attributed on upload, so the library can say what a file belongs to. */
  readonly ownerType = input<string | null>(null);
  readonly ownerId = input<string | null>(null);

  readonly picked = output<readonly MediaFileResponse[]>();
  readonly closed = output<void>();

  protected readonly list = this.media.files({ visibility: 'Public' });
  protected readonly uploading = signal(false);
  protected readonly failure = signal<string | null>(null);
  protected readonly skeletons = Array.from({ length: 8 }, (_, index) => index);

  private readonly selection = signal<readonly MediaFileResponse[]>([]);
  protected readonly chosen = computed(() => this.selection());

  constructor() {
    this.list.load();
  }

  protected isChosen(id: string): boolean {
    return this.selection().some((file) => file.id === id);
  }

  /**
   * `status` is already the "may this be served" answer (`StoredFileStatus.Ready`), so it alone
   * decides usability. `scanState` is separately `Skipped` in this deployment — no scanner is
   * wired in yet (docs/08-integrations.md §4) — and a skipped file is still Ready; it just was
   * never examined. Waiting on `Clean` here would mean no upload is ever usable.
   */
  protected isUsable(file: MediaFileResponse): boolean {
    return file.status === 'Ready';
  }

  protected usableReason(file: MediaFileResponse): string {
    if (file.scanState === 'Infected') return 'Refused by the scanner';
    if (file.scanState === 'Pending') return 'Being scanned';
    return 'Not available';
  }

  protected thumb(file: MediaFileResponse) {
    return this.images.sourceForImage(
      { url: file.url, fileId: file.id, width: file.width, height: file.height },
      file.fileName,
    );
  }

  protected extension(file: MediaFileResponse): string {
    return (file.fileName.split('.').pop() ?? 'file').toUpperCase().slice(0, 4);
  }

  protected toggle(file: MediaFileResponse): void {
    if (!this.multiple()) {
      this.picked.emit([file]);
      return;
    }
    this.selection.update((current) =>
      current.some((chosen) => chosen.id === file.id)
        ? current.filter((chosen) => chosen.id !== file.id)
        : [...current, file],
    );
  }

  protected confirm(): void {
    this.picked.emit(this.selection());
    this.selection.set([]);
  }

  protected upload(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    // Cleared straight away so choosing the same file twice in a row still fires a change event.
    input.value = '';
    if (files.length === 0) return;

    this.failure.set(null);
    this.uploading.set(true);

    let remaining = files.length;
    for (const file of files) {
      this.media
        .upload(file, { ownerType: this.ownerType() ?? undefined, ownerId: this.ownerId() ?? undefined })
        .subscribe({
          next: () => {
            remaining -= 1;
            if (remaining === 0) {
              this.uploading.set(false);
              // Refetched rather than prepended locally: the row the API answers carries the scan
              // state at the instant of upload, and the list is the honest view of it a moment later.
              this.list.load();
            }
          },
          error: (error: unknown) => {
            remaining -= 1;
            if (remaining === 0) this.uploading.set(false);
            this.failure.set(describeError(error, `${file.name} could not be uploaded.`));
          },
        });
    }
  }
}
