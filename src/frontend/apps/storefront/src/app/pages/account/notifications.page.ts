import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NotificationPreferencesStore, PreferenceResponse } from '@klarahome/data-access-account';
import { Alert, Checkbox, ErrorState, PageHeader, Skeleton } from '@klarahome/ui-primitives';
import { FeatureFlags, ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';

/**
 * How each channel is labelled, which field of a preference row it is, and the flag that gates it.
 * `null` means always on — in-app has no flag because it has nothing to be switched off by.
 */
const CHANNELS: readonly {
  readonly key: 'email' | 'sms' | 'whatsApp' | 'inApp';
  readonly label: string;
  readonly flag: string | null;
}[] = [
  { key: 'email', label: 'Email', flag: 'notifications.email' },
  { key: 'sms', label: 'SMS', flag: 'notifications.sms' },
  { key: 'whatsApp', label: 'WhatsApp', flag: 'notifications.whatsapp' },
  { key: 'inApp', label: 'In the app', flag: null },
];

/**
 * Notification preferences — `/account/notifications`.
 *
 * A grid: a row per category, a tick per channel. **Both axes come from the API** (Step 8) — the
 * categories are what the platform actually sends, and the columns are `availableChannels`, so a
 * deployment with no WhatsApp account does not show a WhatsApp column that could never do anything.
 *
 * Saved **one row at a time**, which is the endpoint's shape and the right one: a customer ticking
 * one box should not have their other nine preferences rewritten from whatever the page happened to
 * be holding.
 *
 * The state after a save is the **server's**, not the click's. Some combinations are refused
 * outright — an order confirmation cannot be switched off, because it carries the invoice — and a
 * page that assumed its own write had been honoured would leave a tick in a position the server
 * disagrees with.
 */
@Component({
  selector: 'kh-account-notifications-page',
  imports: [Alert, Checkbox, ErrorState, PageHeader, Skeleton],
  template: `
    <kh-page-header
      title="Notifications"
      lead="Choose what we tell you about, and where. Some messages about an order you have placed are sent whatever you choose here — they are part of buying something."
    />

    <!-- A channel this deployment has not switched on yet is still shown below, so the customer can
         see it exists and set a preference ahead of time — but a tick they cannot yet act on needs
         saying, or a disabled SMS column reads as a bug rather than a "not yet". -->
    @if (comingSoonMessage(); as message) {
      <kh-alert tone="info">{{ message }}</kh-alert>
    }

    @if (!store.hasLoaded()) {
      <kh-skeleton height="12rem" />
    } @else if (error()) {
      <kh-error-state (retry)="load()" />
    } @else if (store.categories().length === 0) {
      <kh-alert tone="info">There is nothing to configure on this store yet.</kh-alert>
    } @else {
      <div class="grid">
        @for (category of store.categories(); track category.category) {
          <fieldset [class.busy]="store.savingCategory() === category.category">
            <legend>{{ category.category }}</legend>
            <p class="description">{{ category.description }}</p>

            <div class="channels">
              @for (channel of visibleChannels(); track channel.key) {
                <kh-checkbox
                  [bare]="true"
                  [label]="channel.label"
                  [inputId]="category.category + '-' + channel.key"
                  [checked]="isOn(category, channel.key)"
                  [disabled]="isChannelDisabled(channel.key) || store.savingCategory() === category.category"
                  (checkedChange)="toggle(category, channel.key, $event)"
                />
              }
            </div>
          </fieldset>
        }
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .grid {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
    }

    fieldset {
      margin: 0;
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
    }

    fieldset.busy {
      opacity: 0.6;
    }

    /* A legend straddles the fieldset border by default. Floating it pulls it inside the card, so
       the heading sits above the description like any other card title instead of cutting the top
       edge. The description clears the float so it starts on the next line. */
    legend {
      float: left;
      inline-size: 100%;
      margin: 0 0 var(--space-1);
      padding: 0;
      font-size: var(--text-base);
      font-weight: var(--weight-medium);
    }

    .description {
      clear: both;
      margin: 0 0 var(--space-2);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    /* The column gap is deliberately wider than the gap inside a choice (\`--space-3\`, between the
       tick and its label). Ticks and labels alternate along this row, and when the two gaps are
       close in size the eye has nothing to tell it that a label belongs to the box on its left
       rather than the one on its right — which is what 'Email SMS In the app' read as. */
    .channels {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2) var(--space-6);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountNotificationsPage {
  protected readonly store = inject(NotificationPreferencesStore);
  private readonly flags = inject(FeatureFlags);
  private readonly toasts = inject(ToastService);

  protected readonly error = signal(false);

  /** The columns this deployment can actually send on. */
  protected readonly visibleChannels = computed(() => {
    const available = this.store.channels().map((channel) => channel.toLowerCase());
    return CHANNELS.filter((channel) => available.includes(channel.key.toLowerCase()));
  });

  /**
   * Visible channels whose flag is off. A `computed` over the flag set, not a one-off read, so an
   * operator switching the flag on a live tab moves the checkbox from disabled to interactive
   * without a reload — the same reasoning as `AccountLayout.links`.
   */
  private readonly disabledChannels = computed(() => {
    const flags = this.flags.all();
    return this.visibleChannels().filter((channel) => channel.flag && flags[channel.flag] !== true);
  });

  /**
   * The banner above the grid, or null when every visible channel is live.
   *
   * `availableChannels` only checks that a provider exists on this deployment, not whether an
   * operator has switched it on — so a channel can be listed, tickable and silently not sent. This
   * says so, instead of leaving the customer to find out when nothing arrives.
   */
  protected readonly comingSoonMessage = computed(() => {
    const labels = this.disabledChannels().map((channel) => channel.label);
    if (labels.length === 0) return null;
    return `${joinNaturally(labels)} notifications are coming soon. You can set your preferences now and they will apply once these are switched on.`;
  });

  constructor() {
    this.load();
  }

  protected load(): void {
    this.error.set(false);
    this.store.load().subscribe({ error: () => this.error.set(true) });
  }

  protected isChannelDisabled(key: 'email' | 'sms' | 'whatsApp' | 'inApp'): boolean {
    return this.disabledChannels().some((channel) => channel.key === key);
  }

  protected isOn(category: PreferenceResponse, key: 'email' | 'sms' | 'whatsApp' | 'inApp'): boolean {
    return category[key];
  }

  protected toggle(
    category: PreferenceResponse,
    key: 'email' | 'sms' | 'whatsApp' | 'inApp',
    value: boolean,
  ): void {
    // A channel this deployment has not switched on yet can be seen but not changed — the checkbox
    // shows the server's own value (never forced unchecked) so a preference set ahead of time is
    // not lost, but a click on it must not reach the API.
    if (this.isChannelDisabled(key)) return;

    // The whole row is sent because the endpoint replaces it. The other three channels come off the
    // row as it stands, so a save never carries a stale copy of a value somebody changed a moment ago.
    this.store
      .save({
        category: category.category,
        email: key === 'email' ? value : category.email,
        sms: key === 'sms' ? value : category.sms,
        whatsApp: key === 'whatsApp' ? value : category.whatsApp,
        inApp: key === 'inApp' ? value : category.inApp,
      })
      .subscribe({
        next: () => this.toasts.success('Preferences saved.'),
        error: (error: unknown) =>
          this.toasts.warning(describeError(error, 'We could not save that preference. Please try again.')),
      });
  }
}

/** "Email", "Email and SMS", "Email, SMS and WhatsApp" — never an Oxford comma before the last item. */
function joinNaturally(items: readonly string[]): string {
  if (items.length <= 1) return items[0] ?? '';
  if (items.length === 2) return `${items[0]} and ${items[1]}`;
  return `${items.slice(0, -1).join(', ')} and ${items[items.length - 1]}`;
}
