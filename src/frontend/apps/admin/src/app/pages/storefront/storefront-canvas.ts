import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import {
  CmsBlockRenderer,
  CmsBlockView,
  FooterStyle,
  HeaderStyle,
  NavItem,
  SiteFooter,
  SiteHeader,
} from '@klarahome/ui-patterns';

import { FrameMirror, MirrorActivity } from './frame-mirror';
import { canvasTokens } from './storefront-theme';

export type CanvasViewport = 'desktop' | 'phone';

/** The widths the two viewports are drawn at, in CSS pixels. */
export const CANVAS_WIDTHS: Readonly<Record<CanvasViewport, number>> = { desktop: 1280, phone: 390 };

/** One block on the canvas: what to draw, and what to call it in the editor's frame. */
export interface CanvasFrame {
  readonly id: string;
  readonly label: string;
  /** Null while the block has no render yet (just added, or hidden); the frame says so. */
  readonly view: CmsBlockView | null;
  /** Why there is no render, when there is not. */
  readonly note?: string;
}

/** A frame key press the canvas passes up. `Enter` selects, the arrows move, `Delete` removes. */
export interface CanvasKey {
  readonly id: string;
  readonly key: string;
}

/** A menu to draw in the header and footer, so the treatments have something to lay out. */
const SAMPLE_MENU: readonly NavItem[] = [
  { label: 'New in', href: '/c/new' },
  { label: 'Living', href: '/c/living' },
  { label: 'Bedroom', href: '/c/bedroom' },
  { label: 'Kitchen', href: '/c/kitchen' },
  { label: 'Sale', href: '/c/sale', badge: 'Sale' },
];

/**
 * The tokens for the editing chrome (frame outlines, labels, the + between blocks).
 *
 * The admin's own primary, read from the page so dark mode follows; not the storefront palette,
 * which would make the selection ring invisible on a theme whose primary matches the block behind
 * it. Falls back to the admin's light navy where there is no stylesheet to read (a unit test).
 */
function chromeTokens(): Record<string, string> {
  let ring = '#2d4a8a';
  let text = '#ffffff';
  if (typeof document !== 'undefined' && typeof getComputedStyle === 'function') {
    const root = getComputedStyle(document.documentElement);
    ring = root.getPropertyValue('--color-primary').trim() || ring;
    text = root.getPropertyValue('--color-on-primary').trim() || text;
  }
  return { '--designer-ring': ring, '--designer-ring-text': text };
}

/**
 * The storefront, drawn inside the admin: header, blocks and footer in the chosen palette.
 *
 * **Scoped.** Everything the preset decides is set as inline custom properties on the `.stage`
 * element only (`canvasTokens`), and the stage is rendered inside {@link FrameMirror}'s own
 * document, so neither the preset nor the storefront's stylesheets can reach the back office
 * around it. The admin's skin is never read or written.
 *
 * The header and footer are the real `kh-site-header` / `kh-site-footer`, in the template's styles;
 * the blocks are the shared `CmsBlockRenderer`. With `editable` each block sits in a selectable
 * frame with its label, and a `+` sits in every gap between them. Those are markup for
 * {@link FrameMirror} to report clicks on, so they carry `data-` attributes rather than handlers.
 */
