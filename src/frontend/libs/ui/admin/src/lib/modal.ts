import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { Button, Icon } from '@klarahome/ui-primitives';

/**
 * A centred modal dialog.
 *
 * The storefront has `kh-drawer`, which is the right shape for a phone; the back office needs the
 * other one — a short, centred surface for a confirmation, a small form or a preview, over a page
 * whose context the user must keep. Rather than widen the drawer with a fourth `side`, this is a
 * separate component, because the two differ in every dimension that matters: where they come
 * from, how wide they get, and whether the page behind them is still the subject.
 *
 * What it shares with the drawer is the part that is not decoration, and the reason neither is
 * hand-written per screen:
 *
 *  - focus moves in on open and is **kept in** while it is up;
 *  - Escape closes it, and focus returns to whatever opened it;
 *  - the page behind does not scroll;
 *  - it is **not in the DOM when closed**, so nothing inside it is focusable or readable.
 *
 * The header, body and footer are three content slots rather than three inputs, because a footer
 * is a row of buttons whose wiring belongs to the screen that owns them.
 */
@Component({
  selector: 'kh-modal',
  imports: [Button, Icon],
  template: `
    @if (open()) {
      <div class="backdrop" (click)="requestClose()"></div>
      <div class="wrap">
        <div
          #panel
          class="panel"
          role="dialog"
          aria-modal="true"
          [attr.aria-label]="heading()"
          [style.max-width]="width()"
          tabindex="-1"
          (keydown)="onKeydown($event)"
        >
          <header>
            <h2>{{ heading() }}</h2>
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
        </div>
      </div>
    }
  `,
  styles: `
    .backdrop {
      position: fixed;
      inset: 0;
      z-index: var(--z-modal);
      background: color-mix(in srgb, var(--color-text) 55%, transparent);
    }

    .wrap {
      position: fixed;
      inset: 0;
      z-index: var(--z-modal);
      display: flex;
      align-items: center;
      justify-content: center;
      padding: var(--space-4);
      pointer-events: none;
    }

    .panel {
      display: flex;
      flex-direction: column;
      width: 100%;
      max-height: calc(100vh - var(--space-8));
      pointer-events: auto;
      border-radius: var(--radius-lg);
      background: var(--color-bg);
      box-shadow: var(--shadow-lg);
    }

    header {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      justify-content: space-between;
      padding: var(--space-4);
      border-block-end: 1px solid var(--color-border);
    }

    h2 {
      margin: 0;
      font-size: var(--text-lg);
    }

    .body {
      flex: 1;
      overflow-y: auto;
      padding: var(--space-4);
    }

    footer:not(:empty) {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      justify-content: flex-end;
      padding: var(--space-4);
      border-block-start: 1px solid var(--color-border);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Modal {
  private readonly document = inject(DOCUMENT);

  readonly open = input(false);
  /** The dialog's accessible name, and its visible heading. The two are deliberately one string. */
  readonly heading = input.required<string>();
  readonly width = input('32rem');
  /**
   * Whether Escape and the backdrop may close it.
   *
   * Set false for a dialog in the middle of something that cannot be half-done — an upload in
   * flight, a payout being confirmed — so a stray Escape does not abandon it.
   */
  readonly dismissible = input(true);
  readonly closed = output<void>();

  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  private restoreFocusTo: HTMLElement | null = null;

  constructor() {
    effect(() => {
      const isOpen = this.open();
      const panel = this.panel();

      if (isOpen && panel) {
        this.restoreFocusTo = this.document.activeElement as HTMLElement | null;
        this.document.body.style.overflow = 'hidden';
        // The panel, not its first control: a screen reader then reads the dialog's name and role
        // before its contents, which is the announcement the user needs.
        panel.nativeElement.focus({ preventScroll: true });
        return;
      }

      if (!isOpen) {
        this.document.body.style.overflow = '';
        this.restoreFocusTo?.focus({ preventScroll: true });
        this.restoreFocusTo = null;
      }
    });
  }

  protected requestClose(): void {
    if (this.dismissible()) this.closed.emit();
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.requestClose();
      return;
    }

    if (event.key !== 'Tab') return;

    const focusable = this.focusableElements();
    if (focusable.length === 0) {
      event.preventDefault();
      return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = this.document.activeElement;

    if (event.shiftKey && (active === first || active === this.panel()?.nativeElement)) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  private focusableElements(): HTMLElement[] {
    const root = this.panel()?.nativeElement;
    if (!root) return [];
    const selector =
      'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';
    return Array.from(root.querySelectorAll<HTMLElement>(selector)).filter(
      (element) => element.offsetParent !== null || element === this.document.activeElement,
    );
  }
}
