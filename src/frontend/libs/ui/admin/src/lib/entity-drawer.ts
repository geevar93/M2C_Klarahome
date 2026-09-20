import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { Button, Drawer, Icon } from '@klarahome/ui-primitives';

import { ConfirmDialog } from './confirm-dialog';

/**
 * An entity edited beside the list it came from, rather than on a page of its own.
 *
 * The back office's most common shape is "forty rows, edit one, look at the next": a full route
 * change for each of those loses the list's scroll position, its filters and the user's place in
 * them, and makes the back gesture the only way home. A side panel keeps the list on screen and
 * the context with it.
 *
 * It is a thin frame over `kh-drawer` — which already owns focus containment, Escape, scroll
 * locking and not being in the DOM when closed — adding the three things every editing panel
 * needs and none of them should re-invent: a heading with a subtitle, a scrolling body, and a
 * pinned footer so Save is reachable without scrolling to the bottom of a long form.
 *
 * The drawer opens from the **end** edge, not the start: the start edge is the navigation's, and
 * a panel that flew in over the sidebar would read as a change of place rather than a detail of
 * what is on screen.
 *
 * **A dirty drawer does not close on a stray Escape.** The panel's whole reason to exist is a
 * form, and a form half-typed is exactly what a backdrop click or an Escape pressed for some other
 * reason would throw away. When `dirty` is true the close is challenged first, here rather than in
 * each of the fifteen screens that would otherwise each have to remember to.
 */
@Component({
  selector: 'kh-entity-drawer',
  imports: [Button, ConfirmDialog, Drawer, Icon],
  template: `
    <kh-drawer [open]="true" side="end" [label]="heading()" (closed)="requestClose()">
      <header>
        <div class="titles">
          <h2>{{ heading() }}</h2>
          @if (subtitle(); as text) {
            <p class="subtitle">{{ text }}</p>
          }
        </div>

        <button
          khButton
          type="button"
          variant="tertiary"
          size="sm"
          [iconOnly]="true"
          aria-label="Close"
          (click)="requestClose()"
        >
          <kh-icon name="close" size="sm" />
        </button>
      </header>

      <div class="body">
        <ng-content />
      </div>

      <footer>
        <ng-content select="[slot=footer]" />
      </footer>
    </kh-drawer>

    <kh-confirm-dialog
      [open]="confirmingDiscard()"
      heading="Discard unsaved changes?"
      message="What has been typed in this panel has not been saved and will be lost."
      confirmLabel="Discard"
      tone="warning"
      (confirmed)="discard()"
      (cancelled)="confirmingDiscard.set(false)"
    />
  `,
  styles: `
    header {
      display: flex;
      gap: var(--space-3);
      align-items: flex-start;
      justify-content: space-between;
      padding: var(--space-4);
      border-block-end: 1px solid var(--color-border);
    }

    .titles {
      min-width: 0;
    }

    h2 {
      margin: 0;
      font-size: var(--text-lg);
    }

    .subtitle {
      margin: var(--space-1) 0 0;
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .body {
      flex: 1;
      overflow-y: auto;
      padding: var(--space-4);
    }

    /* Pinned, so Save does not depend on how long the form is. */
    footer:not(:empty) {
      position: sticky;
      inset-block-end: 0;
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      justify-content: flex-end;
      padding: var(--space-4);
      border-block-start: 1px solid var(--color-border);
      background: var(--color-bg);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class EntityDrawer {
  readonly heading = input.required<string>();
  readonly subtitle = input<string | null>(null);
  /** Whether the panel holds unsaved work. True makes Escape, the backdrop and × ask first. */
  readonly dirty = input(false);
  readonly closed = output<void>();

  protected readonly confirmingDiscard = signal(false);

  protected requestClose(): void {
    if (this.dirty()) {
      this.confirmingDiscard.set(true);
      return;
    }
    this.closed.emit();
  }

  protected discard(): void {
    this.confirmingDiscard.set(false);
    this.closed.emit();
  }
}
