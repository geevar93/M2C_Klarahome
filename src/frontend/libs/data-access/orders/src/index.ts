export * from './lib/orders.service';
export * from './lib/returns.service';

/**
 * The shapes these services answer with. Types only — see `data-access-content` for the reasoning.
 */
export type {
  CancelLineBody,
  CancelOrderBody,
  InvoiceDownloadResponse,
  InvoiceResponse,
  OrderAddressResponse,
  OrderEventResponse,
  OrderLineResponse,
  OrderResponse,
  OrderSummaryResponse,
  PagedResultOfOrderSummaryResponse,
  PagedResultOfReturnSummaryResponse,
  RaiseReturnBody,
  ReturnEligibilityResponse,
  ReturnLineResponse,
  ReturnReasonOption,
  ReturnResponse,
  ReturnSummaryResponse,
  ReturnableLineResponse,
  SubOrderResponse,
} from '@klarahome/data-access-api';
