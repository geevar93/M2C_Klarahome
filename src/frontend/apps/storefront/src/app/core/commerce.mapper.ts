import { Injectable, inject } from '@angular/core';
import { AddressResponse } from '@klarahome/data-access-account';
import {
  CartLineResponse,
  CartResponse,
  CartVendorGroupResponse,
  QuoteResult,
} from '@klarahome/data-access-cart';
import {
  CheckoutAddressResponse,
  PaymentMethodResponse,
  VendorShippingOptionsResponse,
} from '@klarahome/data-access-checkout';
import {
  OrderEventResponse,
  OrderResponse,
  OrderSummaryResponse,
  ReturnReasonOption,
  ReturnableLineResponse,
  SubOrderResponse,
} from '@klarahome/data-access-orders';
import { WalletTransactionResponse } from '@klarahome/data-access-account';
import { INR, Money, money } from '@klarahome/domain';
import {
  AddressView,
  CartGroupView,
  CartLineView,
  OrderCardView,
  OrderSummaryView,
  PaymentMethodView,
  ReturnReasonView,
  ReturnableLineView,
  ShippingGroupView,
  StatusTone,
  SummaryRowView,
  TimelineEntryView,
  WalletEntryView,
} from '@klarahome/ui-patterns';
import { ImageUrls } from '@klarahome/util';

/**
 * The buying and account contracts, in the shapes the presentation layer draws.
 *
 * The counterpart of `catalog.mapper.ts`, and it holds to the same three rules: money arrives as a
 * number and leaves as `Money`, an image is resolved here because whether a file id can become a
 * URL is a deployment question, and a `null` stays `null`.
 *
 * Two things are decided here rather than in a component, and both are decisions rather than
 * translations — which is why they are in one file instead of eight templates:
 *
 *  - **Which rows a price summary has.** They come from the quote, and a row exists only when the
 *    quote has a non-zero figure for it. A basket with no COD fee has no COD row rather than a row
 *    reading ₹0, and no component gets to decide that a missing figure means zero.
 *  - **What an order status looks like.** Whether `PartiallyCancelled` reads as a warning is a
 *    judgement about a state machine (Step 14), made once, here.
 */
@Injectable({ providedIn: 'root' })
export class CommerceMapper {
  private readonly images = inject(ImageUrls);

  // ---- Cart ------------------------------------------------------------------------------------

  cartLine(line: CartLineResponse, currency: string, hrefBySku?: ReadonlyMap<string, string>): CartLineView {
    return {
      id: line.id,
      listingId: line.listingId,
      name: line.name,
      // The cart projection carries no slug — it is denormalised for speed and does not join the
      // catalogue — so a line links back to the product only when the caller could resolve one.
      href: hrefBySku?.get(line.sku) ?? null,
      sku: line.sku,
      sellerName: line.vendorName,
      image: this.images.sourceForFile(line.imageFileId, line.name),
      quantity: line.quantity,
      unitPrice: money(line.unitPrice, currency),
      unitMrp: line.unitMrp > line.unitPrice ? money(line.unitMrp, currency) : null,
      lineTotal: money(line.lineTotal, currency),
      quantityAvailable: line.quantityAvailable,
      isCodAllowed: line.isCodAllowed,
      savedForLater: line.savedForLater,
      issues: line.issues.map((issue) => ({
        code: issue.code,
        message: issue.message,
        isBlocking: issue.isBlocking,
      })),
    };
  }

  cartGroup(group: CartVendorGroupResponse, currency: string): CartGroupView {
    return {
      vendorId: group.vendorId,
      sellerName: group.vendorName,
      lineIds: group.lineIds,
      total: money(group.total, currency),
      dispatchHours: group.dispatchSlaHours,
    };
  }

