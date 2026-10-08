import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';

/** What a click or key press on the mirrored page resolved to, for the canvas to translate. */
export interface MirrorActivity {
  /** The block frame the event landed on, if any. */
  readonly blockId?: string;
  /** The gap between blocks the event landed on, if any: the index a new block would take. */
  readonly insertAt?: number;
  /** Set for a key press on a focused frame. A click has none. */
  readonly key?: string;
}

/** The document the frame starts with; everything else is mirrored in. */
// `data-kh-mirror` marks the document this component wrote, because an iframe fires `load` for the
// `about:blank` document it starts with as well, and wiring that one up left the styles in a
// document that was thrown away the moment `srcdoc` was set - an unstyled preview that also threw
// "Cannot set properties of null" while the real document was still parsing.
const BLANK_DOCUMENT =
  '<!doctype html><html><head><meta charset="utf-8"></head><body data-kh-mirror></body></html>';

/**
 * A page drawn at its own width, inside an `<iframe>`, from content the admin rendered.
 *
 * **Why an iframe.** The storefront's components size themselves with viewport media queries
 * (`@media (min-width: 1024px)`), not container queries. Squeezing them into a 390px column inside
 * the admin would therefore show the *desktop* arrangement, narrow - the header without its drawer
 * button, a hero with its wide padding - which is not what a phone shows. An iframe has a viewport
 * of its own, so the same stylesheets answer for that width and the render is honest.
 *
 * **How.** The projected content is rendered by Angular as usual, in a hidden element (so it is the
 * real shared components, bound to real data, with nothing re-implemented). This component copies
 * the document's stylesheets into the frame and mirrors that element's markup into the frame body
 * whenever it changes. The copy is inert markup: it has no Angular behind it, which is why the
 * canvas marks what can be clicked with `data-` attributes and this component reports clicks and
 * keys on them as {@link MirrorActivity}.
 *
 * The frame is laid out at `width()` and scaled down to fit the space it is given, so a 1280px page
 * can sit in a narrow column. Focus is put back on the same frame after each mirror, because the
 * body is replaced and a keyboard user would otherwise be thrown back to the start of the page.
 */
