import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FeatureFlagResponse, PlatformSettingsService, RolloutModel } from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import { ConfirmDialog, PageHeader, humanise } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Control, Field, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';

/** What each flag area is called on screen; an area not listed is spaced out from its key. */
const AREA_LABELS: Readonly<Record<string, string>> = {
  content: 'Storefront content',
  identity: 'Sign-in and accounts',
  notifications: 'Messages',
  platform: 'Platform',
  pricing: 'Pricing and credit',
  reporting: 'Reports',
  reviews: 'Reviews and wishlists',
  search: 'Search',
};

const PERMANENT_GAP = /permanent gap/i;

/**
 * Feature flags.
 *
 * **These are kill switches, not preferences.** Several of them turn off a whole capability that
 * depends on something outside this deployment — SMS delivery, a payment gateway, a courier
 * account (Step 7A's four flags among them) — so switching one off is how a store keeps working
 * when a third party does not. That is why turning one off is confirmed and turning it on is not:
 * the risk sits with enabling something whose dependency may be missing.
 *
 * **A rollout is sent whole.** The percentage, the named users and the segments go together,
 * because a partial write is how a flag ends up reading as on and being enabled for nobody. The
 * editor therefore opens a flag's whole rollout rather than offering a toggle beside three
 * separate fields.
 *
 * The flags themselves are declared by the modules that read them; nothing here can create one,
 * which is correct — a flag with no code behind it is a switch that does nothing.
 */