  /**
   * The price panel, from a quote.
   *
   * Every row is conditional on the quote having a figure for it, and the labels say what the money
   * actually is: "Taxes (GST)" rather than "Tax", because an Indian customer reads a price that is
   * already inclusive and needs to see that the line is a breakdown, not an addition.
   */
  summaryFromQuote(quote: QuoteResult, options: { totalLabel?: string } = {}): OrderSummaryView {
    const currency = quote.currencyCode || INR;
    const rows: SummaryRowView[] = [{ label: `Items`, amount: money(quote.subtotal, currency) }];

    if (quote.discountTotal > 0) {
      rows.push({ label: 'Discount', amount: money(quote.discountTotal, currency), isDiscount: true });
    }

    // Shipping is always shown, including when it is zero, because "free delivery" is information a
    // customer wants and a missing line is not. It is the one exception to the rule above.
    rows.push({
      label: 'Delivery',
      amount: money(quote.shipping, currency),
      note: quote.shipping === 0 ? 'Free on this order' : null,
    });

    if (quote.codFee > 0) {
      rows.push({ label: 'Cash on delivery fee', amount: money(quote.codFee, currency) });
    }

    if (quote.taxTotal > 0) {
      rows.push({
        label: 'Taxes (GST)',
        amount: money(quote.taxTotal, currency),
        note: 'Included in the item prices above',
      });
    }

    if (quote.roundingAdjustment !== 0) {
      rows.push({ label: 'Rounding', amount: money(quote.roundingAdjustment, currency) });
    }

    return {
      rows,
      total: money(quote.grandTotal, currency),
      totalLabel: options.totalLabel ?? 'Total',
      walletApplied: quote.walletApplied > 0 ? money(quote.walletApplied, currency) : null,
      // Shown only when store credit made the two differ; otherwise it repeats the total under a
      // second name, which is how a customer starts wondering which one they are charged.
      amountPayable:
        quote.walletApplied > 0 && quote.amountPayable !== quote.grandTotal
          ? money(quote.amountPayable, currency)
          : null,
      savings: quote.discountTotal > 0 ? money(quote.discountTotal, currency) : null,
    };
  }

  /** The price panel of an order, which carries its own frozen totals rather than a live quote. */
  summaryFromOrder(order: OrderResponse): OrderSummaryView {
    const currency = order.currencyCode || INR;
    const rows: SummaryRowView[] = [{ label: 'Items', amount: money(order.itemsTotal, currency) }];

    if (order.discountTotal > 0) {
      rows.push({ label: 'Discount', amount: money(order.discountTotal, currency), isDiscount: true });
    }

    rows.push({
      label: 'Delivery',
      amount: money(order.shippingTotal, currency),
      note: order.shippingTotal === 0 ? 'Free on this order' : null,
    });

    if (order.codFee > 0) rows.push({ label: 'Cash on delivery fee', amount: money(order.codFee, currency) });
    if (order.taxTotal > 0) {
      rows.push({
        label: 'Taxes (GST)',
        amount: money(order.taxTotal, currency),
        note: 'Included in the item prices above',
      });
    }
    if (order.roundingAdjustment !== 0) {
      rows.push({ label: 'Rounding', amount: money(order.roundingAdjustment, currency) });
    }

    // A cancellation is shown as its own row rather than by quietly reducing the total: the customer
    // agreed to `grandTotal`, and what changed after that is a fact they are entitled to see.
    if (order.cancelledTotal > 0) {
      rows.push({ label: 'Cancelled', amount: money(order.cancelledTotal, currency), isDiscount: true });
    }

    return {
      rows,
      total: money(order.cancelledTotal > 0 ? order.netTotal : order.grandTotal, currency),
      totalLabel: order.cancelledTotal > 0 ? 'Net total' : 'Total',
      walletApplied: order.walletApplied > 0 ? money(order.walletApplied, currency) : null,
      amountPayable: null,
      savings: order.discountTotal > 0 ? money(order.discountTotal, currency) : null,
    };
  }

  // ---- Addresses -------------------------------------------------------------------------------

