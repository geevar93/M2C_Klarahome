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
- **Outcome / Notes:** _(to be filled on completion)_