@Component({
  selector: 'kh-storefront-canvas',
  imports: [CmsBlockRenderer, FrameMirror, SiteFooter, SiteHeader],
  template: `
    <kh-frame-mirror
      [width]="width()"
      [maxHeight]="maxHeight()"
      [interactive]="interactive()"
      [title]="title()"
      (activity)="onActivity($event)"
    >
      <div class="stage" data-storefront-canvas [style]="tokens()" [attr.data-theme]="preset() ?? 'sand'">
        <div class="chrome" inert>
          <kh-site-header [style]="headerStyle()" [storeName]="storeName()" [menu]="menu()" />
        </div>

        <main>
          @if (editable()) {
            @for (item of items(); track item.id; let index = $index) {
              @if (index === 0) {
                <div
                  class="gap"
                  role="button"
                  tabindex="0"
                  data-insert-at="0"
                  data-focus-key="i:0"
                  aria-label="Add a block at the top"
                >
                  <span class="plus" aria-hidden="true">+</span>
                </div>
              }
              <div
                class="frame"
                role="button"
                tabindex="0"
                [class.selected]="item.id === selectedId()"
                [attr.aria-pressed]="item.id === selectedId()"
                [attr.aria-label]="'Edit ' + item.label"
                [attr.data-block-id]="item.id"
                [attr.data-focus-key]="'b:' + item.id"
              >
                <span class="tag">{{ item.label }}</span>
                <div class="body" inert>
                  @if (item.blocks.length > 0) {
                    <kh-cms-block-renderer [blocks]="item.blocks" [prioritiseFirst]="false" [fullBleed]="false" />
                  } @else {
                    <p class="empty">{{ item.note ?? 'Nothing to show for this block yet.' }}</p>
                  }
                </div>
              </div>
              <div
                class="gap"
                role="button"
                tabindex="0"
                [attr.data-insert-at]="index + 1"
                [attr.data-focus-key]="'i:' + (index + 1)"
                [attr.aria-label]="'Add a block after ' + item.label"
              >
                <span class="plus" aria-hidden="true">+</span>
              </div>
            } @empty {
              <div class="gap lone" role="button" tabindex="0" data-insert-at="0" data-focus-key="i:0" aria-label="Add the first block">
                <span class="plus" aria-hidden="true">+</span>
                <span class="lone-text">Add the first block</span>
              </div>
            }
          } @else {
            <kh-cms-block-renderer [blocks]="plainViews()" [prioritiseFirst]="false" [fullBleed]="false" />
          }
        </main>

        <div class="chrome" inert>
          <kh-site-footer [style]="footerStyle()" [storeName]="storeName()" [menu]="menu()" />
        </div>
      </div>
    </kh-frame-mirror>
  `,
  styles: `
    :host {
      display: block;
      min-width: 0;
    }

    .stage {
      min-height: 12rem;
      background: var(--color-bg);
      color: var(--color-text);
      font-family: var(--font-sans);
      font-size: var(--text-base);
      line-height: var(--leading-normal);
    }

    .chrome {
      display: block;
    }

    main {
      display: block;
    }

    .frame {
      position: relative;
      outline: 1px dashed transparent;
      outline-offset: -1px;
      cursor: pointer;
    }

    .frame:hover {
      outline-color: var(--designer-ring);
    }

    .frame:focus-visible,
    .frame.selected {
      outline: 3px solid var(--designer-ring);
      outline-offset: -3px;
    }

    .tag {
      position: absolute;
      inset-block-start: var(--space-2);
      inset-inline-start: var(--space-2);
      z-index: 2;
      padding: var(--space-1) var(--space-2);
      border-radius: var(--radius-sm);
      background: var(--designer-ring);
      color: var(--designer-ring-text);
      font-family: var(--font-sans);
      font-size: var(--text-xs);
      font-weight: var(--weight-medium);
      line-height: var(--leading-tight);
      opacity: 0;
      pointer-events: none;
    }

    .frame:hover .tag,
    .frame:focus-visible .tag,
    .frame.selected .tag {
      opacity: 1;
    }

    .body {
      display: block;
      min-height: 4rem;
    }

    .empty {
      margin: 0;
      padding: var(--space-8) var(--space-4);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
      text-align: center;
    }

    .gap {
      position: relative;
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--space-2);
      min-height: 1.75rem;
      cursor: pointer;
      color: var(--designer-ring);
    }

    .gap::before {
      content: '';
      position: absolute;
      inset-inline: var(--space-4);
      inset-block-start: 50%;
      border-block-start: 1px dashed var(--designer-ring);
      opacity: 0.35;
    }

    .plus {
      position: relative;
      display: grid;
      place-items: center;
      inline-size: 1.5rem;
      block-size: 1.5rem;
      border-radius: var(--radius-full);
      background: var(--designer-ring);
      color: var(--designer-ring-text);
      font-family: var(--font-sans);
      font-size: var(--text-base);
      line-height: 1;
      opacity: 0.55;
    }

    .gap:hover::before,
    .gap:focus-visible::before,
    .gap:hover .plus,
    .gap:focus-visible .plus {
      opacity: 1;
    }

    .gap:focus-visible {
      outline: 3px solid var(--designer-ring);
      outline-offset: -3px;
    }

    .lone {
      min-height: 8rem;
    }

    .lone-text {
      position: relative;
      font-family: var(--font-sans);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StorefrontCanvas {
  readonly frames = input<readonly CanvasFrame[]>([]);
  /** The palette preset id; absent or unknown is the built-in sand. */
  readonly preset = input<string | null>(null);
  readonly headerStyle = input<HeaderStyle>('inline');
  readonly footerStyle = input<FooterStyle>('columns');
  readonly storeName = input('Klara Home');
  readonly menu = input<readonly NavItem[]>(SAMPLE_MENU);
  readonly viewport = input<CanvasViewport>('desktop');
  /** Draws a selectable frame, a label and a `+` for every block. Off, the page is just the page. */
  readonly editable = input(false);
  readonly selectedId = input<string | null>(null);
  /** Off for a thumbnail: it ignores the pointer and takes no focus. */
  readonly interactive = input(true);
  /** The tallest the canvas may be before the page scrolls inside it. */
  readonly maxHeight = input<number | null>(null);
  readonly title = input('Storefront preview');

  readonly blockSelected = output<string>();
  readonly insertRequested = output<number>();
  readonly blockKey = output<CanvasKey>();

  protected readonly width = computed(() => CANVAS_WIDTHS[this.viewport()]);

  /** The palette plus the editing chrome, as one map for the stage's inline style. */
  protected readonly tokens = computed(() => ({ ...canvasTokens(this.preset()), ...chromeTokens() }));

  /** Each frame with its single-block list, built once so the renderer's input is stable. */
  protected readonly items = computed(() =>
    this.frames().map((frame) => ({ ...frame, blocks: frame.view ? [frame.view] : [] })),
  );

  protected readonly plainViews = computed(() =>
    this.frames().flatMap((frame) => (frame.view ? [frame.view] : [])),
  );

  protected onActivity(activity: MirrorActivity): void {
    if (activity.insertAt !== undefined) {
      if (activity.key === undefined || activity.key === 'Enter') this.insertRequested.emit(activity.insertAt);
      return;
    }
    if (activity.blockId === undefined) return;
    if (activity.key === undefined || activity.key === 'Enter') {
      this.blockSelected.emit(activity.blockId);
      return;
    }
    this.blockKey.emit({ id: activity.blockId, key: activity.key });
  }
}
