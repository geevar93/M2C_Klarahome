# Step 16 — Shipping, Fulfilment & Logistics module

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** D · **Depends on:** Step 15
- **Objective:** Get parcels moving and keep customers informed.
- **Deliverables:**
  - Shipping zones and rate rules (weight/value/zone/free-shipping thresholds), per-vendor
    overrides, volumetric weight.
  - PIN-code serviceability and ETA estimation (prepaid vs COD serviceability differ).
  - Logistics aggregator integration behind a provider interface (Shiprocket / Delhivery /
    Blue Dart class): create shipment, generate AWB + label + manifest, schedule pickup,
    cancel shipment.
  - Packing/fulfilment workflow: pick list, pack, weight capture, multi-package shipments,
    partial shipments.
  - Tracking webhook/polling ingestion → order timeline + customer notifications; NDR
    (non-delivery report) handling and re-attempt workflow.
  - COD remittance tracking per shipment.
- **Build acceptance - what closes this step during the sprint:**
  1. Every item under **Deliverables** is written: entities, configuration, handlers, endpoints,
     module registration and DI wiring, and the EF migration for this module.
  2. The code compiles - `dotnet build src/backend/KlaraHome.sln` (backend) or
     `npx nx build <app>` (frontend) succeeds with no errors.
  3. The endpoints are registered and declare their permissions; the module is referenced by the
     host, and the migrator project compiles with the new migration.
  4. Every behaviour named in the full acceptance criteria below that has not been proved has a
     row in [`../TEST_DEBT.md`](../TEST_DEBT.md).
  5. Outcome / Notes filled in, Parking Lot updated, permission requested.
- **Full acceptance criteria (verified at Step 29, not now):** A confirmed order produces a shipment with an AWB in the provider
  sandbox; tracking updates flow into the order timeline and trigger notifications.
