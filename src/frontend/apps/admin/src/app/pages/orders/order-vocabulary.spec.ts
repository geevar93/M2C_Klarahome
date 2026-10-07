import {
  ORDER_STATUS_VOCAB,
  PAYMENT_STATUS_VOCAB,
  RETURN_STATUS_VOCAB,
  SHIPMENT_STATUS_VOCAB,
  SUB_ORDER_STATUS_VOCAB,
  statusLabel,
} from './order-vocabulary';

/**
 * The keys must be the backend enum names, in full. The API drops a filter value it cannot parse
 * without saying so, so a key that drifts turns a filter into "show everything" rather than an
 * error. When one of these enums changes, this list changes with it.
 */
describe('order vocabulary', () => {
  it.each([
    ['OrderStatus', ORDER_STATUS_VOCAB, ['PendingPayment', 'InProgress', 'Completed', 'Cancelled']],
    [
      'SubOrderStatus',
      SUB_ORDER_STATUS_VOCAB,
      [
        'PendingPayment',
        'PaymentFailed',
        'Confirmed',
        'Processing',
        'Packed',
        'Shipped',
        'OutForDelivery',
        'DeliveryFailed',
        'RtoInitiated',
        'RtoDelivered',
        'Delivered',
        'ReturnRequested',
        'ReturnInProgress',
        'Returned',
        'Completed',
        'Cancelled',
      ],
    ],
    [
      'OrderPaymentStatus',
      PAYMENT_STATUS_VOCAB,
      ['Pending', 'Authorized', 'Paid', 'Failed', 'PartiallyRefunded', 'Refunded'],
    ],
    [
      'ReturnStatus',
      RETURN_STATUS_VOCAB,
      [
        'Requested',
        'Approved',
        'Rejected',
        'PickupScheduled',
        'Picked',
        'InTransit',
        'Received',
        'QcPassed',
        'QcFailed',
        'Refunded',
        'Replaced',
        'Closed',
        'Cancelled',
      ],
    ],
    [
      'ShipmentStatus',
      SHIPMENT_STATUS_VOCAB,
      [
        'Draft',
        'Created',
        'LabelGenerated',
        'PickupScheduled',
        'PickedUp',
        'InTransit',
        'OutForDelivery',
        'Delivered',
        'Exception',
        'RtoInitiated',
        'RtoDelivered',
        'Cancelled',
      ],
    ],
  ])('covers every %s value and nothing else', (_name, vocab, members) => {
    expect(Object.keys(vocab).sort()).toEqual([...members].sort());
  });

  it('spaces out a status it does not know rather than showing it raw', () => {
    expect(statusLabel(SUB_ORDER_STATUS_VOCAB, 'AwaitingCourierSlot')).toBe('Awaiting courier slot');
    expect(statusLabel(SUB_ORDER_STATUS_VOCAB, null)).toBe('—');
  });
});
