import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Directive,
  Injectable,
  OnInit,
  Signal,
  TemplateRef,
  afterRenderEffect,
  booleanAttribute,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';

/**
 * The one sticky bar at the bottom of the screen, and the page that fills it.
 *
 * The primary action on a phone belongs in the thumb zone and has to stay there while the page
 * scrolls: "Add to cart" on a product, "Checkout" on the cart, "Pay" on the payment step
 * (docs/05-frontend-architecture.md §3.3). That means the bar lives in the shell — a bar rendered
 * inside a routed page would scroll away with it — while its content belongs to the page.
 *
 * A template registered with a service is how those two facts are reconciled. There is exactly
 * one bar, it is always the last thing in the tab order, and a page that declares no action gets
 * no bar and no reserved space.
 */
@Injectable({ providedIn: 'root' })
export class StickyActionBarService {
  private readonly current = signal<TemplateRef<unknown> | null>(null);
  private readonly phoneOnly = signal(false);
  private readonly anchor = signal<string | null>(null);
  private readonly shown = signal(true);

  readonly template: Signal<TemplateRef<unknown> | null> = this.current.asReadonly();
  /**
   * Read by the shell, which reserves the height so the bar never covers the last line of a page.
   *
   * False while a bar that waits for its anchor has not yet appeared: reserving 60px of blank
   * ground for a bar that is not on screen is the same bug as the bar covering the last line, seen
   * from the other side.
   */
  readonly active = computed(() => this.current() !== null && this.shown());
  /** CSS selector of the element the bar waits to see leave the viewport, or null for "always". */
  readonly revealAnchor: Signal<string | null> = this.anchor.asReadonly();
  /** Whether the bar disappears from 'lg' up, because the page already shows the action in place. */
  readonly mobileOnly: Signal<boolean> = this.phoneOnly.asReadonly();

  register(template: TemplateRef<unknown>, mobileOnly = false, revealAfter: string | null = null): void {
    this.current.set(template);
    this.phoneOnly.set(mobileOnly);
    this.anchor.set(revealAfter);
    // A bar with an anchor starts hidden (so the server-rendered HTML never shows a duplicate of
    // the inline button) and `StickyActionBar` shows it once it has looked at the anchor, or at
    // once if it cannot look.
    this.shown.set(revealAfter === null);
  }

  /** Written by `StickyActionBar` only: whether the bar is currently on screen. */
  setShown(shown: boolean): void {
    this.shown.set(shown);
  }

  /**
   * Clears the bar, but only if the caller still owns it.
   *
   * During a route change the incoming page registers before the outgoing one is destroyed, so an
   * unconditional clear on destroy would take away the new page's bar.
   */
  release(template: TemplateRef<unknown>): void {
    if (this.current() === template) this.current.set(null);
  }
}

/**
 * Declares a page's sticky action.
 *
 * ```html
 * <ng-template khStickyAction>
 *   <button khButton variant="primary" block>Add to cart</button>
 * </ng-template>
 * ```
 *
 * `mobileOnly` hides the bar from 'lg' up, for a page whose own layout already shows the same
 * action on a wide screen — the product page's buy box, where a second full-width "Add to cart"
 * pinned under it is just a duplicate. Checkout leaves it off: its bar is the only "Continue".
 *
 * `revealAfter` is a CSS selector for the page's own in-place copy of the action. While that
 * element is on screen the bar stays out of the way, and it slides in once the element has left
 * the viewport: two "Add to cart" buttons visible at once at the top of a product page was the
 * thing this exists to stop. Left off (cart, checkout), the bar is always shown, as before.
 */
@Directive({ selector: 'ng-template[khStickyAction]' })
export class StickyAction implements OnInit {
  readonly mobileOnly = input(false, { transform: booleanAttribute });
  readonly revealAfter = input<string | null>(null);

  private readonly template = inject(TemplateRef);
  private readonly service = inject(StickyActionBarService);

  constructor() {
    inject(DestroyRef).onDestroy(() => this.service.release(this.template));
  }

  // Registered once inputs are bound, so `mobileOnly` is known when the bar first renders.
  ngOnInit(): void {
    this.service.register(this.template, this.mobileOnly(), this.revealAfter());
  }
}

/** Renders whatever the current page registered. Mounted once, by the shell. */
@Component({
  selector: 'kh-sticky-action-bar',
  imports: [NgTemplateOutlet],
  template: `
    @if (template(); as content) {
      <!-- \`inert\` while off screen, so the buttons of a hidden bar are not in the tab order and
           not announced: the page's own copy of the action is the one a keyboard user reaches. -->
      <div
        class="bar"
        [class.mobile-only]="mobileOnly()"
        [class.is-hidden]="!visible()"
        [attr.inert]="visible() ? null : ''"
      >
        <ng-container [ngTemplateOutlet]="content" />
      </div>
    }
  `,
  styles: `
    .bar {
      position: fixed;
      inset-inline: 0;
      inset-block-end: 0;
      z-index: var(--z-header);
      display: flex;
      align-items: center;
      gap: var(--space-3);
      min-height: var(--bottom-bar-height);
      padding: var(--space-2) var(--space-4);
      /* The home indicator on a modern phone sits over the bottom of the viewport. */
      padding-block-end: max(var(--space-2), env(safe-area-inset-bottom));
      background: var(--color-surface-raised);
      border-block-start: 1px solid var(--color-border);
      box-shadow: var(--shadow-lg);
      transform: translateY(0);
      transition:
        transform var(--duration-slow) var(--ease-out),
        visibility 0s linear 0s;
    }

    /* Slides down out of the thumb zone and is then \`visibility: hidden\`, the latter delayed until
       the slide has finished. Under reduced motion the duration token is 0ms, so both are instant. */
    .bar.is-hidden {
      transform: translateY(100%);
      visibility: hidden;
      transition:
        transform var(--duration-slow) var(--ease-out),
        visibility 0s linear var(--duration-slow);
    }

    /* From the 'lg' breakpoint the action moves back into the page: a fixed bar across a 1440px screen is a lot
       of furniture for one button. 1024px is the \`lg\` breakpoint from _breakpoints.scss. */
    @media (min-width: 1024px) {
      .bar {
        position: sticky;
        box-shadow: none;
        max-width: var(--container-max);
        margin-inline: auto;
      }

      .bar.mobile-only {
        display: none;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StickyActionBar {
  private readonly service = inject(StickyActionBarService);
  protected readonly template = this.service.template;
  protected readonly mobileOnly = this.service.mobileOnly;

  /** Whether the bar is currently on screen. Mirrors the service so the shell reserves space to match. */
  protected readonly visible = computed(() => this.service.active());

  constructor() {
    // Watches the page's own copy of the action. Runs only in the browser (after render), so on the
    // server an anchored bar stays hidden and nothing flashes on hydration.
    afterRenderEffect((onCleanup) => {
      const selector = this.service.revealAnchor();
      if (this.template() === null || selector === null) return;

      const target = document.querySelector(selector);
      // Cannot look, so do not hide: a bar that never appears is worse than a duplicate button.
      if (!target || typeof IntersectionObserver === 'undefined') {
        this.service.setShown(true);
        return;
      }

      const observer = new IntersectionObserver(([entry]) => this.service.setShown(!entry.isIntersecting));
      observer.observe(target);
      onCleanup(() => observer.disconnect());
    });
  }
}
