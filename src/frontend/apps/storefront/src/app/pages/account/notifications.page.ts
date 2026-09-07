import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NotificationPreferencesStore, PreferenceResponse } from '@klarahome/data-access-account';
import { Alert, Checkbox, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';

/** How each channel is labelled and which field of a preference row it is. */
const CHANNELS: readonly { readonly key: 'email' | 'sms' | 'whatsApp' | 'inApp'; readonly label: string }[] =
  [
    { key: 'email', label: 'Email' },
    { key: 'sms', label: 'SMS' },
    { key: 'whatsApp', label: 'WhatsApp' },
    { key: 'inApp', label: 'In the app' },
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
  imports: [Alert, Checkbox, Skeleton],
  template: `
    <h1>Notifications</h1>
    <p class="lead">
      Choose what we tell you about, and where. Some messages about an order you have placed are sent whatever
      you choose here — they are part of buying something.
    </p>

    @if (!store.hasLoaded()) {
      <kh-skeleton height="12rem" />
    } @else if (store.categories().length === 0) {
      <kh-alert tone="info">There is nothing to configure on this store yet.</kh-alert>
    } @else {
      <div class="grid">
        @for (category of store.categories(); track category.category) {
          <fieldset [class.busy]="store.savingCategory() === category.category">
            <legend>{{ category.category }}</legend>
            <p class="description">{{ category.description }}</p>

            <div class="channels">
              @for (channel of channelsFor(); track channel.key) {
                <kh-checkbox
                  [bare]="true"
                  [label]="channel.label"
                  [inputId]="category.category + '-' + channel.key"
                  [checked]="isOn(category, channel.key)"
                  [disabled]="store.savingCategory() !== null"
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

    h1 {
      font-size: var(--text-2xl);
    }

    .lead {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
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

    legend {
      padding: 0;
      font-size: var(--text-base);
      font-weight: var(--weight-medium);
    }

    .description {
      margin: 0 0 var(--space-2);
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .channels {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountNotificationsPage {
  protected readonly store = inject(NotificationPreferencesStore);
  private readonly toasts = inject(ToastService);

  constructor() {
    this.store.loadOnce();
  }

  /** The columns this deployment can actually send on. */
  protected channelsFor(): readonly { key: 'email' | 'sms' | 'whatsApp' | 'inApp'; label: string }[] {
    const available = this.store.channels().map((channel) => channel.toLowerCase());
    return CHANNELS.filter((channel) => available.includes(channel.key.toLowerCase()));
  }

  protected isOn(category: PreferenceResponse, key: 'email' | 'sms' | 'whatsApp' | 'inApp'): boolean {
    return category[key];
  }

  protected toggle(
    category: PreferenceResponse,
    key: 'email' | 'sms' | 'whatsApp' | 'inApp',
    value: boolean,
  ): void {
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
        error: (error: unknown) =>
          this.toasts.warning(describeError(error, 'We could not save that preference. Please try again.')),
      });
  }
}