- **Outcome / Notes:**

  **Closed 2026-09-06.** The Shipping module exists, compiles, registers 30 endpoints and fills the
  seam Cart declared at Step 13. `dotnet build src/backend/KlaraHome.sln` is clean with no warnings
  in the new code; 711 unit tests and 14 architecture tests pass. **No aggregator credentials
  exist**, so nothing here has been proved against a live courier — see *Known gaps*.

  ### What was built

  `src/backend/modules/KlaraHome.Modules.Shipping`, schema `shipping`, module order 120 (after
  Payments, before Returns).

  **Domain (9 files).** `ShippingZone` — states and PIN-code ranges together, priority as the
  tie-break, and a zone that names nowhere covers everywhere. `ShippingRate` — the rate-card rule:
  zone, service, weight band, order-value band, base + per-kilogram, free-above threshold and a
  cash-handling fee, with a nullable `vendor_id` that is the seller's override. `ServiceabilityEntry`
  — one cached answer per (PIN code, courier), prepaid and COD as separate flags because Indian PIN
  codes genuinely differ. `Shipment` + `ShipmentLine` — the aggregate, with **two weights** (packed
  and courier-billed) and **two freight figures** (charged and cost), so a weight dispute and a
  delivery margin are both queries rather than guesses. `TrackingEvent` — append-only, partitioned
  monthly. `NdrRecord` — one row per failed attempt, not per parcel. `ShippingManifest` — the
  handover sheet. `CourierEvent` — the webhook log, replay index and dead-letter queue in one table.
  `ShipmentLifecycle` — the transition table: forward movement may skip states because couriers
  report out of order, backward movement is refused, and the two corrections that genuinely happen
  (exception then moving again, RTO then delivered after all) are edges rather than exceptions.

  **Courier integration.** `IShippingProvider` with two adapters. `AggregatorShippingProvider` is
  written against the Shiprocket-class API docs/08 §2 recommends — token exchange with exactly one
  refresh on a 401, two-call booking (register, then assign the air waybill), label fetch, manifest,
  pickup, cancel, track, and HMAC webhook verification. `ManualShippingProvider` is the adapter with
  nothing behind it: an operator types the air waybill their courier gave them, and everything
  downstream is unchanged. The registry falls through to it whenever the aggregator is unconfigured
  or unusable, which is what makes this module useful on the day it ships.

  **The rate engine.** Zone by priority (catch-all last), then the seller's own rule over the
  platform's, then the narrower weight band. The base rate buys the band's floor weight — or the
  first kilogram where the floor is zero — and every part-kilogram above it is charged whole. A
  free-shipping threshold zeroes the freight and **not** the cash-handling fee. Volumetric weight at
  the configured divisor (5000), and the chargeable weight is the greater of the two.

  **The workflow.** `ShipmentWorkflow` is the single place a parcel moves; `ShipmentBooker` is the
  single place one is booked. A scan is deduplicated, applied, and recorded — and where the machine
  had no edge for it, recorded anyway and marked unapplied, because a courier saying something
  impossible is the one thing worth not discarding. Delivery closes any open failed-delivery report,
  records the cash as collected and starts the return window; a return to origin waives the cash.

  **Three seams added to `KlaraHome.Contracts`**, each implemented by the module that owns the data.
  `IOrderFulfilment` (Orders) — read what a parcel is booked from, advance the sub-order as `System`,
  or write a timeline note. `ICodCollections` (Payments) — attach a shipment to a cash record, mark
  it collected or waived, and apply a courier's remittance by shipment. `IVendorPickupPoints`
  (Vendors) — read a seller's collection address and write back the courier's id for it. All three
  are the mirror of `IOrderPaymentSync` and are registered unconditionally.

  **Endpoints (30).** One storefront route: anonymous, cached serviceability. Admin: zones and rates,
  serviceability read and refresh, the pick list, pack, weigh, book, label, schedule pickup,
  dispatch, cancel, sync tracking, record tracking by hand, manifests, the failed-delivery queue and
  its action, the courier-event log and replay, and the COD remittance import. One webhook receiver
  at `/webhooks/shipping/{provider}` that verifies, stores and answers `200` — and does nothing else.

  **Jobs.** Courier-event worker (5 s), tracking poll (30 min, for parcels silent 24 h),
  serviceability refresh (daily). All three off in the API and on in the worker.

  **Migrations.** `InitialShippingSchema` (8 tables) and `TrackingEventPartitions` — hand-written,
  `PARTITION BY RANGE (occurred_at)`, 25 monthly partitions plus a `DEFAULT`, an append-only trigger,
  and a unique index on (shipment, provider event, instant). The same shape as `platform.audit_logs`
  and `inventory.stock_ledger_entries`, and for the same reasons.

  **Five permissions** added to the Identity catalogue and wired into six role bundles: Support gains
  parcel reads, Operations gains the whole fulfilment surface, Finance gains the rate card, vendor
  owners gain their own parcels and rate overrides, vendor staff gain packing and dispatch.

  **Twelve unit tests** — rate arithmetic, volumetric weight, the transition table, the courier
  status map, the deduplication id. They found two real bugs while being written: the base rate
  charged an extra kilogram for any parcel in a band starting at zero, and `"UNDELIVERED"` was being
  read as a delivery because it contains the word.

  ### Deviations from the specification

  1. **Three new shared contracts.** docs/01 §2.1 names the seam but not its shape. `IOrderFulfilment`
     and `ICodCollections` are the mirrors of contracts those modules already publish;
     `IVendorPickupPoints` is a fourth, and it is the **only place another module writes into the
     Vendors schema** — one field, the courier's id for an address, which only this module can learn.
  2. **Two shipment states before the courier's.** docs/02 §5 starts the machine at `Created`. A
     `Draft` state was added in front of it: the gap between "the sub-order is confirmed" and "a
     courier has accepted it" is the whole packing workflow, and without it a platform cannot answer
     "what is waiting to be packed".
  3. **The manual adapter is not in the specification.** docs/08 §2 names hand-booking only as the
     fallback when an API call fails. It is a first-class adapter here, because a marketplace with no
     aggregator account otherwise cannot dispatch at all — and the same code path serves both cases.
  4. **The courier's label is stored as an object key, not a media file.** `LabelObjectKey` sits
     beside `LabelFileId` rather than replacing it: a courier's PDF carries a customer's address and
     has no gallery, no derivatives and no owner but the parcel, so registering it in the media
     library would put a delivery address into a browsable catalogue.
  5. **The rendered fallback label carries no barcode.** The shared document vocabulary draws text,
     rules and tables. The air waybill is printed large and read by a human, which is enough for a
     hand-booked parcel and is why an aggregator's own label always wins where one exists.
  6. **Freight tax is back-calculated at a configured rate** (18 %, the Indian rate on courier
     services) and is used for display only. The split that reaches an invoice is the pricing
     engine's, computed from the same tax-inclusive figure, so the two cannot disagree about a total.
  7. **`dotnet format` was run across the solution** and one pre-existing naming violation in the
     Payments module (`Load` → `LoadAsync`) was corrected, because the format gate was red before
     this step began and a red gate hides the next failure. Recorded in the Parking Lot.

  ### Known gaps

  - **No aggregator credentials.** Every path that needs a courier API is unproved against a live
    service: booking, label fetch, manifest, pickup, cancel, tracking and webhook verification. The
    manual adapter covers all of them functionally and none of them evidentially.
  - **Nothing consumes the four `Shipping.*` events yet**, so the "customer notifications" half of
    the deliverable reaches the outbox and stops there. Recorded in the Parking Lot.
  - **Reverse pickups are modelled and not built.** `Shipment.MarkReturn`, `IsReturn` and the
    provider's `isReturn` flag exist; the RMA that would create one is Step 17's.
  - **Every integration test is deferred** — see [`../TEST_DEBT.md`](../TEST_DEBT.md), which carries
    eighteen rows for this step.
