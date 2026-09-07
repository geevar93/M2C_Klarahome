import { ChangeDetectionStrategy, Component, inject, input, model, signal } from '@angular/core';
import { MediaFileResponse, MediaPayload } from '@klarahome/data-access-admin';
import { Button, Control, Field, Icon, ProductImage } from '@klarahome/ui-primitives';
import { ImageUrls } from '@klarahome/util';

import { MediaPicker } from './media-picker';

/**
 * The ordered images on a product or a variant.
 *
 * `MediaPayload` is `{ fileId, kind, altText, position }`, and every one of those four is a
 * decision somebody has to be able to make:
 *
 *  - **Order is `position`, and position is the array index.** The first image is the one the
 *    storefront's card and the PDP's gallery open with, so "make this the main photograph" has to
 *    be one click rather than a numeric field somebody has to renumber around. Move up / move down
 *    rather than drag: a keyboard user can reorder, which drag-and-drop alone never allows, and
 *    the positions are rewritten from the array on every change so they cannot go out of step.
 *  - **Alt text is edited here, next to the image.** It is a legal accessibility requirement and a
 *    ranking signal, and it is the field that gets skipped when it lives on another screen.
 *
 * The value is a `model()` — the editor above owns the list and this mutates it — because the
 * media are saved by the product's own save, not by a request of their own.
 */
@Component({
  selector: 'kh-media-manager',
  imports: [Button, Control, Field, Icon, MediaPicker, ProductImage],
  template: `
    <div class="head">
      <p class="hint">
        The first image is the one shoppers see first. Every image needs alt text describing what it shows.
      </p>
      <button khButton type="button" size="sm" (click)="pickerOpen.set(true)">
        <kh-icon name="plus" size="sm" />
        Add images
      </button>
    </div>

    @if (media().length === 0) {
      <p class="empty">No images yet. A product with no image will not sell.</p>
    } @else {
      <ul>
        @for (item of media(); track item.fileId; let index = $index) {
          <li>
            <kh-product-image [source]="thumb(item)" [placeholder]="'Image ' + (index + 1)" />

            <div class="detail">
              <kh-field
                [label]="'Alt text for image ' + (index + 1)"
                [for]="idPrefix() + '-alt-' + index"
                hint="What the picture shows, in a few words."
              >
                <input
                  khControl
                  [id]="idPrefix() + '-alt-' + index"
                  type="text"
                  maxlength="200"
                  [value]="item.altText ?? ''"
                  (input)="setAlt(index, $any($event.target).value)"
                />
              </kh-field>
            </div>

            <div class="controls">
              @if (index === 0) {
                <span class="primary">Main image</span>
              }
              <button
                khButton
                type="button"
                size="sm"
                variant="tertiary"
                aria-label="Move earlier"
                [disabled]="index === 0"
                (click)="move(index, -1)"
              >
                <kh-icon name="chevron-up" size="sm" />
              </button>
              <button
                khButton
                type="button"
                size="sm"
                variant="tertiary"
                aria-label="Move later"
                [disabled]="index === media().length - 1"
                (click)="move(index, 1)"
              >
                <kh-icon name="chevron-down" size="sm" />
              </button>
              <button
                khButton
                type="button"
                size="sm"
                variant="danger"
                aria-label="Remove image"
                (click)="remove(index)"
              >
                <kh-icon name="trash" size="sm" />
              </button>
            </div>
          </li>
        }
      </ul>
    }

    <kh-media-picker
      [open]="pickerOpen()"
      [ownerType]="ownerType()"
      [ownerId]="ownerId()"
      (picked)="add($event)"
      (closed)="pickerOpen.set(false)"
    />
  `,
  styles: `
    .head {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      justify-content: space-between;
      margin-block-end: var(--space-3);
    }

    .hint,
    .empty {
      margin: 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    ul {
      display: grid;
      gap: var(--space-3);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    li {
      display: grid;
      grid-template-columns: 5rem 1fr auto;
      gap: var(--space-3);
      align-items: start;
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    .detail kh-field {
      margin-block-end: 0;
    }

    .controls {
      display: flex;
      gap: var(--space-1);
      align-items: center;
    }

    .primary {
      margin-inline-end: var(--space-2);
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MediaManager {
  private readonly images = inject(ImageUrls);

  readonly media = model.required<readonly MediaPayload[]>();
  /** Distinguishes this manager's control ids from another on the same screen. */
  readonly idPrefix = input('media');
  readonly ownerType = input<string | null>(null);
  readonly ownerId = input<string | null>(null);

  protected readonly pickerOpen = signal(false);

  /** The URLs the picker answered, so a just-added image renders before the product is saved. */
  private readonly resolved = new Map<string, string>();

  protected thumb(item: MediaPayload) {
    return this.images.sourceForImage(
      { url: this.resolved.get(item.fileId) ?? null, fileId: item.fileId, alt: item.altText },
      'Product image',
    );
  }

  protected add(files: readonly MediaFileResponse[]): void {
    this.pickerOpen.set(false);
    if (files.length === 0) return;

    for (const file of files) if (file.url) this.resolved.set(file.id, file.url);

    const existing = new Set(this.media().map((item) => item.fileId));
    const additions = files
      .filter((file) => !existing.has(file.id))
      .map((file, offset) => ({
        fileId: file.id,
        kind: 'Image' as const,
        altText: null,
        position: this.media().length + offset,
      }));

    this.media.set([...this.media(), ...additions]);
  }

  protected setAlt(index: number, altText: string): void {
    this.media.update((current) =>
      current.map((item, at) => (at === index ? { ...item, altText: altText || null } : item)),
    );
  }

  protected move(index: number, direction: -1 | 1): void {
    const next = [...this.media()];
    const target = index + direction;
    if (target < 0 || target >= next.length) return;
    [next[index], next[target]] = [next[target], next[index]];
    this.media.set(renumber(next));
  }

  protected remove(index: number): void {
    this.media.set(renumber(this.media().filter((_item, at) => at !== index)));
  }
}

/** Positions are rewritten from the array, so the two can never describe different orders. */
function renumber(media: readonly MediaPayload[]): MediaPayload[] {
  return media.map((item, position) => ({ ...item, position }));
}
