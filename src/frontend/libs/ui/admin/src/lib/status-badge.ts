import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Badge } from '@klarahome/ui-primitives';

import { BadgeTone } from './admin.model';

/**
 * A status, drawn the one way the back office draws every status.
 *
 * Give it the status as the API spells it (`PartiallyShipped`, `under_review`) and it humanises
 * the label and picks the tone from the single vocabulary below. A page that needs to say
 * something the vocabulary does not cover passes `tone` itself; it never invents a colour.
 *
 * The meaning of each tone is the same on every screen, which is the entire point of having one
 * component for it:
 *
 *  - **green** (`success`)  done, healthy: paid, delivered, active, published
 *  - **orange** (`warning`) waiting on somebody: pending, unfulfilled, awaiting, low stock
 *  - **blue** (`info`)      in progress or not live yet: shipped, draft, scheduled
 *  - **red** (`danger`)     a problem: failed, rejected, suspended, out of stock
 *  - **grey** (`neutral`)   inactive or finished with: cancelled, archived, expired, refunded
 *
 * The tone is carried by a dot and the label as well as by colour (WCAG 1.4.1): the text always
 * says the status, so nobody has to tell orange from red to read it.
 */
@Component({
  selector: 'kh-status-badge',
  imports: [Badge],
  template: `<kh-badge [tone]="resolvedTone()"
    ><span class="dot" aria-hidden="true"></span>{{ label() ?? text() }}</kh-badge
  >`,
  styles: `
    .dot {
      flex: none;
      width: 0.375rem;
      height: 0.375rem;
      border-radius: var(--radius-full);
      background: currentColor;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusBadge {
  readonly status = input<string | null | undefined>(null);
  readonly tone = input<BadgeTone | null>(null);
  /** The words to show when they are not just the humanised status (a page's own vocabulary). */
  readonly label = input<string | null>(null);

  protected readonly text = computed(() => humanise(this.status()));
  protected readonly resolvedTone = computed(() => this.tone() ?? toneFor(this.status()));
}

/** `PartiallyShipped` / `under_review` / `UNDER-REVIEW` → `Partially shipped` / `Under review`. */
export function humanise(status: string | null | undefined): string {
  if (!status) return '—';
  const spaced = status
    .replace(/[_-]+/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .trim()
    .toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

/** Finished with or switched off. Checked first: `partially refunded` must not read as "partial". */
const NEUTRAL = [
  'cancelled',
  'canceled',
  'archived',
  'expired',
  'disabled',
  'inactive',
  'refunded',
  'closed',
  'void',
];
const DANGER = [
  'failed',
  'rejected',
  'suspended',
  'locked',
  'bounced',
  'error',
  'disputed',
  'blocked',
  'offboarded',
  'out of stock',
  'overdue',
  'exception',
];
const WARNING = [
  'pending',
  'awaiting',
  'processing',
  'queued',
  'review',
  'hold',
  'partial',
  'retry',
  'submitted',
  'unverified',
  'unfulfilled',
  'low stock',
  'unpaid',
];
const INFO = [
  'in progress',
  'shipped',
  'dispatched',
  'in transit',
  'out for delivery',
  'picked',
  'label generated',
  'created',
  'draft',
  'scheduled',
  'returned',
  'returning',
  'rto initiated',
  'confirmed',
  'packed',
];
const SUCCESS = [
  'active',
  'approved',
  'completed',
  'delivered',
  'paid',
  'published',
  'sent',
  'verified',
  'settled',
  'received',
  'enabled',
  'succeeded',
  'fulfilled',
  'in stock',
  'live',
];

/** The tone for a status string. The order of the checks is the vocabulary's precedence. */
export function toneFor(status: string | null | undefined): BadgeTone {
  if (!status) return 'neutral';
  const text = humanise(status).toLowerCase();
  if (NEUTRAL.some((word) => text.includes(word))) return 'neutral';
  // "Partially shipped" is under way, not waiting: it is blue, and only a bare "partial" is orange.
  if (text.includes('partially')) return 'info';
  if (DANGER.some((word) => text.includes(word))) return 'danger';
  if (WARNING.some((word) => text.includes(word))) return 'warning';
  if (INFO.some((word) => text.includes(word))) return 'info';
  if (SUCCESS.some((word) => text.includes(word))) return 'success';
  return 'neutral';
}
