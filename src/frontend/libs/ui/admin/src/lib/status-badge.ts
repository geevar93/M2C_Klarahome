import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Badge } from '@klarahome/ui-primitives';

import { BadgeTone } from './admin.model';

/**
 * A status, drawn in the tone its meaning deserves.
 *
 * The back office shows perhaps sixty distinct status values across orders, shipments, returns,
 * payouts, products and users, and the API spells them in PascalCase (`AwaitingDispatch`,
 * `PartiallyRefunded`). Two things have to happen to every one of them: it has to be readable, and
 * it has to be the right colour. Doing that at each of the forty places a status appears produces
 * forty slightly different answers, one of which will paint `Failed` green.
 *
 * The mapping is by **suffix and keyword rather than by an exhaustive list**, so a status this
 * build has never seen still lands somewhere sensible instead of rendering untoned. A module that
 * needs a different answer passes `tone` explicitly — that is the escape hatch, and it is rare.
 *
 * **Colour is never the only signal**: the word itself is always rendered (WCAG 1.4.1), which is
 * also what keeps this honest after Step 30 repaints every token.
 */
@Component({
  selector: 'kh-status-badge',
  imports: [Badge],
  template: `<kh-badge [tone]="resolvedTone()">{{ text() }}</kh-badge>`,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusBadge {
  /** The status as the API spells it. Null renders an em dash rather than an empty badge. */
  readonly status = input<string | null | undefined>(null);
  /** Overrides the derived tone, for a status whose meaning this component cannot know. */
  readonly tone = input<BadgeTone | null>(null);

  protected readonly text = computed(() => humanise(this.status()));
  protected readonly resolvedTone = computed(() => this.tone() ?? toneFor(this.status()));
}

/** `AwaitingDispatch` → `Awaiting dispatch`; `partially_refunded` → `Partially refunded`. */
export function humanise(status: string | null | undefined): string {
  if (!status) return '—';
  const spaced = status
    .replace(/[_-]+/g, ' ')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .trim()
    .toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

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
  'closed',
];
const WARNING = [
  'pending',
  'awaiting',
  'processing',
  'queued',
  'draft',
  'review',
  'hold',
  'partial',
  'retry',
  'scheduled',
  'submitted',
  'unverified',
];
const DANGER = [
  'failed',
  'rejected',
  'cancelled',
  'canceled',
  'suspended',
  'locked',
  'bounced',
  'expired',
  'error',
  'disputed',
  'blocked',
  'offboarded',
];
const INFO = ['shipped', 'dispatched', 'in transit', 'refunded', 'returned', 'archived', 'created'];

/** The tone a status word implies. Unknown words are neutral, which is the honest answer. */
export function toneFor(status: string | null | undefined): BadgeTone {
  if (!status) return 'neutral';
  const text = humanise(status).toLowerCase();
  if (DANGER.some((word) => text.includes(word))) return 'danger';
  if (WARNING.some((word) => text.includes(word))) return 'warning';
  if (SUCCESS.some((word) => text.includes(word))) return 'success';
  if (INFO.some((word) => text.includes(word))) return 'info';
  return 'neutral';
}
