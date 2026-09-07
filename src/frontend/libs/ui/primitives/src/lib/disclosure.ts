import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';

/**
 * A titled section that opens and closes.
 *
 * `<details>` / `<summary>`, not a `<div>` with a click handler. The browser gives it the right
 * role, the right keyboard behaviour and — the part that matters for a storefront — **the content
 * is in the DOM and findable by the page's own find-in-page even while collapsed**, which is how a
 * shopper looking for a warranty clause on a phone actually finds it. It also means a crawler
 * reads the specifications on a PDP without executing anything.
 *
 * The marker is a rotating chevron drawn in CSS rather than an icon component, so that a
 * collapsed section costs no extra element per row on a filter panel with fifteen groups.
 */
@Component({
  selector: 'kh-disclosure',
  template: `
    <details [attr.open]="open() ? '' : null" (toggle)="open.set($any($event.target).open)">
      <summary>
        <span class="title">{{ heading() }}</span>
        @if (hint()) {
          <span class="hint">{{ hint() }}</span>
        }
      </summary>
      <div class="body">
        <ng-content />
      </div>
    </details>
  `,
  styles: `
    :host {
      display: block;
      border-block-end: 1px solid var(--color-border);
    }

    summary {
      display: flex;
      align-items: center;
      gap: var(--space-2);
      min-block-size: var(--touch-target-min);
      padding-block: var(--space-3);
      cursor: pointer;
      font-weight: var(--weight-medium);
      /* The default triangle is replaced by the chevron below; it cannot be styled or positioned. */
      list-style: none;
    }

    summary::-webkit-details-marker {
      display: none;
    }

    .title {
      flex: 1;
      min-inline-size: 0;
    }

    .hint {
      font-size: var(--text-sm);
      font-weight: var(--weight-regular);
      color: var(--color-text-muted);
    }

    summary::after {
      content: '';
      inline-size: 0.5rem;
      block-size: 0.5rem;
      border-inline-end: 2px solid var(--color-text-muted);
      border-block-end: 2px solid var(--color-text-muted);
      transform: rotate(45deg);
      transform-origin: center;
      transition: transform var(--duration-fast) var(--ease-standard);
    }

    details[open] summary::after {
      transform: rotate(-135deg);
    }

    .body {
      padding-block-end: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Disclosure {
  readonly heading = input.required<string>();
  /** A short right-aligned note — "3 selected", "12 items". */
  readonly hint = input<string | null>(null);
  /**
   * Two-way, because both sides legitimately change it: the user by clicking, and the page by
   * opening the group a URL filter is already applied to.
   */
  readonly open = model(false);
}