  /**
   * A saved address.
   *
   * `lines` is assembled here — the order an Indian address is written in is decided once, in this
   * method, and the raw fields travel alongside it for the form to edit. `stateName` is not on the
   * response, so the caller passes it in from the reference data it already holds.
   */
  address(address: AddressResponse, stateName?: string): AddressView {
    const locality = [address.city, stateName, address.pincode].filter(Boolean).join(', ');

    return {
      id: address.id,
      label: address.label,
      recipientName: address.recipientName,
      mobile: address.mobile,
      lines: [address.line1, address.line2 ?? '', address.landmark ?? '', locality].filter(
        (line) => line.trim().length > 0,
      ),
      line1: address.line1,
      line2: address.line2,
      landmark: address.landmark,
      city: address.city,
      stateId: address.stateId,
      pincode: address.pincode,
      gstin: address.gstin,
      type: address.type,
      isDefaultShipping: address.isDefaultShipping,
      isDefaultBilling: address.isDefaultBilling,
    };
  }

  /**
   * The address frozen onto a checkout session or an order.
   *
   * It has no id of its own — it is a copy taken at the moment of purchase, which is the point of
   * it: editing the address book afterwards must not change where a parcel was sent. The `id` here
   * is the address it was copied from, so a page can highlight the right card.
   */
  checkoutAddress(address: CheckoutAddressResponse, stateName?: string): AddressView {
    const locality = [address.city, stateName, address.pincode].filter(Boolean).join(', ');

    return {
      id: address.sourceAddressId,
      label: null,
      recipientName: address.recipientName,
      mobile: address.mobile,
      lines: [address.line1, address.line2 ?? '', address.landmark ?? '', locality].filter(
        (line) => line.trim().length > 0,
      ),
      line1: address.line1,
      line2: address.line2,
      landmark: address.landmark,
      city: address.city,
      stateId: address.stateId,
      pincode: address.pincode,
      gstin: address.gstin,
      type: 'Home',
      isDefaultShipping: false,
      isDefaultBilling: false,
    };
  }

  // ---- Checkout --------------------------------------------------------------------------------

  paymentMethod(method: PaymentMethodResponse, currency: string): PaymentMethodView {
    return {
      method: method.method,
      name: method.name,
      isAvailable: method.isAvailable,
      fee: method.fee > 0 ? money(method.fee, currency) : null,
      reason: method.reason,
    };
  }

  shippingGroup(group: VendorShippingOptionsResponse, currency: string): ShippingGroupView {
    return {
      vendorId: group.vendorId,
      sellerName: group.vendorName,
      selectedCode: group.selectedCode,
      options: group.options.map((option) => ({
        code: option.code,
        name: option.name,
        carrier: option.carrier,
        amount: money(option.amount, currency),
        promisedMinDays: option.promisedMinDays,
        promisedMaxDays: option.promisedMaxDays,
        isCodAvailable: option.isCodAvailable,
      })),
    };
  }

  // ---- Orders ----------------------------------------------------------------------------------

  orderCard(order: OrderSummaryResponse): OrderCardView {
    const currency = order.currencyCode || INR;
    return {
      id: order.id,
      orderNumber: order.orderNumber,
      placedAt: order.placedAt,
      status: order.status,
      statusLabel: humanise(order.status),
      statusTone: statusTone(order.status),
      paymentLabel: paymentLabel(order.paymentMethod, order.paymentStatus),
      total: money(order.netTotal || order.grandTotal, currency),
      itemCount: order.itemCount,
      sellerCount: order.vendorCount,
      // The summary row carries no images. The list stays a list rather than fetching a product per
      // order to decorate it; the detail page is where the pictures are.
      images: [],
    };
  }

  /**
   * The customer's view of an order's history.
   *
   * Everything not marked customer-visible is dropped here, at the boundary, so no internal note
   * is one CSS rule away from a shopper. The most recent entry is the current one — the timeline is
   * append-only and in order (Step 14), so "current" is "last" rather than a status lookup.
   */
  timeline(events: readonly OrderEventResponse[]): readonly TimelineEntryView[] {
    const visible = events.filter((event) => event.isCustomerVisible);

    return visible.map((event, index) => ({
      id: event.id,
      label: event.toStatus ? humanise(event.toStatus) : humanise(event.type),
      detail: event.message,
      occurredAt: event.occurredAt,
      isCurrent: index === visible.length - 1,
    }));
  }

  /**
   * "Cash on delivery" or "Paid online" — the two payment facts as one line.
   *
   * Exposed as well as used by `orderCard`, because the order *detail* page has the same two fields
   * and no summary row to read them off.
   */
  paymentLine(method: string, status: string): string {
    return paymentLabel(method, status);
  }

