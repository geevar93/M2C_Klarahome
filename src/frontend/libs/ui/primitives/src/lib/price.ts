import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Money, discountPercent } from '@klarahome/domain';
import { MoneyPipe } from '@klarahome/i18n';

/**
 * A selling price, its MRP and what that saves — the three figures Indian retail law and Indian
 * shoppers both expect together.
 *
 * The MRP is struck through and marked up as `<s>`, not styled with `text-decoration` on a span:
 * a screen reader announces a `<s>` as "deleted", which is the only way the strike-through is
 * conveyed to somebody who cannot see it. Without that, the two prices are read as one after the
 * other and the listing appears to state two different prices for the same thing.
 *
 * **The percentage is computed, not sent.** It is the one derivation the client is allowed
 * (`discountPercent` rounds down, so the badge never overstates the saving), and computing it
 * here means the card and the PDP cannot disagree by a percentage point.
 */
@Component({
  selector: 'kh-price',
  imports: [MoneyPipe],
  template: `
    <span class="now">{{ price() | khMoney }}</span>

    @if (showMrp()) {
      <s class="mrp">{{ mrp() | khMoney }}</s>
      <span class="off">{{ percentOff() }}% off</span>
    }

    @if (taxNote()) {
      <span class="tax">{{ taxNote() }}</span>
    }
  `,
  styles: `
    :host {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: var(--space-1) var(--space-2);
    }

    .now {
      font-size: var(--kh-price-size, var(--text-lg));
      font-weight: var(--weight-bold);
      color: var(--color-text);
    }

    .mrp {
      font-size: var(--text-sm);
      color: var(--color-text-muted);
    }

    .off {
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
      color: var(--color-success);
    }

    .tax {
      flex-basis: 100%;
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[style.--kh-price-size]': 'sizeToken()' },
})
export class Price {
  readonly price = input.required<Money>();
  /** The maximum retail price. Shown only when it is genuinely above the selling price. */
  readonly mrp = input<Money | null>(null);
  readonly size = input<'sm' | 'md' | 'lg'>('md');
  /** "Inclusive of all taxes" on a PDP; absent on a card, where it would be forty repetitions. */
  readonly taxNote = input<string | null>(null);

  protected readonly percentOff = computed(() => {
    const mrp = this.mrp();
    return mrp ? discountPercent(mrp, this.price()) : 0;
  });

  /**
   * An MRP equal to (or below) the selling price is not a discount, and rendering "0% off" beside
   * a struck-through identical number is how a storefront looks like it is lying.
   */
  protected readonly showMrp = computed(() => this.mrp() !== null && this.percentOff() > 0);

  protected readonly sizeToken = computed(() => {
    switch (this.size()) {
      case 'sm':
        return 'var(--text-base)';
      case 'lg':
        return 'var(--text-2xl)';
      default:
        return 'var(--text-lg)';
    }
  });
}
