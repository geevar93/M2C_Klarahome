import {
  ChangeDetectionStrategy,
  Component,
  Directive,
  TemplateRef,
  contentChild,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { Button, Icon } from '@klarahome/ui-primitives';

/** One row in an ordered list. Everything else about it is the caller's template. */
export interface ReorderItem {
  readonly id: string;
  readonly label: string;
  readonly sublabel?: string | null;
  /** Drawn muted at the end of the row — a block's type, a menu item's target. */
  readonly meta?: string | null;
}

/**
 * The caller's body for one row.
 *
 * Context is `$implicit` (the item) and `index`, the same shape as the data table's cell
 * templates, so the two read alike where a screen uses both.
 */
@Directive({ selector: '[khReorderItem]' })
export class ReorderItemTemplate {
  readonly template = inject(TemplateRef<unknown>);
}

/**
 * A list whose order is the data.
 *
 * Two places in the back office hold an ordered list that a person arranges by hand — the blocks
 * on a CMS page and the items in a menu — and in both the position is a stored field rather than a
 * view preference, so the same interaction has to produce the same kind of write. Hence one
 * component: it emits the new order of ids and the caller sends the whole list back, which is what
 * both endpoints accept and the only thing that keeps positions consistent while somebody drags.
 *
 * **Dragging is the second way to do it, never the only one.** The move buttons come first in the
 * DOM and do the whole job on their own, because a pointer-only reorder excludes keyboard and
 * assistive-technology users from a control that decides what a shopper sees. The buttons are also
 * what makes a precise move easy — dragging a block past nine others is fiddly on a laptop
 * trackpad and exact with two keystrokes.
 *
 * The component holds no order of its own. `items` is the order; a drop emits and the caller
 * re-renders. That is deliberate: a list that reordered itself optimistically would disagree with
 * the server the moment a save failed, and the disagreement would be invisible.
 */
@Component({
  selector: 'kh-reorder-list',
  imports: [Button, Icon, NgTemplateOutlet],
  template: `
    @if (items().length === 0) {
      <p class="empty">{{ emptyMessage() }}</p>
    } @else {
      <ul [attr.aria-label]="label()">
        @for (item of items(); track item.id; let index = $index; let count = $count) {
          <li
            [class.dragging]="draggingId() === item.id"
            [class.over]="overId() === item.id && draggingId() !== item.id"
            draggable="true"
            (dragstart)="startDrag(item.id)"
            (dragover)="dragOver($event, item.id)"
            (dragleave)="overId.set(null)"
            (drop)="drop($event, index)"
            (dragend)="endDrag()"
          >
            <span class="handle" aria-hidden="true"><kh-icon name="menu" size="sm" /></span>

            <div class="body">
              @if (body()) {
                <ng-container
                  *ngTemplateOutlet="body()!.template; context: { $implicit: item, index: index }"
                />
              } @else {
                <span class="label">{{ item.label }}</span>
                @if (item.sublabel) {
                  <span class="sublabel">{{ item.sublabel }}</span>
                }
              }
            </div>

            @if (item.meta) {
              <span class="meta">{{ item.meta }}</span>
            }

            <div class="controls">
              <button
                khButton
                type="button"
                size="sm"
                variant="tertiary"
                [disabled]="index === 0"
                [attr.aria-label]="'Move ' + item.label + ' up'"
                (click)="move(index, index - 1)"
              >
                <kh-icon name="chevron-up" size="sm" />
              </button>
              <button
                khButton
                type="button"
                size="sm"
                variant="tertiary"
                [disabled]="index === count - 1"
                [attr.aria-label]="'Move ' + item.label + ' down'"
                (click)="move(index, index + 1)"
              >
                <kh-icon name="chevron-down" size="sm" />
              </button>
              @if (removable()) {
                <button
                  khButton
                  type="button"
                  size="sm"
                  variant="tertiary"
                  [attr.aria-label]="'Remove ' + item.label"
                  (click)="removed.emit(item.id)"
                >
                  <kh-icon name="close" size="sm" />
                </button>
              }
            </div>
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    ul {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    li {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      padding: var(--space-2) var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    li.dragging {
      opacity: 0.5;
    }

    li.over {
      border-color: var(--color-primary);
    }

    .handle {
      color: var(--color-text-muted);
      cursor: grab;
    }

    .body {
      flex: 1;
      min-inline-size: 0;
    }

    .label {
      display: block;
      overflow-wrap: anywhere;
      font-weight: var(--weight-medium);
    }

    .sublabel,
    .meta {
      overflow-wrap: anywhere;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .sublabel {
      display: block;
    }

    .controls {
      display: flex;
      /* Never the thing that gives: three touch targets, not text, so they hold their size while
         \`.body\` (which has \`min-inline-size: 0\`) is what shrinks and wraps a long label instead. */
      flex-shrink: 0;
      gap: var(--space-1);
    }

    .empty {
      margin: 0;
      padding: var(--space-6);
      border: 1px dashed var(--color-border);
      border-radius: var(--radius-md);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
      text-align: center;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReorderList {
  readonly items = input<readonly ReorderItem[]>([]);
  /** The list's accessible name — "Blocks on this page", "Menu items". */
  readonly label = input.required<string>();
  readonly removable = input(false);
  readonly emptyMessage = input('Nothing here yet.');

  /** The ids in their new order. The caller saves them; this component keeps no order. */
  readonly reordered = output<readonly string[]>();
  readonly removed = output<string>();

  protected readonly body = contentChild(ReorderItemTemplate);

  protected readonly draggingId = signal<string | null>(null);
  protected readonly overId = signal<string | null>(null);

  protected startDrag(id: string): void {
    this.draggingId.set(id);
  }

  protected dragOver(event: DragEvent, id: string): void {
    // Without this the browser refuses the drop, and the row silently springs back.
    event.preventDefault();
    this.overId.set(id);
  }

  protected drop(event: DragEvent, index: number): void {
    event.preventDefault();
    const dragging = this.draggingId();
    this.overId.set(null);
    if (!dragging) return;

    const from = this.items().findIndex((item) => item.id === dragging);
    this.draggingId.set(null);
    if (from >= 0 && from !== index) this.move(from, index);
  }

  protected endDrag(): void {
    this.draggingId.set(null);
    this.overId.set(null);
  }

  protected move(from: number, to: number): void {
    const ids = this.items().map((item) => item.id);
    if (to < 0 || to >= ids.length) return;

    const [moved] = ids.splice(from, 1);
    ids.splice(to, 0, moved);
    this.reordered.emit(ids);
  }
}
