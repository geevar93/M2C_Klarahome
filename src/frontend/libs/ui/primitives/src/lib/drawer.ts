import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  PLATFORM_ID,
  effect,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';

/** Where the panel comes from. `bottom` is the mobile sheet; the sides are navigation and cart. */
export type DrawerSide = 'start' | 'end' | 'bottom';

/**
 * An off-canvas panel that behaves like a dialog, because it is one.
 *
 * Four things have to be true or a drawer is a trap rather than a panel, and all four are here
 * rather than in each of the six places a drawer is used:
 *
 *  - **Focus moves in and is kept in.** A modal surface with the page still tabbable behind it
 *    leaves a keyboard user typing into a form they cannot see.
 *  - **Escape closes it**, and focus returns to whatever opened it — otherwise the next Tab
 *    starts at the top of the document.
 *  - **The page behind does not scroll.** On iOS in particular, a scrolling background under an
 *    open sheet is the single most common "the app is broken" report.
 *  - **It is not in the DOM when closed**, so nothing inside it is focusable, readable by a
 *    screen reader, or rendered into the SSR HTML a crawler indexes.
 */
@Component({
  selector: 'kh-drawer',
  template: `
    @if (open()) {
      <!-- Presentational: the dialog below is the thing, and a second announced element would
           just be an unlabelled region between the user and it. -->
      <div class="backdrop" (click)="requestClose()"></div>
      <div
        #panel
        class="panel"
        role="dialog"
        aria-modal="true"
        [attr.aria-label]="label()"
        tabindex="-1"
        (keydown)="onKeydown($event)"
      >
        <ng-content />
      </div>
    }
  `,
  styles: `
    .backdrop {
      position: fixed;
      inset: 0;
      z-index: var(--z-drawer);
      /* Derived from a token rather than written as a colour: no component may hold a hex value,
         and a scrim that ignored the theme would stay dark-on-dark after Step 30. */
      background: color-mix(in srgb, var(--color-text) 55%, transparent);
    }

    .panel {
      position: fixed;
      z-index: var(--z-drawer);
      display: flex;
      flex-direction: column;
      overflow-y: auto;
      overscroll-behavior: contain;
      background: var(--color-bg);
      box-shadow: var(--shadow-lg);
      animation: kh-drawer-in var(--duration-base) var(--ease-standard);
    }

    :host([data-side='start']) .panel,
    :host([data-side='end']) .panel {
      inset-block: 0;
      width: min(20rem, 85vw);
    }

    :host([data-side='start']) .panel {
      inset-inline-start: 0;
    }

    :host([data-side='end']) .panel {
      inset-inline-end: 0;
    }

    :host([data-side='bottom']) .panel {
      inset-inline: 0;
      inset-block-end: 0;
      max-height: 85vh;
      border-radius: var(--radius-lg) var(--radius-lg) 0 0;
    }

    @keyframes kh-drawer-in {
      from {
        opacity: 0.6;
        transform: translateY(var(--space-4));
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .panel {
        animation: none;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-side]': 'side()' },
})
export class Drawer {
  private readonly document = inject(DOCUMENT);
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));

  readonly open = input(false);
  readonly side = input<DrawerSide>('start');
  /** The accessible name. A dialog without one is announced as "dialog" and nothing else. */
  readonly label = input.required<string>();
  /**
   * Asked to close — by Escape, by the backdrop, or by a control inside. The parent owns the
   * `open` state, so a drawer never closes itself behind the back of whatever opened it.
   */
  readonly closed = output<void>();

  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');
  private restoreFocusTo: HTMLElement | null = null;

  constructor() {
    effect(() => {
      const isOpen = this.open();
      const panel = this.panel();
      if (!this.isBrowser) return;

      if (isOpen && panel) {
        this.restoreFocusTo = this.document.activeElement as HTMLElement | null;
        this.document.body.style.overflow = 'hidden';
        // The panel itself, not its first control: a screen reader then reads the dialog's name
        // and role before its contents, which is the announcement the user needs.
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
    this.closed.emit();
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
      // Nothing to move to; keeping focus on the panel is better than releasing it to the page.
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
