import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Button, Icon } from '@klarahome/ui-primitives';

import { EntityOption } from './entity-picker';

/** One chosen reference, as the tag shows it. */
export interface ReferenceTag extends EntityOption {
  /** True while the id has not been resolved to a name yet. */
  readonly pending?: boolean;
  /** True when the id no longer names anything: deleted, or never existed. */
  readonly missing?: boolean;
}

/**
 * The chosen references of one field, as removable tags — with no text box to type an id into.
 *
 * The page composer's reference fields were a textarea of UUIDs, one per line. The ids are still
 * what is stored, but nobody should have to read or type one: each is shown by its name and
 * picture, removed with its cross, and added through the browse dialog the button opens. A tag
 * whose id resolves to nothing is kept and marked, so a deleted product is visible and removable
 * rather than silently rendering nothing on the storefront.
 */
@Component({
  selector: 'kh-reference-tags',
  imports: [Button, Icon],
  template: `
    <div class="field" role="group" [attr.aria-labelledby]="controlId() + '-label'">
      <div class="head">
        <span class="label" [id]="controlId() + '-label'">
          {{ label() }}
          @if (!required()) {
            <span class="optional">(optional)</span>
          }
        </span>
        @if (hint()) {
          <span class="hint">{{ hint() }}</span>
        }
      </div>

      @if (tags().length > 0) {
        <ul class="tags" [attr.aria-label]="label()">
          @for (tag of tags(); track tag.id) {
            <li class="tag" [class.missing]="tag.missing" [attr.title]="tag.hint ?? tag.id">
              @if (tag.imageUrl) {
                <img [src]="tag.imageUrl" alt="" loading="lazy" width="28" height="28" />
              } @else {
                <span class="thumb" aria-hidden="true"></span>
              }
              <span class="text">{{ tag.pending ? 'Loading…' : tag.label }}</span>
              <button
                type="button"
                class="remove"
                [attr.aria-label]="'Remove ' + (tag.pending ? tag.id : tag.label)"
                (click)="removed.emit(tag.id)"
              >
                <kh-icon name="close" size="sm" />
              </button>
            </li>
          }
        </ul>
      } @else {
        <p class="empty">Nothing chosen yet.</p>
      }

      <div>
        <button khButton type="button" size="sm" [disabled]="full()" (click)="browse.emit()">
          <kh-icon name="search" size="sm" />
          {{ browseLabel() }}
        </button>
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
      margin-block-end: var(--space-4);
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
    }

    .head {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
    }

    .label {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .optional,
    .hint,
    .empty {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
      font-weight: var(--weight-regular);
    }

    .empty {
      margin: 0;
    }

    .tags {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .tag {
      display: inline-flex;
      align-items: center;
      gap: var(--space-2);
      max-inline-size: 100%;
      padding: var(--space-1);
      padding-inline-end: 0;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-muted);
      font-size: var(--text-sm);
    }

    .tag.missing {
      border-color: var(--color-danger-text);
      background: var(--color-danger-subtle);
      color: var(--color-danger-text);
    }

    img,
    .thumb {
      flex-shrink: 0;
      inline-size: 1.75rem;
      block-size: 1.75rem;
      border-radius: var(--radius-sm);
      object-fit: cover;
      background: var(--color-surface);
    }

    .text {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .remove {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
      min-inline-size: var(--touch-target-min);
      min-block-size: 1.75rem;
      padding: 0;
      border: none;
      background: none;
      color: inherit;
      cursor: pointer;
    }

    .remove:hover,
    .remove:focus-visible {
      color: var(--color-danger-text);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReferenceTags {
  readonly label = input.required<string>();
  /** A unique id, so the group's label can name it. */
  readonly controlId = input.required<string>();
  readonly hint = input('');
  readonly required = input(false);
  readonly tags = input<readonly ReferenceTag[]>([]);
  readonly browseLabel = input('Browse');
  /** Disables the browse button, for a field already holding as many as it may. */
  readonly full = input(false);

  readonly removed = output<string>();
  readonly browse = output<void>();
}
