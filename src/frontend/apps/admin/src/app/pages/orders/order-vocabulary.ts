/**
 * How order, seller's-share, payment, return and parcel statuses read on screen.
 *
 * The same status must say the same plain thing wherever it shows up: a filter dropdown and a
 * badge that use different words for one value are a bug, not a style choice. Every page under
 * `orders/`, `returns/` and `fulfilment/` reads its status wording from here.
 *
 * **The keys are the API's enum names, exactly** — `OrderStatus`, `SubOrderStatus`,
 * `OrderPaymentStatus`, `ReturnStatus` and `ShipmentStatus` in the backend. A filter option is
 * sent back as its key and the API parses it with `Enum.TryParse`, which *ignores* a value it does
 * not know: a key that drifts from the enum does not fail, it silently stops filtering. The
 * vocabulary spec pins the key lists to the enums for that reason.
 *
 * Order, seller's share and payment each have a status meaning "not paid yet", so they are kept in
 * separate maps and none of them renders as a bare, ambiguous "Pending".
 */
export interface StatusDisplay {
  readonly label: string;
  readonly tooltip: string;
}

/** `OrderStatus` — the whole order's status, derived from its sellers' shares. */
export const ORDER_STATUS_VOCAB: Record<string, StatusDisplay> = {
  PendingPayment: {
    label: 'Awaiting payment',
    tooltip: 'The customer has placed the order but the money has not come through yet.',
  },
  InProgress: {
    label: 'In progress',
    tooltip: 'Paid or cash on delivery, and at least one seller still has work to do on it.',
  },
  Completed: {
    label: 'Completed',
    tooltip: 'Every item has been delivered, returned or cancelled. Nothing is left to do.',
  },
  Cancelled: {
    label: 'Cancelled',
    tooltip: 'The whole order was cancelled.',
  },
};

/** `SubOrderStatus` — one seller's share of an order, which is what actually gets packed and shipped. */
export const SUB_ORDER_STATUS_VOCAB: Record<string, StatusDisplay> = {
  PendingPayment: {
    label: 'Awaiting payment',
    tooltip: 'Waiting for the customer’s payment. Nothing to pack yet.',
  },
  PaymentFailed: {
    label: 'Payment failed',
    tooltip: 'The payment was declined or abandoned. This will not be shipped.',
  },
  Confirmed: {
    label: 'New — to accept',
    tooltip: 'Paid or cash on delivery. The seller needs to accept it and start packing.',
  },
  Processing: {
    label: 'Being prepared',
    tooltip: 'The seller has accepted it and is picking the items.',
  },
  Packed: {
    label: 'Packed',
    tooltip: 'Packed and waiting for the courier to collect it.',
  },
  Shipped: {
    label: 'Shipped',
    tooltip: 'Handed to the courier, with a tracking number.',
  },
  OutForDelivery: {
    label: 'Out for delivery',
    tooltip: 'With the delivery agent today.',
  },
  DeliveryFailed: {
    label: 'Delivery failed',
    tooltip: 'The courier could not deliver it. Decide what to do under Delivery problems.',
  },
  RtoInitiated: {
    label: 'Returning to seller',
    tooltip: 'Delivery was given up and the courier is bringing it back to the seller.',
  },
  RtoDelivered: {
    label: 'Returned to seller',
    tooltip: 'The undelivered parcel is back with the seller.',
  },
  Delivered: {
    label: 'Delivered',
    tooltip: 'The customer has it.',
  },
  ReturnRequested: {
    label: 'Return requested',
    tooltip: 'The customer has asked to send something back. Decide under Returns.',
  },
  ReturnInProgress: {
    label: 'Return in progress',
    tooltip: 'A return was approved and is on its way back.',
  },
  Returned: {
    label: 'Returned',
    tooltip: 'The customer’s return has been received.',
  },
  Completed: {
    label: 'Completed',
    tooltip: 'Delivered and past the return window. Nothing is left to do.',
  },
  Cancelled: {
    label: 'Cancelled',
    tooltip: 'This seller’s share was cancelled.',
  },
};

/** `OrderPaymentStatus` — where the money for an order stands. */
export const PAYMENT_STATUS_VOCAB: Record<string, StatusDisplay> = {
  Pending: {
    label: 'Payment pending',
    tooltip: 'No payment has been collected for this order yet.',
  },
  Authorized: {
    label: 'Authorised',
    tooltip: 'The payment is held but has not been collected yet.',
  },
  Paid: {
    label: 'Paid',
    tooltip: 'The payment has been collected.',
  },
  Failed: {
    label: 'Payment failed',
    tooltip: 'The payment did not go through.',
  },
  PartiallyRefunded: {
    label: 'Partly refunded',
    tooltip: 'Some of the payment has been given back to the customer.',
  },
  Refunded: {
    label: 'Refunded',
    tooltip: 'The payment has been given back to the customer in full.',
  },
};

