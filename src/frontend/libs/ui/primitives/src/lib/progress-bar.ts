import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * The indeterminate bar at the top of the page while requests are in flight.
 *
 * Deliberately indeterminate: the client cannot know how far along a request is, and a fake
 * progress bar that jumps to ninety per cent and waits is a worse lie than an honest sliver of
 * movement. Under `prefers-reduced-motion` it stops moving and stays visible — the information is
 * "something is happening", and that must survive the animation being switched off.
 */
@Component({
  selector: 'kh-progress-bar',
  template: '',
  styles: `
    :host {
      position: fixed;
      inset-block-start: 0;
      inset-inline: 0;
      z-index: var(--z-header);
      display: block;
      height: 2px;
      background: var(--color-primary-subtle);
      overflow: hidden;
    }

    :host::after {
      content: '';
      display: block;
      width: 40%;
      height: 100%;
      background: var(--color-primary);
      animation: kh-progress-slide 1.1s var(--ease-standard) infinite;
    }

    @keyframes kh-progress-slide {
      from {
        transform: translateX(-100%);
      }
      to {
        transform: translateX(350%);
      }
    }

    @media (prefers-reduced-motion: reduce) {
      :host::after {
        width: 100%;
        animation: none;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    role: 'progressbar',
    'aria-busy': 'true',
    '[attr.aria-label]': 'label()',
  },
})
export class ProgressBar {
  readonly label = input('Loading');
}
