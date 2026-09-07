import { DOCUMENT, isPlatformBrowser } from '@angular/common';
import { Injectable, PLATFORM_ID, Signal, inject, signal } from '@angular/core';

/**
 * Whether the browser thinks it can reach the network.
 *
 * `navigator.onLine` is a weak signal — it says the device has *a* connection, not that our API
 * answers — so it is used for exactly one thing: telling somebody on a train that the blank page
 * is the tunnel and not the shop. A failed request is still reported by the interceptor chain,
 * which is the honest source of "this did not work".
 *
 * On the server it is always online. There is no other truthful answer, and rendering an offline
 * banner into SSR output would put it in the HTML a crawler reads.
 */
@Injectable({ providedIn: 'root' })
export class NetworkStatus {
  private readonly isBrowser = isPlatformBrowser(inject(PLATFORM_ID));
  private readonly document = inject(DOCUMENT);
  private readonly state = signal(true);

  /** True while the browser believes it has a connection. */
  readonly online: Signal<boolean> = this.state.asReadonly();

  constructor() {
    if (!this.isBrowser) return;

    const view = this.document.defaultView;
    if (!view) return;

    this.state.set(view.navigator?.onLine ?? true);
    // Registered for the life of the application: the service is a root singleton, and removing
    // these would mean the app had stopped caring whether it is connected.
    view.addEventListener('online', () => this.state.set(true));
    view.addEventListener('offline', () => this.state.set(false));
  }
}
