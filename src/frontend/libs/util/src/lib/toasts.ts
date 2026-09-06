import { Injectable, Signal, signal } from '@angular/core';

import { newUuid } from './ids';

export type ToastTone = 'info' | 'success' | 'warning' | 'danger';

export interface Toast {
  readonly id: string;
  readonly tone: ToastTone;
  readonly message: string;
  /** Optional heading. Most toasts do not need one. */
  readonly title?: string;
  /** A single action, because a toast the user has to read twice is not a toast. */
  readonly action?: { readonly label: string; readonly run: () => void };
  /** Milliseconds before it dismisses itself; 0 means it stays until dismissed. */
  readonly durationMs: number;
  /** The correlation id, when the toast came from a failed request. Shown in the detail line. */
  readonly correlationId?: string;
}

/**
 * The notification queue.
 *
 * State only — the visual component is Step 23's, and the interceptor chain needs somewhere to
 * put a failure long before there is anything to render it. Announcing them is the renderer's
 * job: the container is an `aria-live="polite"` region, which is why a toast is never the only
 * place an outcome appears.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private readonly queue = signal<readonly Toast[]>([]);
  private readonly timers = new Map<string, ReturnType<typeof setTimeout>>();

  readonly toasts: Signal<readonly Toast[]> = this.queue.asReadonly();

  show(toast: Omit<Toast, 'id' | 'durationMs'> & { durationMs?: number }): string {
    const id = newUuid();
    // A failure stays until it is read. A confirmation does not need to be.
    const durationMs = toast.durationMs ?? (toast.tone === 'danger' ? 0 : 5000);
    this.queue.update((current) => [...current, { ...toast, id, durationMs }]);

    if (durationMs > 0) {
      this.timers.set(
        id,
        setTimeout(() => this.dismiss(id), durationMs),
      );
    }
    return id;
  }

  info(message: string, title?: string): string {
    return this.show({ tone: 'info', message, title });
  }

  success(message: string, title?: string): string {
    return this.show({ tone: 'success', message, title });
  }

  warning(message: string, title?: string): string {
    return this.show({ tone: 'warning', message, title });
  }

  danger(message: string, title?: string, correlationId?: string): string {
    return this.show({ tone: 'danger', message, title, correlationId });
  }

  dismiss(id: string): void {
    const timer = this.timers.get(id);
    if (timer) {
      clearTimeout(timer);
      this.timers.delete(id);
    }
    this.queue.update((current) => current.filter((toast) => toast.id !== id));
  }

  clear(): void {
    for (const timer of this.timers.values()) clearTimeout(timer);
    this.timers.clear();
    this.queue.set([]);
  }
}
