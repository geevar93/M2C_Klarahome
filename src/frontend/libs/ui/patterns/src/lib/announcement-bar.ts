import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icon } from '@klarahome/ui-primitives';
import { BrowserStorage } from '@klarahome/util';

import { BannerView } from './banner.model';

/** Where a dismissal is remembered, keyed by the banner so a new one is not pre-dismissed. */
const DISMISSED_KEY = 'kh.announcement.dismissed';

/**
 * The strip above the header.
 *
 * **One banner, not a carousel.** The placement can hold several and the API returns them in
 * priority order; this renders the first. A rotating announcement is a line of text that changes
 * while somebody is reading it, and the merchandising answer to "we have two things to say" is to
 * say the more important one.
 *
 * **Dismissal is per banner and per browser.** The id is what is remembered, so tomorrow's sale
 * announcement is not pre-dismissed by yesterday's delivery notice — which is what a single
 * "hidden" flag would do, and is how an announcement bar becomes a feature nobody sees twice.
 * `localStorage` and not the account: it is a preference about a strip of text, and reading it must
 * not wait for a session.
 *
 * It renders nothing at all when there is no banner, rather than an empty bar — an empty strip is a
 * layout shift on every page load for a feature that is off.
 */
@Component({
  selector: 'kh-announcement-bar',
  imports: [Icon, RouterLink],
  template: `
    @if (current(); as banner) {
      <div class="bar" role="region" aria-label="Announcement">
        @if (banner.link) {
          <a [routerLink]="banner.link">{{ banner.message }}</a>
        } @else {
          <span>{{ banner.message }}</span>
        }

        <button
          type="button"
          class="dismiss"
          aria-label="Dismiss this announcement"
          (click)="dismiss(banner.id)"
        >
          <kh-icon name="close" size="sm" />
        </button>
      </div>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .bar {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      justify-content: center;
      padding: var(--space-2) var(--space-4);
      background: var(--color-accent-surface);
      color: var(--color-accent-text);
      font-size: var(--text-sm);
      text-align: center;
    }

    a {
      color: inherit;
    }

    .dismiss {
      display: inline-flex;
      padding: var(--space-1);
      border: none;
      border-radius: var(--radius-sm);
      background: none;
      color: inherit;
      cursor: pointer;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnnouncementBar {
  private readonly storage = inject(BrowserStorage);

  /** The banners for the announcement placement, in priority order. */
  readonly banners = input<readonly BannerView[]>([]);

  private readonly dismissed = signal(this.storage.getJson<string | null>(DISMISSED_KEY, null));

  /** The one to show: the first with a message that has not been dismissed. */
  protected readonly current = computed(() => {
    const dismissed = this.dismissed();

    return this.banners().find((banner) => banner.message && banner.id !== dismissed) ?? null;
  });

  protected dismiss(id: string): void {
    this.dismissed.set(id);
    this.storage.setJson(DISMISSED_KEY, id);
  }
}
