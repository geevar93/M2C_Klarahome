import { NgTemplateOutlet } from '@angular/common';
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
 * **A marquee is the one motion allowed, and it is opt-in per banner.** An editor who has more to
 * say than a 360-pixel line holds can let the words scroll rather than wrap the strip to three
 * lines; the text is duplicated once so the loop has no gap, and the copy is `aria-hidden` so a
 * screen reader hears the message once. `prefers-reduced-motion` wins over the editor's choice —
 * the words stop and the strip behaves like any other announcement.
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
  imports: [Icon, NgTemplateOutlet, RouterLink],
  template: `
    @if (current(); as banner) {
      <div class="bar" [class.is-marquee]="banner.marquee" role="region" aria-label="Announcement">
        @if (banner.marquee) {
          <div class="track">
            <span class="run">
              <ng-container *ngTemplateOutlet="message; context: { banner }" />
            </span>
            <span class="run" aria-hidden="true">
              <ng-container *ngTemplateOutlet="message; context: { banner }" />
            </span>
          </div>
        } @else {
          <ng-container *ngTemplateOutlet="message; context: { banner }" />
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

    <ng-template #message let-banner="banner">
      @if (banner.link) {
        <a [routerLink]="banner.link">{{ banner.message }}</a>
      } @else {
        <span>{{ banner.message }}</span>
      }
    </ng-template>
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
      flex-shrink: 0;
      padding: var(--space-1);
      border: none;
      border-radius: var(--radius-sm);
      background: none;
      color: inherit;
      cursor: pointer;
    }

    /* The marquee: the track clips, the two copies sit side by side, and the pair slides left by
       exactly one copy's width before snapping back — which lands on the identical second copy,
       so the loop is seamless. Duration scales with the length of the text via the copy count
       rather than a per-banner setting: 20 s across the pair reads at roughly a comfortable pace
       for a 200-character message. */
    .is-marquee {
      justify-content: space-between;
      text-align: start;
    }

    .track {
      display: flex;
      flex: 1;
      min-inline-size: 0;
      overflow: hidden;
      white-space: nowrap;
    }

    .run {
      display: inline-block;
      flex-shrink: 0;
      padding-inline-end: var(--space-8);
      animation: kh-marquee 20s linear infinite;
    }

    .is-marquee:hover .run,
    .is-marquee:focus-within .run {
      animation-play-state: paused;
    }

    @keyframes kh-marquee {
      from {
        transform: translateX(0);
      }

      to {
        transform: translateX(-100%);
      }
    }

    @media (prefers-reduced-motion: reduce) {
      .run {
        animation: none;
      }

      .track {
        white-space: normal;
      }

      .run[aria-hidden='true'] {
        display: none;
      }
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