  /**
   * A status, as a badge draws it.
   *
   * The one place the state machine's vocabulary becomes a label and a colour. Called with a raw
   * status by the pages that only have one, and with a sub-order by those that have the whole thing.
   */
  status(status: string): { label: string; tone: StatusTone } {
    return { label: humanise(status), tone: statusTone(status) };
  }

  /** A sub-order's status, for the badge on each seller's part of an order. */
  subOrderStatus(subOrder: SubOrderResponse): { label: string; tone: StatusTone } {
    return this.status(subOrder.status);
  }

  // ---- Returns ---------------------------------------------------------------------------------

  returnableLine(line: ReturnableLineResponse, currency: string): ReturnableLineView {
    return {
      orderLineId: line.orderLineId,
      name: line.name,
      sku: line.sku,
      image: this.images.sourceForFile(line.imageFileId, line.name),
      quantityReturnable: line.quantityReturnable,
      estimatedRefund: money(line.estimatedRefund, currency),
      isReturnable: line.isReturnable,
      reason: line.reason,
    };
  }

  returnReason(reason: ReturnReasonOption): ReturnReasonView {
    return {
      code: reason.code,
      label: reason.label,
      description: reason.description,
      requiresEvidence: reason.requiresEvidence,
      allowsReplacement: reason.allowsReplacement,
    };
  }

  // ---- Wallet ----------------------------------------------------------------------------------

  walletEntry(entry: WalletTransactionResponse, currency: string): WalletEntryView {
    // The ledger's own vocabulary: a `Credit` adds, everything else takes away. Read off the type
    // rather than off the sign of the amount, which is always positive on the wire.
    const isCredit = entry.type === 'Credit';

    return {
      id: entry.id,
      reason: humanise(entry.reason),
      note: entry.note,
      amount: money(entry.amount, currency),
      isCredit,
      balanceAfter: money(entry.balanceAfter, currency),
      occurredAt: entry.occurredAt,
      expiresAt: entry.expiresAt,
    };
  }
}

/**
 * How each order status is drawn.
 *
 * A judgement about the state machine, made once. Anything not listed is neutral — a status this
 * table has not heard of is more likely to be new than to be a failure, and colouring an unknown
 * state red is how a routine addition to the backend becomes a support call.
 */
function statusTone(status: string): StatusTone {
  switch (status) {
    case 'Delivered':
    case 'Completed':
      return 'success';
    case 'Cancelled':
    case 'Failed':
    case 'Rejected':
      return 'danger';
    case 'PartiallyCancelled':
    case 'OnHold':
    case 'PaymentPending':
      return 'warning';
    case 'Shipped':
    case 'OutForDelivery':
    case 'Packed':
    case 'Confirmed':
      return 'info';
    default:
      return 'neutral';
  }
}

/** "PartiallyCancelled" as "Partially cancelled" — the API's `PascalCase`, made readable. */
function humanise(value: string): string {
  if (!value) return '';
  const spaced = value.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/_/g, ' ');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

/**
 * Whether a payment method is cash on delivery.
 *
 * The Orders API names the method `CashOnDelivery` (its `OrderPaymentMethod` enum) while the
 * checkout's payment-method list says `COD`, and this is the one place that knows both — every
 * screen used to compare against `COD` alone, so a cash order read as prepaid, offered "Pay now",
 * and the API refused it with "there is nothing to pay for this order".
 */
export function isCashOnDelivery(method: string | null | undefined): boolean {
  return method === 'COD' || method === 'CashOnDelivery';
}

/**
 * "Cash on delivery" or "Paid online" — the two facts as one line.
 *
 * The method alone is not enough: a prepaid order that has not been paid for is the case the
 * customer most needs to see, because it is the one they can do something about.
 */
function paymentLabel(method: string, status: string): string {
  const paid = status === 'Paid' || status === 'Captured';
  if (isCashOnDelivery(method)) return paid ? 'Paid on delivery' : 'Cash on delivery';
  return paid ? 'Paid online' : `Payment ${humanise(status).toLowerCase()}`;
}
