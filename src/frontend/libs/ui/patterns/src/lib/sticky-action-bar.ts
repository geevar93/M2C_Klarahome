import { NgTemplateOutlet } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  Directive,
  Injectable,
  Signal,
  TemplateRef,
  computed,
  inject,
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

  readonly template: Signal<TemplateRef<unknown> | null> = this.current.asReadonly();
  /** Read by the shell, which reserves the height so the bar never covers the last line of a page. */
  readonly active = computed(() => this.current() !== null);

  register(template: TemplateRef<unknown>): void {
    this.current.set(template);
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
 */
@Directive({ selector: 'ng-template[khStickyAction]' })
export class StickyAction {
  constructor() {
    const template = inject(TemplateRef);
    const service = inject(StickyActionBarService);
    service.register(template);
    inject(DestroyRef).onDestroy(() => service.release(template));
  }
}

/** Renders whatever the current page registered. Mounted once, by the shell. */
@Component({
  selector: 'kh-sticky-action-bar',
  imports: [NgTemplateOutlet],
  template: `
    @if (template(); as content) {
      <div class="bar">
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
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StickyActionBar {
  protected readonly template = inject(StickyActionBarService).template;
}
