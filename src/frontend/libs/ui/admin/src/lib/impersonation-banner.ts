import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { Button, Icon } from '@klarahome/ui-primitives';

/** What the banner needs to say. The store behind it is the app's, not this library's. */
export interface ImpersonationView {
  /** Who is being acted as, as an operator would recognise them. */
  readonly displayName: string;
  /** Why, in the operator's own words. */
  readonly reason: string;
  /** When it stops being honoured, as epoch milliseconds. */
  readonly expiresAt: number;
}

/**
 * The strip that says an operator is acting as somebody else.
 *
 * **"Visibly flagged in the session" is a security requirement, not a nicety**
 * (docs/07-security-compliance.md §2). An impersonation that is not on screen the whole time it is
 * running is one somebody forgets they are in, and the next thing they do is done in a customer's
 * name without meaning to.
 *
 * So it is the loudest thing on the page and it does not scroll away: full-bleed, above the header,
 * a warning tone, the customer's name, the reason that was given, and a countdown. The countdown is
 * the part that is easy to leave out and worth the ticking timer — a support session that is about
 * to end without warning is one that ends mid-investigation.
 *
 * The exit control is a button rather than a link, because leaving is an action with a server call
 * behind it: the session is revoked and the stop is audited. Closing the tab does not do that; the
 * clock does, eventually, and that is exactly the outcome the button exists to avoid.
 */
@Component({
  selector: 'kh-impersonation-banner',
  imports: [Button, Icon],
  template: `
    <div class="banner" role="status">
      <kh-icon name="alert" size="sm" />

      <p>
        <strong>You are acting as {{ view().displayName }}.</strong>
        <span class="reason">{{ view().reason }}</span>
      </p>

      <span class="remaining" [class.urgent]="isUrgent()">{{ remaining() }}</span>

      <button khButton type="button" size="sm" variant="tertiary" [disabled]="busy()" (click)="exited.emit()">
        {{ busy() ? 'Stopping…' : 'Stop' }}
      </button>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }

    .banner {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      padding: var(--space-2) var(--space-4);
      background: var(--color-warning-surface);
      color: var(--color-warning-text);
    }

    p {
      display: flex;
      flex: 1;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: baseline;
      margin: 0;
      min-inline-size: 0;
    }

    .reason,
    .remaining {
      font-size: var(--text-xs);
    }

    .remaining {
      font-variant-numeric: tabular-nums;
      white-space: nowrap;
    }

    .remaining.urgent {
      font-weight: var(--weight-bold);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ImpersonationBanner {
  private readonly destroyRef = inject(DestroyRef);

  readonly view = input.required<ImpersonationView>();

  /** Whether the stop is in flight. */
  readonly busy = input(false);

  readonly exited = output<void>();

  /** Ticked once a second, which is the resolution a countdown is read at. */
  private readonly now = signal(Date.now());

  protected readonly remaining = computed(() => {
    const left = Math.max(0, this.view().expiresAt - this.now());

    if (left === 0) {
      return 'Expired';
    }

    const minutes = Math.floor(left / 60_000);
    const seconds = Math.floor((left % 60_000) / 1000);

    return `${minutes}:${seconds.toString().padStart(2, '0')} left`;
  });

  /** The last two minutes, where the number stops being background information. */
  protected readonly isUrgent = computed(() => this.view().expiresAt - this.now() <= 120_000);

  constructor() {
    const tick = setInterval(() => this.now.set(Date.now()), 1000);
    this.destroyRef.onDestroy(() => clearInterval(tick));
  }
}