/** `ReturnStatus`. */
export const RETURN_STATUS_VOCAB: Record<string, StatusDisplay> = {
  Requested: {
    label: 'Awaiting a decision',
    tooltip: 'The customer has asked to return this. Approve or reject it.',
  },
  Approved: {
    label: 'Approved',
    tooltip: 'You agreed to the return. A pickup is arranged next.',
  },
  Rejected: {
    label: 'Rejected',
    tooltip: 'The return was turned down.',
  },
  PickupScheduled: {
    label: 'Pickup arranged',
    tooltip: 'A courier is booked to collect it from the customer.',
  },
  Picked: {
    label: 'Collected',
    tooltip: 'The courier has collected it from the customer.',
  },
  InTransit: {
    label: 'On its way back',
    tooltip: 'Travelling back to the seller.',
  },
  Received: {
    label: 'Received',
    tooltip: 'It has arrived back. Check its condition next.',
  },
  QcPassed: {
    label: 'Checked — OK',
    tooltip: 'The item passed its condition check. Refund or replace next.',
  },
  QcFailed: {
    label: 'Checked — not OK',
    tooltip: 'The item failed its condition check.',
  },
  Refunded: {
    label: 'Refunded',
    tooltip: 'The customer has been refunded.',
  },
  Replaced: {
    label: 'Replaced',
    tooltip: 'A replacement was sent instead of a refund.',
  },
  Closed: {
    label: 'Closed',
    tooltip: 'Finished. Nothing is left to do.',
  },
  Cancelled: {
    label: 'Cancelled',
    tooltip: 'The customer withdrew the return.',
  },
};

/** `ShipmentStatus` — one parcel, as the courier reports it. */
export const SHIPMENT_STATUS_VOCAB: Record<string, StatusDisplay> = {
  Draft: {
    label: 'Not booked yet',
    tooltip: 'Packed but no courier has been booked.',
  },
  Created: {
    label: 'Booked',
    tooltip: 'A courier is booked and a tracking number issued.',
  },
  LabelGenerated: {
    label: 'Label ready',
    tooltip: 'The shipping label can be printed and stuck on the parcel.',
  },
  PickupScheduled: {
    label: 'Pickup arranged',
    tooltip: 'The courier is due to collect it.',
  },
  PickedUp: {
    label: 'Picked up',
    tooltip: 'The courier has collected it.',
  },
  InTransit: {
    label: 'In transit',
    tooltip: 'On its way to the customer.',
  },
  OutForDelivery: {
    label: 'Out for delivery',
    tooltip: 'With the delivery agent today.',
  },
  Delivered: {
    label: 'Delivered',
    tooltip: 'The customer has it.',
  },
  Exception: {
    label: 'Delivery problem',
    tooltip: 'The courier could not deliver it. Decide what to do under Delivery problems.',
  },
  RtoInitiated: {
    label: 'Returning to seller',
    tooltip: 'Delivery was given up and it is coming back to the seller.',
  },
  RtoDelivered: {
    label: 'Returned to seller',
    tooltip: 'The undelivered parcel is back with the seller.',
  },
  Cancelled: {
    label: 'Cancelled',
    tooltip: 'The booking was cancelled.',
  },
};

/** How the order's payment method reads. `CashOnDelivery` is the API's enum name, not a phrase. */
export function paymentMethodLabel(method: string | null | undefined): string {
  if (!method) return '—';
  if (method === 'CashOnDelivery') return 'Cash on delivery';
  if (method === 'Prepaid') return 'Prepaid';
  return spaceOut(method);
}

/**
 * Looks a status up in the given vocabulary. An unknown value — one the API added after this file
 * was written — is spaced out ("SomeNewStatus" → "Some new status") rather than shown raw.
 */
export function statusLabel(vocab: Record<string, StatusDisplay>, status: string | null | undefined): string {
  if (!status) return '—';
  return vocab[status]?.label ?? spaceOut(status);
}

/** The explanation shown on hover, or nothing for an unknown value. */
export function statusTooltip(
  vocab: Record<string, StatusDisplay>,
  status: string | null | undefined,
): string {
  return (status && vocab[status]?.tooltip) || '';
}

/** The plain-language options for a status filter dropdown, in the vocabulary's own order. */
export function statusFilterOptions(
  vocab: Record<string, StatusDisplay>,
): readonly { readonly value: string; readonly label: string }[] {
  return Object.entries(vocab).map(([value, display]) => ({ value, label: display.label }));
}

function spaceOut(value: string): string {
  const spaced = value.replace(/([a-z])([A-Z])/g, '$1 $2').toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}
