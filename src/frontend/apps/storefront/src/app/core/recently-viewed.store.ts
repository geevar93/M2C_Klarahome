import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { ProductCardView } from '@klarahome/ui-patterns';
import { BrowserStorage } from '@klarahome/util';

/** Where the trail is kept, and how long it is. */
const STORAGE_KEY = 'kh.recently-viewed';
const LIMIT = 12;

/** What is stored — enough to draw a card, and nothing that can go stale dangerously. */
interface RecentEntry {
  readonly slug: string;
  readonly name: string;
  readonly imageFileId: string | null;
  readonly viewedAt: number;
}

/**
 * The products this visitor has looked at.
 *
 * **In the browser only, and never sent anywhere.** It is a convenience, not a profile: no
 * identifier, no server round trip, and `BrowserStorage` answers null during server rendering, so
 * an SSR document never carries one visitor's history into a page an edge cache might serve to
 * somebody else (Step 32).
 *
 * **Prices are deliberately not stored.** A price kept for a week and rendered from storage is a
 * price the shop is not offering, which is worse than no rail at all — so what is remembered is
 * the identity of the product, and the rail links to the PDP where the price is current. That is
 * also why the cards it produces are marked unpurchasable: nothing here may be added to a basket
 * without the product page having been read.
 */
@Injectable({ providedIn: 'root' })
export class RecentlyViewedStore {
  private readonly storage = inject(BrowserStorage);
  private readonly entries = signal<readonly RecentEntry[]>([]);

  /** The trail, most recent first, as product cards. */
  readonly items: Signal<readonly ProductCardView[]> = computed(() =>
    this.entries().map((entry) => ({
      variantId: entry.slug,
      productId: entry.slug,
      listingId: null,
      name: entry.name,
      href: `/p/${entry.slug}`,
      brand: null,
      price: { amount: 0, currency: 'INR' },
      mrp: null,
      image: null,
      rating: null,
      ratingCount: 0,
      isPurchasable: false,
      reference: entry.name,
    })),
  );

  /**
   * Read once, in the constructor.
   *
   * Not lazily on first use: the rails that render this are `computed`, and a lazy read that wrote
   * a signal on first access would be a write inside a computed — which Angular refuses. Empty
   * during server rendering, filled in the browser.
   */
  constructor() {
    this.entries.set(this.storage.getJson<RecentEntry[]>(STORAGE_KEY, []));
  }

  /**
   * Records a product view.
   *
   * The product moves to the front if it was already there rather than being added twice — a
   * shopper comparing two things goes back and forth, and a rail of the same product six times is
   * the result of the naive implementation.
   */
  record(slug: string, name: string, imageFileId: string | null): void {
    const next = [
      { slug, name, imageFileId, viewedAt: Date.now() },
      ...this.entries().filter((entry) => entry.slug !== slug),
    ].slice(0, LIMIT);

    this.entries.set(next);
    this.storage.setJson(STORAGE_KEY, next);
  }

  /** The trail without one product — what a PDP shows, which is everything but itself. */
  except(slug: string): readonly ProductCardView[] {
    return this.items().filter((item) => item.href !== `/p/${slug}`);
  }

  clear(): void {
    this.entries.set([]);
    this.storage.remove(STORAGE_KEY);
  }
}
