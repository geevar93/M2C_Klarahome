import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { KhDatePipe } from '@klarahome/i18n';
import { Icon } from '@klarahome/ui-primitives';

import { TimelineEntryView } from './commerce.model';

/**
 * What has happened to an order, in the order it happened.
 *
 * An ordered list, newest last, because that is the direction a delivery reads — placed, confirmed,
 * packed, dispatched, out for delivery, delivered — and reversing it to put the latest on top turns
 * a progress story into a log file.
 *
 * **Only what the customer is allowed to see reaches this component.** The order timeline the API
 * keeps is an append-only audit trail that also records who inside the business did what (Step 14);
 * the mapper drops everything not marked customer-visible before it gets here, so there is no way
 * for an internal note to be one CSS rule away from a shopper.
 *
 * The current entry is marked, not just styled: `aria-current="step"` is what tells a screen-reader
 * user where the parcel is, and the colour is what tells everybody else.
 */
@Component({
  selector: 'kh-order-timeline',
  imports: [Icon, KhDatePipe],
  template: `
    <ol>
      @for (entry of entries(); track entry.id) {
        <li [class.current]="entry.isCurrent" [attr.aria-current]="entry.isCurrent ? 'step' : null">
          <span class="marker" aria-hidden="true">
            <kh-icon [name]="entry.isCurrent ? 'truck' : 'check'" size="sm" />
          </span>
          <span class="body">
            <span class="label">{{ entry.label }}</span>
            @if (entry.detail) {
              <span class="detail">{{ entry.detail }}</span>
            }
            <time class="when" [attr.datetime]="entry.occurredAt">{{ entry.occurredAt | khDate }}</time>
          </span>
        </li>
      } @empty {
        <li class="empty">Nothing has happened to this order yet.</li>
      }
    </ol>
  `,
  styles: `
    :host {
      display: block;
    }

    ol {
      list-style: none;
      margin: 0;
      padding: 0;
    }

    li {
      display: flex;
      gap: var(--space-3);
      padding-block-end: var(--space-4);
      position: relative;
    }

    /* The rail between the markers. Drawn on every entry but the last, so the line stops at the
       most recent event rather than trailing off into whatever comes after it. */
    li:not(:last-child)::before {
      content: '';
      position: absolute;
      inset-block: var(--space-6) 0;
      inset-inline-start: calc(var(--space-3) - 1px);
      inline-size: 2px;
      background: var(--color-border);
    }

    .marker {
      display: grid;
      place-items: center;
      flex: none;
      inline-size: var(--space-6);
      block-size: var(--space-6);
      border-radius: var(--radius-full);
      background: var(--color-surface);
      color: var(--color-text-muted);
    }

    li.current .marker {
      background: var(--color-primary-subtle);
      color: var(--color-primary);
    }

    .body {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      min-width: 0;
      font-size: var(--text-sm);
    }

    .label {
      font-weight: var(--weight-medium);
    }

    li.current .label {
      color: var(--color-primary);
    }

    .detail,
    .when {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .empty {
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OrderTimeline {
  readonly entries = input.required<readonly TimelineEntryView[]>();
}
