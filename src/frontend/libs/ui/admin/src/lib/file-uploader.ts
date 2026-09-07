import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { Button, Icon, ProgressBar } from '@klarahome/ui-primitives';

import { UploadItem } from './admin.model';

/**
 * Picks files, and shows what became of them.
 *
 * **It uploads nothing.** The admin's uploads go to two different places with two different
 * contracts — `POST /admin/media` for catalogue imagery, and a module's own endpoint for a KYC
 * document or a CSV import — so the transport belongs to the screen and the picking belongs here.
 * The caller owns the `items` list and updates each entry's status as its request progresses.
 *
 * What this does own is the four things a hand-written file input gets wrong:
 *
 *  - **A real `<input type="file">`, kept in the DOM and merely hidden.** A drop zone with no
 *    input behind it cannot be reached by a keyboard at all, which is the most common way an
 *    upload control ends up inaccessible.
 *  - **Type and size are checked before anything is sent.** A 40 MB photograph refused after it
 *    has uploaded on a tethered connection is a minute of somebody's day.
 *  - **Dragging is an enhancement, never the only route.** The button does the same job.
 *  - **Failures stay on screen with their reason**, next to the file that failed, rather than as
 *    a toast that has already gone by the time the user looks up.
 */
@Component({
  selector: 'kh-file-uploader',
  imports: [Button, Icon, ProgressBar],
  template: `
    <div
      class="zone"
      [class.over]="dragging()"
      (dragover)="onDragOver($event)"
      (dragleave)="dragging.set(false)"
      (drop)="onDrop($event)"
    >
      <kh-icon name="download" />
      <p class="lead">{{ label() }}</p>
      <p class="hint">{{ hint() }}</p>

      <!-- The button drives the real input through its template reference. The input is the
           control that is actually focusable and operable; this is the visible affordance. -->
      <button khButton type="button" size="sm" [disabled]="disabled()" (click)="picker.click()">
        Choose {{ multiple() ? 'files' : 'a file' }}
      </button>

      <!-- Visually hidden rather than display:none - a hidden input is not focusable, and this
           one is the control a screen-reader user actually operates. -->
      <input
        #picker
        class="kh-visually-hidden"
        type="file"
        [attr.accept]="accept()"
        [multiple]="multiple()"
        [disabled]="disabled()"
        [attr.aria-label]="label()"
        (change)="onPicked($event)"
      />
    </div>

    @if (rejected().length > 0) {
      <ul class="rejected" role="alert">
        @for (message of rejected(); track message) {
          <li>{{ message }}</li>
        }
      </ul>
    }

    @if (items().length > 0) {
      <ul class="items">
        @for (item of items(); track item.id) {
          <li>
            <div class="item-head">
              <span class="name">{{ item.name }}</span>
              <span class="size">{{ readableSize(item.sizeBytes) }}</span>
              <button
                khButton
                type="button"
                variant="tertiary"
                size="sm"
                [iconOnly]="true"
                [attr.aria-label]="'Remove ' + item.name"
                (click)="removed.emit(item.id)"
              >
                <kh-icon name="close" size="sm" />
              </button>
            </div>

            @if (item.status === 'uploading') {
              <kh-progress-bar [label]="'Uploading ' + item.name" />
            }
            @if (item.status === 'failed') {
              <p class="error">{{ item.error ?? 'That file could not be uploaded.' }}</p>
            }
            @if (item.status === 'done') {
              <p class="done">Uploaded</p>
            }
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .zone {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      align-items: center;
      padding: var(--space-8) var(--space-4);
      border: 1px dashed var(--color-border-strong);
      border-radius: var(--radius-md);
      background: var(--color-surface);
      text-align: center;
    }

    .zone.over {
      border-color: var(--color-primary);
      background: var(--color-primary-subtle);
    }

    .lead {
      margin: 0;
      font-weight: var(--weight-medium);
    }

    .hint,
    .size {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .hint {
      margin: 0;
    }

    .items,
    .rejected {
      margin: var(--space-3) 0 0;
      padding: 0;
      list-style: none;
    }

    .items li {
      padding: var(--space-2) 0;
      border-block-end: 1px solid var(--color-border);
    }

    .item-head {
      display: flex;
      gap: var(--space-2);
      align-items: center;
    }

    .name {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-size: var(--text-sm);
    }

    .error,
    .rejected {
      color: var(--color-danger);
      font-size: var(--text-xs);
    }

    .done {
      color: var(--color-success);
      font-size: var(--text-xs);
    }

    .error,
    .done {
      margin: var(--space-1) 0 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FileUploader {
  readonly label = input('Drag files here, or choose them');
  readonly hint = input('');
  /** The `accept` attribute, e.g. `image/*,.csv`. Also enforced here before anything is sent. */
  readonly accept = input<string | null>(null);
  readonly multiple = input(false);
  readonly maxSizeMb = input(10);
  readonly disabled = input(false);
  /** The files the caller is tracking, and what has become of each. */
  readonly items = input<readonly UploadItem[]>([]);

  /** Files that passed the type and size checks. The caller uploads them. */
  readonly picked = output<readonly File[]>();
  readonly removed = output<string>();

  protected readonly dragging = signal(false);
  protected readonly rejected = signal<readonly string[]>([]);

  private readonly maxBytes = computed(() => this.maxSizeMb() * 1024 * 1024);

  protected onDragOver(event: DragEvent): void {
    if (this.disabled()) return;
    // Without both of these the browser navigates to the dropped file, which looks exactly like
    // the application crashing.
    event.preventDefault();
    this.dragging.set(true);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    if (this.disabled()) return;
    this.accept_(Array.from(event.dataTransfer?.files ?? []));
  }

  protected onPicked(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.accept_(Array.from(input.files ?? []));
    // Cleared so that choosing the same file twice in a row still fires a change event — which is
    // exactly what somebody does after a failed upload.
    input.value = '';
  }

  protected readableSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} kB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  /** Filters what was chosen, and says why anything was refused. */
  private accept_(files: readonly File[]): void {
    const refusals: string[] = [];
    const kept: File[] = [];

    for (const file of files) {
      if (file.size > this.maxBytes()) {
        refusals.push(
          `${file.name} is ${this.readableSize(file.size)}; the limit is ${this.maxSizeMb()} MB.`,
        );
        continue;
      }
      if (!this.matchesAccept(file)) {
        refusals.push(`${file.name} is not a file type this accepts.`);
        continue;
      }
      kept.push(file);
    }

    this.rejected.set(refusals);
    if (kept.length > 0) this.picked.emit(this.multiple() ? kept : kept.slice(0, 1));
  }

  /**
   * Whether a file matches the `accept` list.
   *
   * Both spellings the attribute allows: an extension (`.csv`) and a MIME type with or without a
   * wildcard (`image/*`). The check is a courtesy — the API validates the content, not the name,
   * because a `.png` extension proves nothing about what is inside the file.
   */
  private matchesAccept(file: File): boolean {
    const accept = this.accept();
    if (!accept) return true;

    return accept
      .split(',')
      .map((entry) => entry.trim().toLowerCase())
      .filter((entry) => entry.length > 0)
      .some((entry) => {
        if (entry.startsWith('.')) return file.name.toLowerCase().endsWith(entry);
        if (entry.endsWith('/*')) return file.type.toLowerCase().startsWith(entry.slice(0, -1));
        return file.type.toLowerCase() === entry;
      });
  }
}
