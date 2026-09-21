# ADR-022 — The shopper's delivery charge is the courier's live price at checkout, with the rate card as fallback

- **Status:** ✅ Accepted
- **Raised at:** 2026-09-21, outside the step sequence, by the User: *"shipping charges are not
  being included in the price … use the delivery aggregator's APIs to pull delivery charges for the
  pin code provided"*
- **Supersedes:** the *Rate selection* row of `08-integrations.md` §2 (*"our own `shipping_rates`
  table decides what the customer pays"*)
- **Related:** ADR-018 (Shiprocket, and delivery coverage as a store decision)

---

## Context

Since Step 16 the customer's delivery charge has come from the platform's own rate card —
`shipping.shipping_zones` and `shipping.shipping_rates`, matched by `RateResolver` — and the
aggregator has only ever been asked what *we* pay, after booking. The reasoning was sound: it keeps
price and cost as separate facts, and it keeps checkout off somebody else's uptime.

Two things made that insufficient in practice:

1. **Delivery showed as ₹0 on nearly every test order.** Not because charging was unbuilt, but
   because two free-shipping thresholds both default to ₹999 — `CommerceSettings.FreeShippingThreshold`
   (applied by the quote engine to the whole order) and the seeded rate card's `freeAbove` on every
   band. The architecture was right; the observable behaviour was not what the business wanted.
2. **A seeded tariff is a guess.** One catch-all zone at ₹49/₹69/₹99 is not what any courier
   charges for a given origin, destination and weight. The courier's own quote is the only figure
   that is not a guess.

Shiprocket's serviceability endpoint already returns a price per courier alongside its yes-or-no;
the adapter parsed it and threw it away.

## Decision

The User chose, from three options put to them:

1. **Pricing basis — the courier's live price, passed through at cost.** No markup. The courier's
   freight (without its cash-collection charge — cash on delivery carries the store's own handling
   fee in the pricing engine) plus GST where the quote is tax-exclusive.
2. **Surface — checkout only**, after an address is chosen, where the delivery selector already is.
   Product pages and the cart are unchanged.
3. **Hot path — call the courier live at checkout**, bounded by a short timeout, and fall back to
   the rate card when it cannot answer.

It is built as follows.

- `IShippingProvider` gains `QuoteRatesAsync(CourierRateRequest)`. The manual adapter answers
  "unavailable"; the Shiprocket adapter reads `freight_charge`, `courier_company_id`, `cod`,
  `blocked` and `recommended_courier_company_id` off the serviceability endpoint it already calls.
- A new internal seam, **`IDeliveryChargeSource`**, sits under `RatedShippingOptions`. Two
  implementations are registered as **keyed services** — `ratecard` and `aggregator` — and the
  unkeyed interface resolves to the one `Shipping:ChargeSource` names. Nothing else changes: the
  coverage gate, the serviceability cache, the seller's dispatch SLA and the checkout contract are
  identical whichever source answers.
- The aggregator source takes the seller's **default pickup PIN code** as the origin and quotes the
  **cheapest courier** on the route, the recommendation breaking a tie. *Amended the same day, by the
  User:* it first quoted Shiprocket's recommended courier, but on the first real route checked
  (500081 → 500089, 1.9 kg) that was Blue Dart Air at ₹319.68 against Amazon Surface at ₹92.72.
  Booking does not pin a courier, so the Shiprocket account's courier priority must be set to
  **Cheapest** for the courier booked to be the one the shopper paid for.
- **Shiprocket's sandbox is two hosts.** Sign-in and bookings are on `api-sandbox.shiprocket.in`,
  serviceability and rates on `serviceability-sandbox.shiprocket.in`, both taking the token the first
  issues; the sandbox has its own users and refuses production credentials. `Shipping:ServiceabilityBaseUrl`
  (blank in production, where `apiv2.shiprocket.in` answers everything) routes the rate and
  serviceability calls, and is added to the outbound allow-list.
- **What falls back, and what does not.** No courier configured, no pickup address, a failed call or
  one slower than `Shipping:LiveRateTimeoutMilliseconds` (3 s) → the rate card prices it, and a
  warning is logged. The courier answering *"nobody carries this"* → no service is offered, because
  that is a fact about the route and a tariff must not contradict it.
- A quote is reused for `Shipping:LiveRateCacheSeconds` (300 s) for the same route, weight, value
  and cash flag, so the price shown when options load is the price recorded when one is chosen.
- **Only Standard is offered** from live rates. Express needs the booking to name the quoted
  courier, which it does not yet do.

Configuration: `Shipping:ChargeSource` (`aggregator` by default; blank or unknown also means
`aggregator`, which degrades to the rate card by itself), `LiveRateTimeoutMilliseconds`,
`LiveRateCacheSeconds`, `AggregatorRatesIncludeTax` (false — **to be confirmed against a sandbox
invoice**) and `DefaultParcelWeightGrams` (500, for products with no weight).

## Consequences

- **A deployment with no courier behaves exactly as before**: the aggregator source falls straight
  through to the rate card. The rate card is now the fallback tariff rather than the only one, and
  must still be kept sensible.
- **Checkout's delivery step can take up to the timeout** when the courier is slow. It never fails
  because of it.
- **The store's free-shipping threshold still applies** on top, whichever source priced the parcel.
  An operator who wants every order to carry delivery sets it to 0 in admin.
- **Price ≠ cost is still recorded**: the price is frozen on the checkout shipment and the order;
  the freight Shiprocket bills arrives on the shipment at booking. The margin on delivery is still a
  query, and is now expected to hover near zero rather than be designed-in.
- **Cash on delivery cost is the store's own fee.** The courier's cash-collection charge is not
  passed through; `PricingSettings.CodHandlingFee` is meant to cover it and **defaults to ₹0**.
- **Known imprecision**, recorded in `PARKING_LOT.md`: the quote is on dead weight (the cart knows no
  box dimensions, so volumetric weight is not priced); the courier booked may differ from the one
  quoted unless the account's courier priority is set to Cheapest; and a
  basket priced as prepaid and then switched to cash on delivery is not re-quoted.
