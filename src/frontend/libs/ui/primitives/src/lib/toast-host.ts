import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ToastService } from '@klarahome/util';

import { Button } from './button';
import { Icon } from './icon';

/**
 * Renders the toast queue.
 *
 * The queue itself is a service in `util` — the interceptor chain needed somewhere to put a
 * failure long before there was anything to render it — and this is the one component that reads
 * it. There is exactly one host, mounted by the shell.
 *
 * `aria-live="polite"` on the container rather than `role="alert"` on each toast: the region has
 * to exist *before* the message is written into it, or assistive technology has nothing to
 * observe and announces nothing at all. `polite` because a toast is never the only place an
 * outcome appears — the cart badge changed too — and interrupting a screen-reader user to repeat
 * something they can already reach is a worse experience than waiting for a pause.
 */
@Component({
  selector: 'kh-toast-host',
  imports: [Button, Icon],
  template: `
    <div class="region" role="status" aria-live="polite" aria-atomic="false">
      @for (toast of items(); track toast.id) {
        <div class="toast" [attr.data-tone]="toast.tone">
          <div class="body">
            @if (toast.title) {
              <p class="title">{{ toast.title }}</p>
            }
            <p class="message">{{ toast.message }}</p>
            @if (toast.correlationId) {
              <!-- The support handle. Nobody reads it until they have to, and then it is the only
                   thing that connects what they saw to the server's log lines. -->
              <p class="reference">Reference: {{ toast.correlationId }}</p>
            }
          </div>
          @if (toast.action; as action) {
            <button khButton variant="tertiary" size="sm" (click)="runAction(action, toast.id)">
              {{ action.label }}
            </button>
          }
          <button
            khButton
            variant="tertiary"
            size="sm"
            [iconOnly]="true"
            attr.aria-label="Dismiss: {{ toast.message }}"
            (click)="dismiss(toast.id)"
          >
            <kh-icon name="close" size="sm" />
          </button>
        </div>
      }
    </div>
  `,
  styles: `
    .region {
      position: fixed;
      inset-inline: var(--space-4);
      /* Above the sticky bar, which is where a phone's thumb already is. */
      inset-block-end: calc(var(--bottom-bar-height) + var(--space-4));
      z-index: var(--z-toast);
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      pointer-events: none;
    }

    .toast {
      display: flex;
      align-items: flex-start;
      gap: var(--space-2);
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-inline-start: var(--space-1) solid var(--color-border-strong);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-md);
      font-size: var(--text-sm);
      pointer-events: auto;
    }

    .toast[data-tone='success'] {
      border-inline-start-color: var(--color-success);
    }

    .toast[data-tone='warning'] {
      border-inline-start-color: var(--color-warning);
    }

    .toast[data-tone='danger'] {
      border-inline-start-color: var(--color-danger);
    }

    .toast[data-tone='info'] {
      border-inline-start-color: var(--color-info);
    }

    .body {
      flex: 1;
      min-width: 0;
    }

    .title {
      margin: 0 0 var(--space-1);
      font-weight: var(--weight-medium);
    }

    .message {
      margin: 0;
    }

    .reference {
      margin: var(--space-1) 0 0;
      color: var(--color-text-muted);
      font-family: var(--font-mono);
      font-size: var(--text-xs);
      word-break: break-all;
    }

    /* 480px is the \`sm\` breakpoint from _breakpoints.scss. Restated rather than imported because
       a component's inline styles are compiled without the workspace include paths under Jest. */
    @media (min-width: 480px) {
      .region {
        inset-inline-start: auto;
        max-width: 26rem;
      }
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ToastHost {
  private readonly service = inject(ToastService);

  protected readonly items = this.service.toasts;

  protected dismiss(id: string): void {
    this.service.dismiss(id);
  }

  /** Runs the toast's single action and takes the toast away — the outcome is the dismissal. */
  protected runAction(action: { run: () => void }, id: string): void {
    action.run();
    this.service.dismiss(id);
  }
}