@Component({
  selector: 'kh-frame-mirror',
  template: `
    <div class="slot" [style.width.px]="slotWidth()" [style.height.px]="slotHeight()">
      <iframe
        #frame
        class="frame"
        [attr.title]="title()"
        [attr.tabindex]="interactive() ? null : -1"
        [attr.inert]="interactive() ? null : ''"
        [style.width.px]="width()"
        [style.height.px]="frameHeight()"
        [style.transform]="'scale(' + scale() + ')'"
        (load)="start()"
      ></iframe>
    </div>
    <div hidden #source><ng-content /></div>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }

    .slot {
      position: relative;
      max-width: 100%;
      margin-inline: auto;
      overflow: hidden;
    }

    .frame {
      display: block;
      border: 0;
      background: transparent;
      transform-origin: 0 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FrameMirror {
  /** The page's own width, in CSS pixels: 390 for a phone, 1280 for a desktop. */
  readonly width = input(1280);
  /** The tallest the visible frame may be (after scaling); the page scrolls inside it beyond that. */
  readonly maxHeight = input<number | null>(null);
  /** Whether it reacts to the pointer and the keyboard. A thumbnail does not. */
  readonly interactive = input(true);
  readonly title = input('Storefront preview');

  readonly activity = output<MirrorActivity>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly destroyRef = inject(DestroyRef);
  private readonly frame = viewChild.required<ElementRef<HTMLIFrameElement>>('frame');
  private readonly source = viewChild.required<ElementRef<HTMLElement>>('source');

  private readonly available = signal(0);
  private readonly contentHeight = signal(320);
  private started = false;
  private pending = 0;
  private focusKey: string | null = null;

  /** How far the page is shrunk to fit; never enlarged. */
  protected readonly scale = computed(() => {
    const room = this.available();
    return room > 0 ? Math.min(1, room / this.width()) : 1;
  });
  protected readonly slotWidth = computed(() => this.width() * this.scale());
  private readonly visibleHeight = computed(() => {
    const cap = this.maxHeight();
    const natural = this.contentHeight() * this.scale();
    return cap === null ? natural : Math.min(natural, cap);
  });
  protected readonly slotHeight = computed(() => this.visibleHeight());
  /** The frame's own height, before scaling: what the page needs, so that it scrolls inside when capped. */
  protected readonly frameHeight = computed(() => this.visibleHeight() / this.scale());

  constructor() {
    afterNextRender(() => {
      if (typeof ResizeObserver !== 'undefined') {
        const resize = new ResizeObserver((entries) => {
          const box = entries[0]?.contentRect.width ?? 0;
          if (box > 0) this.available.set(box);
        });
        resize.observe(this.host.nativeElement);
        this.destroyRef.onDestroy(() => resize.disconnect());
      }

      // Set here rather than bound: Angular treats `srcdoc` as an HTML sink and would sanitise a
      // document it is only being asked to hold. `load` fires once, and `start` takes it from there.
      this.frame().nativeElement.srcdoc = BLANK_DOCUMENT;
    });
  }

  /** Wires the frame once its document exists: styles, observers, event delegation, first mirror. */
  protected start(): void {
    const doc = this.frame().nativeElement.contentDocument;
    // Only the srcdoc document (see BLANK_DOCUMENT): `load` also fires for the initial about:blank.
    if (this.started || !doc?.body?.hasAttribute('data-kh-mirror')) return;
    this.started = true;

    const base = doc.createElement('base');
    base.href = document.baseURI;
    doc.head.appendChild(base);
    doc.documentElement.style.cssText = 'margin:0;background:transparent';
    doc.body.style.cssText = 'margin:0;background:transparent';

    this.copyStyles(doc);
    this.mirror(doc);

    const observer = new MutationObserver(() => this.schedule());
    observer.observe(this.source().nativeElement, {
      childList: true,
      subtree: true,
      attributes: true,
      characterData: true,
    });
    // Component styles are added to <head> the first time a component is created.
    const headObserver = new MutationObserver(() => {
      this.copyStyles(doc);
      this.schedule();
    });
    headObserver.observe(document.head, { childList: true });
    this.destroyRef.onDestroy(() => {
      observer.disconnect();
      headObserver.disconnect();
      cancelAnimationFrame(this.pending);
    });

    doc.addEventListener('focusin', (event) => {
      this.focusKey = focusKeyOf(event.target);
    });
    doc.addEventListener('click', (event) => {
      if (!this.interactive()) return;
      const found = resolve(event.target);
      if (found) {
        event.preventDefault();
        this.activity.emit(found);
      }
    });
    doc.addEventListener('keydown', (event) => {
      if (!this.interactive()) return;
      const target = event.target as HTMLElement | null;
      // Only a frame itself: a key typed into something inside it is that thing's.
      if (!target?.hasAttribute?.('data-block-id') && !target?.hasAttribute?.('data-insert-at')) return;
      if (!['ArrowUp', 'ArrowDown', 'Delete', 'Backspace', 'Enter', ' '].includes(event.key)) return;
      const found = resolve(target);
      if (!found) return;
      event.preventDefault();
      this.activity.emit({ ...found, key: event.key === ' ' ? 'Enter' : event.key });
    });
  }

  private schedule(): void {
    cancelAnimationFrame(this.pending);
    this.pending = requestAnimationFrame(() => {
      const doc = this.frame().nativeElement.contentDocument;
      if (doc?.body?.hasAttribute('data-kh-mirror')) this.mirror(doc);
    });
  }

  /** Replaces the frame body with the source's markup, then measures it and restores focus. */
  private mirror(doc: Document): void {
    // Between `srcdoc` being set and its `load`, the frame's document has no body yet.
    if (!doc.body) return;
    const focused = doc.activeElement && doc.activeElement !== doc.body ? focusKeyOf(doc.activeElement) : null;

    doc.body.innerHTML = this.source().nativeElement.innerHTML;
    this.measure(doc);

    if (focused) {
      (doc.querySelector(`[data-focus-key="${focused.replace(/"/g, '\\"')}"]`) as HTMLElement | null)?.focus({
        preventScroll: true,
      });
    }
    // Images and fonts arriving later change the height; measure once more when they have.
    requestAnimationFrame(() => this.measure(doc));
  }

  private measure(doc: Document): void {
    this.contentHeight.set(Math.max(doc.documentElement.scrollHeight, doc.body.scrollHeight, 40));
  }

  /** Mirrors the admin's stylesheets into the frame, replacing any copied earlier. */
  private copyStyles(doc: Document): void {
    doc.head.querySelectorAll('[data-mirrored]').forEach((node) => node.remove());
    document.head.querySelectorAll('style, link[rel="stylesheet"]').forEach((node) => {
      const copy = node.cloneNode(true) as HTMLElement;
      copy.setAttribute('data-mirrored', '');
      doc.head.appendChild(copy);
    });
  }
}

function focusKeyOf(target: EventTarget | null): string | null {
  return (target as HTMLElement | null)?.closest?.('[data-focus-key]')?.getAttribute('data-focus-key') ?? null;
}

function resolve(target: EventTarget | null): MirrorActivity | null {
  const element = target as HTMLElement | null;
  const gap = element?.closest?.('[data-insert-at]');
  if (gap) return { insertAt: Number(gap.getAttribute('data-insert-at')) };
  const block = element?.closest?.('[data-block-id]');
  return block ? { blockId: block.getAttribute('data-block-id') ?? undefined } : null;
}