@Component({
  selector: 'kh-feature-flags-page',
  imports: [Alert, Badge, Button, ConfirmDialog, Control, Field, HasPermission, PageHeader, Skeleton],
  template: `
    <kh-page-header heading="Feature flags" description="Switches that turn admin features on or off." />

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="Flags could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="16rem" />
    } @else {
      @if (actionError(); as message) {
        <kh-alert tone="danger" heading="Something went wrong">{{ message }}</kh-alert>
      }

      @for (group of groups(); track group.area) {
        <section class="group" [attr.aria-labelledby]="'flags-' + group.area">
          <h2 class="group-title" [id]="'flags-' + group.area">{{ group.label }}</h2>
          <ul>
            @for (flag of group.flags; track flag.key) {
              <li>
                <div class="details">
                  <!-- The description leads: it says what the switch does, the code is only its name. -->
                  <span class="what">{{ flag.description }}</span>
                  <span class="key">
                    <code>{{ flag.key }}</code>
                    @if (flag.enabled && isPartial(flag)) {
                      <kh-badge tone="warning">{{ describeRollout(flag.rollout) }}</kh-badge>
                    }
                  </span>
                </div>

                <div class="actions" *khHasPermission="'platform.settings.manage'">
                  <button khButton type="button" size="sm" variant="tertiary" (click)="startEdit(flag)">
                    Rollout
                  </button>
                  <button
                    type="button"
                    class="switch"
                    role="switch"
                    [attr.aria-checked]="flag.enabled"
                    [attr.aria-label]="flag.key"
                    [disabled]="busyKey() === flag.key"
                    (click)="flag.enabled ? disabling.set(flag) : enable(flag)"
                  >
                    <span class="knob" aria-hidden="true"></span>
                    <span class="state">{{ flag.enabled ? 'On' : 'Off' }}</span>
                  </button>
                </div>
              </li>
            }
          </ul>
        </section>
      } @empty {
        <p class="hint">No feature flag is declared for this deployment.</p>
      }

      @if (editing(); as flag) {
        <section class="panel">
          <h2>
            Rollout for <code>{{ flag.key }}</code>
          </h2>
          <p class="hint">
            Sent as one write. A percentage of nought with no named users and no segments means the flag is on
            and reaches nobody.
          </p>

          <kh-field label="Percentage of users" for="rollout-percentage" hint="0 to 100.">
            <input
              khControl
              id="rollout-percentage"
              type="number"
              min="0"
              max="100"
              [value]="percentage()"
              (input)="percentage.set($any($event.target).value)"
            />
          </kh-field>

          <kh-field label="Named users" for="rollout-users" [optional]="true" hint="User ids, one per line.">
            <textarea
              khControl
              id="rollout-users"
              rows="3"
              [value]="userIds()"
              (input)="userIds.set($any($event.target).value)"
            ></textarea>
          </kh-field>

          <kh-field label="Segments" for="rollout-segments" [optional]="true" hint="One per line.">
            <textarea
              khControl
              id="rollout-segments"
              rows="2"
              [value]="segments()"
              (input)="segments.set($any($event.target).value)"
            ></textarea>
          </kh-field>

          <div class="panel-actions">
            <button khButton type="button" variant="tertiary" (click)="editing.set(null)">Cancel</button>
            <button
              khButton
              type="button"
              variant="primary"
              [disabled]="busyKey() !== null"
              (click)="saveRollout()"
            >
              {{ busyKey() !== null ? 'Saving…' : 'Save the rollout' }}
            </button>
          </div>
        </section>
      }
    }

    <kh-confirm-dialog
      [open]="disabling() !== null"
      [heading]="disablingPermanent() ? 'Switch off, and lose data for good' : 'Switch this flag off'"
      [message]="disableMessage()"
      [confirmLabel]="disablingPermanent() ? 'Switch it off anyway' : 'Switch it off'"
      [tone]="disablingPermanent() ? 'danger' : 'warning'"
      [busy]="busyKey() !== null"
      (confirmed)="disable()"
      (cancelled)="disabling.set(null)"
    />
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .group {
      margin-block-end: var(--space-5);
    }

    .group-title {
      margin: 0 0 var(--space-2);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
      font-weight: var(--weight-semibold);
      letter-spacing: 0.04em;
      text-transform: uppercase;
    }

    .what {
      display: block;
      font-weight: var(--weight-medium);
    }

    .key {
      margin-block-start: var(--space-1);
    }

    code {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    /* A real switch: one control that says what it is now, instead of a filled "Switch on" button on
       twelve rows. Off is quiet, on is the brand colour. */
    .switch {
      display: inline-flex;
      gap: var(--space-2);
      align-items: center;
      min-block-size: 2rem;
      padding: 0;
      border: 0;
      background: none;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
      cursor: pointer;
    }

    .switch:disabled {
      cursor: wait;
      opacity: 0.6;
    }

    .switch:focus-visible {
      outline: 2px solid var(--color-focus-ring);
      outline-offset: 2px;
    }

    .knob {
      position: relative;
      inline-size: 2.5rem;
      block-size: 1.5rem;
      border-radius: var(--radius-full);
      background: var(--color-border-strong);
      transition: background var(--duration-fast) var(--ease-standard);
    }

    .knob::after {
      content: '';
      position: absolute;
      inset-block-start: var(--space-1);
      inset-inline-start: var(--space-1);
      inline-size: 1rem;
      block-size: 1rem;
      border-radius: var(--radius-full);
      background: var(--color-surface-raised);
      transition: transform var(--duration-fast) var(--ease-standard);
    }

    .switch[aria-checked='true'] {
      color: var(--color-text);
    }

    .switch[aria-checked='true'] .knob {
      background: var(--color-primary);
    }

    .switch[aria-checked='true'] .knob::after {
      transform: translateX(1rem);
    }

    .state {
      min-inline-size: 1.75rem;
    }

    @media (pointer: coarse) {
      .switch,
      button[khButton] {
        min-block-size: var(--touch-target-min);
      }
    }

    li {
      display: flex;
      gap: var(--space-3);
      align-items: flex-start;
      justify-content: space-between;
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      margin-block-end: var(--space-2);
    }

    .key {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      flex-wrap: wrap;
    }

    code {
      font-family: var(--font-mono);
      font-weight: var(--weight-medium);
    }

    .note {
      display: block;
      margin-block-start: var(--space-1);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .actions {
      display: flex;
      flex: none;
      gap: var(--space-2);
      align-items: center;
    }

    .panel {
      margin-block-start: var(--space-4);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
    }

    .hint {
      margin: 0 0 var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .panel-actions {
      display: flex;
      gap: var(--space-2);
      margin-block-start: var(--space-3);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FeatureFlagsPage {
  private readonly settings = inject(PlatformSettingsService);
  private readonly toasts = inject(ToastService);

  protected readonly flags = signal<readonly FeatureFlagResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly busyKey = signal<string | null>(null);

  protected readonly editing = signal<FeatureFlagResponse | null>(null);
  protected readonly disabling = signal<FeatureFlagResponse | null>(null);
  protected readonly percentage = signal('100');
  protected readonly userIds = signal('');
  protected readonly segments = signal('');

  constructor() {
    this.load();
  }

  /** On, but reaching only some people — worth saying, because "On" alone would be misleading. */
  protected isPartial(flag: FeatureFlagResponse): boolean {
    return (
      flag.rollout.percentage < 100 || flag.rollout.userIds.length > 0 || flag.rollout.segments.length > 0
    );
  }

  protected describeRollout(rollout: RolloutModel): string {
    const parts: string[] = [];
    if (rollout.percentage < 100) parts.push(`${rollout.percentage}% of users`);
    if (rollout.userIds.length > 0) parts.push(`${rollout.userIds.length} named`);
    if (rollout.segments.length > 0) parts.push(rollout.segments.join(', '));
    return parts.join(' · ');
  }

  /** The flags are keyed `area.name`; the area is the heading they are listed under. */
  protected readonly groups = computed(() => {
    const byArea = new Map<string, FeatureFlagResponse[]>();
    for (const flag of this.flags()) {
      const area = flag.key.split('.')[0];
      byArea.set(area, [...(byArea.get(area) ?? []), flag]);
    }
    return [...byArea.entries()]
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([area, flags]) => ({ area, label: AREA_LABELS[area] ?? humanise(area), flags }));
  });

  /** A flag whose description says that switching it off leaves "a permanent gap" in recorded data. */
  protected readonly disablingPermanent = computed(() => {
    const flag = this.disabling();
    return flag ? PERMANENT_GAP.test(flag.description) : false;
  });

  protected disableMessage(): string {
    const flag = this.disabling();
    if (!flag) return '';
    return this.disablingPermanent()
      ? `${flag.description} Anything that happens while this is off is never recorded, and switching it back on does not fill the gap.`
      : `Everything behind ${flag.key} stops working for everybody, immediately. ${flag.description}`;
  }

  protected startEdit(flag: FeatureFlagResponse): void {
    this.editing.set(flag);
    this.percentage.set(String(flag.rollout.percentage));
    this.userIds.set(flag.rollout.userIds.join('\n'));
    this.segments.set(flag.rollout.segments.join('\n'));
  }

  protected enable(flag: FeatureFlagResponse): void {
    this.write(flag, true, flag.rollout);
  }

  protected disable(): void {
    const flag = this.disabling();
    if (!flag) return;
    this.write(flag, false, flag.rollout);
  }

  protected saveRollout(): void {
    const flag = this.editing();
    if (!flag) return;

    this.write(flag, flag.enabled, {
      percentage: clamp(Number(this.percentage())),
      userIds: lines(this.userIds()),
      segments: lines(this.segments()),
    });
  }

  private write(flag: FeatureFlagResponse, enabled: boolean, rollout: RolloutModel): void {
    this.busyKey.set(flag.key);
    this.actionError.set(null);

    this.settings.updateFlag(flag.key, enabled, rollout, flag.description).subscribe({
      next: () => {
        this.busyKey.set(null);
        this.disabling.set(null);
        this.editing.set(null);
        this.toasts.success(`${flag.key} saved.`);
        this.load();
      },
      error: (error: unknown) => {
        this.busyKey.set(null);
        this.disabling.set(null);
        this.actionError.set(describeError(error, 'That flag could not be changed.'));
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.settings.flags().subscribe({
      next: (flags) => {
        this.loading.set(false);
        this.flags.set(flags);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'They could not be loaded.'));
      },
    });
  }
}

function clamp(value: number): number {
  if (!Number.isFinite(value)) return 0;
  return Math.min(100, Math.max(0, Math.round(value)));
}

function lines(value: string): string[] {
  return value
    .split('\n')
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}
