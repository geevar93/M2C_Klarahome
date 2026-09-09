# Step 29 — Step 16 report: Shipping, Fulfilment & Logistics

> Modelled on the "Progress log" style of `../step-29-test-hardening-and-performance-baseline.md`
> §29.2/§29.3. Tracker: [`../../IMPLEMENTATION_PLAN.md`](../../IMPLEMENTATION_PLAN.md) · Test debt:
> [`../../TEST_DEBT.md`](../../TEST_DEBT.md) · Parking Lot: [`../../PARKING_LOT.md`](../../PARKING_LOT.md)

## Rows closed: 16 of 18

All ten 🔴 rows and all six 🟡 rows are closed by a named, passing test, plus the one 🟢 row.
Two rows are **partially** closed, and are marked so rather than falsely closed — see below.

| Row (paraphrased) | Status | Closing test(s) |
|---|---|---|
| A confirmed order → shipment with an AWB | ✅ | `ShippingBookingTests.A_confirmed_order_produces_a_shipment_with_an_awb_in_the_provider_sandbox` |
| Tracking updates → order timeline + notifications | 🟡 partial | Timeline: `ShippingWebhookTests.A_webhook_is_verified_stored_drained_and_applied_exactly_once`, `ShippingBookingTests.AdvanceAsync_moves_the_machine_as_system_and_refuses_an_edge_it_does_not_have`. Notifications: open, no consumer exists (Parking Lot, 2026-09-06, Step 16 row) |
| Webhook verified/stored/200/drained/applied once/redelivery no-op | ✅ | `ShippingWebhookTests.A_webhook_is_verified_stored_drained_and_applied_exactly_once` |
| Bad-signature webhook stored/ignored/401/never processed/replay refused | ✅ | `ShippingWebhookTests.A_badly_signed_webhook_is_stored_ignored_answered_401_and_never_processed` |
| Delivery scan collects COD cash; RTO waives it | ✅ | `ShippingCodTests.A_delivery_scan_collects_cash_and_a_return_to_origin_waives_it` |
| Courier remittance apportions a short total; remitted one untouched | ✅ | `ShippingCodTests.A_courier_remittance_apportions_a_short_total_and_leaves_a_remitted_one_untouched` |
| `IOrderFulfilment.AdvanceAsync` as System, refuses missing edge | ✅ | `ShippingBookingTests.AdvanceAsync_moves_the_machine_as_system_and_refuses_an_edge_it_does_not_have` |
| Two partial shipments cannot over-pack | ✅ | `ShippingBookingTests.Two_partial_shipments_cannot_between_them_pack_more_units_than_were_ordered` |
| Cross-vendor authorisation matrix (404 not 403) | ✅ | `ShippingAuthorisationTests.A_vendor_cannot_read_pack_book_label_or_cancel_another_sellers_parcel`, `...A_vendor_cannot_work_another_sellers_failed_delivery`, `...A_vendor_cannot_write_a_platform_wide_rate_rule` |
| Migration applies/re-runs clean; partitioned/append-only/DEFAULT | ✅ | Migration half: cross-cutting `SchemaMigrationTests` (every module). Partition half: `ShippingConstraintTests.Tracking_events_is_partitioned_append_only_and_has_a_default_partition` |
| Aggregator adapter contract (7 ops + token refresh on 401) | 🟡 partial | 7 ops via the fake, across `ShippingBookingTests`, `ShippingLabelTests`, `ShippingCodTests`, `ShippingWebhookTests`. Token refresh: `ShippingAggregatorContractTests.A_401_triggers_exactly_one_token_refresh_and_one_retry`, `...A_second_401_after_the_retry_is_reported_rather_than_looped` (against `ShiprocketShippingProvider` directly, scripted handler — no live sandbox exists) |
| Outbound client host allow-list (SSRF) | ✅ | `ShippingConstraintTests.The_outbound_client_refuses_any_host_but_the_configured_one` |
| `IShippingOptions` at checkout: priced / unserviceable / COD-refused | ✅ | `ShippingServiceabilityTests.Checkout_prices_a_serviceable_pin_offers_nothing_unserviceable_and_refuses_cod_where_cash_is_refused` |
| Serviceability cache hot-path / nightly refresh upserts | ✅ | `ShippingServiceabilityTests.The_cache_never_calls_a_courier_on_the_hot_path_and_the_refresh_job_upserts` |
| Tracking poll: silent parcels only, two workers no duplicates | ✅ | `ShippingServiceabilityTests.The_tracking_poll_finds_only_silent_booked_parcels_and_two_workers_produce_no_duplicates` |
| Every CHECK constraint refuses what it should | ✅ | `ShippingConstraintTests.Every_check_constraint_refuses_what_it_is_meant_to` |
| Every Shipping permission in `PermissionCatalog` | ✅ | `ShippingPermissionTests.Every_shipping_permission_appears_in_the_identity_catalogue` |
| Label + manifest: rendered, private, signed expiring link | ✅ | `ShippingLabelTests.The_label_and_manifest_are_reachable_only_through_an_expiring_signed_link` |

## Tests added: 21, across 9 new files

`ShippingBookingTests` (4), `ShippingWebhookTests` (2), `ShippingCodTests` (2),
`ShippingAuthorisationTests` (3), `ShippingConstraintTests` (3), `ShippingServiceabilityTests` (3),
`ShippingPermissionTests` (1), `ShippingLabelTests` (1), `ShippingAggregatorContractTests` (2) —
the last one xUnit-only, no Docker/Testcontainers needed, since it is a scripted-handler contract
test against `ShiprocketShippingProvider` rather than an API-driven one.

A new `OrderScenario` joins `CatalogScenario` and `VendorScenario` in the harness: it carries a
shopper from an empty basket through checkout to a placed, cash-on-delivery order — the shortest
honest path to a `Confirmed` sub-order, since a COD sub-order confirms at placement
(`OrderPlacementService.ConfirmForCashOnDelivery`) with no gateway to wait for. `CatalogScenario`
gained `StockAsync` (opens a warehouse and a stock row, then receives stock against it) because
every Step 16 test needs a real, checked-out order, and a checkout refuses an unstocked listing with
`CART_ITEM_OUT_OF_STOCK` — a fact Steps 9–10's own scenarios never needed to know.

## Defects found and fixed (in the Step 29 test harness — not Shipping's own production code)

Five were in `src/backend/tests/KlaraHome.IntegrationTests/Commerce/`, used by nothing before this
step wrote its first shipment-booking or webhook test:

1. **`CommerceTestBase.SignedInShopperAsync` called a route that has never existed.** It posted to
   `/api/v1/store/auth/otp/start`; the real route, mapped by `AuthEndpoints.MapOtp`, is
   `/api/v1/store/auth/otp/request`. Every call answered 404. Fixed, and the mobile-OTP-login feature
   flag (`IdentityFeatures.MobileOtpLogin`, shipped **off** by default — withdrawn from the live
   storefront pending an SMS provider) is now pinned on before the call, since without it the same
   route answers 404 for an unrelated reason.
2. **`CommerceTestBase.NewMobile()` produced a bare 10-digit number.** `RequestOtpCommand` and the
   OTP dispatcher both key on E.164 (`+91…`), so the code sent was filed under a different key than
   the one the test looked it up by, and `Otp.Latest` threw. Fixed to emit `+91…`.
3. **`Rest.PostRawAsync` disposed its `HttpRequestMessage` — and the `StringContent` on it — before
   the request it returned had actually been read**, because `using var request = …; return
   client.SendAsync(request, …);` disposes on return rather than on completion. Every caller that
   awaited the un-awaited task (which is every caller) raced the host reading the body against the
   `using` block, and lost about half the time: `ObjectDisposedException: StreamContent`. This is the
   webhook-posting helper every courier-webhook test in this step depends on. Fixed by awaiting
   inside the method before the `using` block unwinds.
4. **`FakeShippingProvider`'s air waybill was a per-instance sequence** (`AWB00000001`, …), but the
   uniqueness constraint it collides with (`ix_shipments_tenant_id_awb`) is per **tenant**, and every
   test in the collection shares one tenant and one `CommerceApiFactory`-per-test, so the *second*
   test in a run to book a shipment reliably hit `23505: duplicate key value`. Fixed to a
   `Guid`-derived AWB, unique across the whole collection.
5. **`FakeShippingProvider`'s courier-event id was the same per-instance sequence** (`evt_1`, `evt_2`,
   …), and the webhook receiver's replay protection is keyed on `(provider, provider_event_id)`
   **across the whole shared tenant**, not per shipment. Two different tests both mint `evt_2`, and
   whichever runs second has its webhook treated as a **duplicate of the first's** — its own scan is
   silently dropped (answered `200`, nothing stored, nothing applied) if the first was genuine, or a
   forged signature is waved through with a `200` if the first happened to be a legitimately-signed
   one. This produced exactly the intermittent failures it sounds like: `ShippingWebhookTests` and
   `ShippingCodTests` passed reliably alone or in a small group, and failed unpredictably once the
   full `Commerce` namespace ran together and the id space actually collided. Fixed the same way as
   (4): a `Guid`-derived event id.

None of these are Shipping module defects — they are all in test infrastructure this step is the
first to actually exercise (no Step 9/10 test ever signed in a shopper by OTP, posted a raw webhook
body, or booked more than one shipment in a shared-tenant run). No Shipping production code was
changed.

## What was learned about the product, and left alone

- **A booked parcel always ends at `LabelGenerated`, not `Created`.** `ShipmentBooker.AttachLabelAsync`
  renders this platform's own label whenever the courier's `LabelUrl` came back with the booking
  response — which `FakeShippingProvider` always supplies — rather than only when no courier label
  exists at all. This looks like it may not match the "courier's own label wins" intent recorded in
  the Step 16 card's Deviations §5, but changing it is Shipping business logic, and Step 29 protocol
  is to test what is built, not to reshape it near a green suite. Recorded here for whoever next
  touches `ShipmentBooker`; not filed to the Parking Lot since it costs nothing today (every one of
  this step's tests asserts the real status either way).
- **`SetCheckoutAddressCommand` refuses an unserviceable PIN code outright** (`PINCODE_NOT_SERVICEABLE`,
  422) rather than letting the shopper past address entry with an empty shipping-options list. This
  is *more* correct than the debt row's own phrasing assumed, and the closing test asserts the
  refusal rather than an empty list.
- **Delivery coverage defaults to Hyderabad-only** (`DeliveryCoverageSettings`, `AllowedPincodePrefixes:
  ["500"]`), which is a store policy, not a bug — the PIN codes used by every new test are all
  `500xxx` for exactly this reason.

## Suite state

| | Before this report | After |
|---|---|---|
| Unit | 947 | 947 (unchanged — no Shipping unit-test gaps were found; the module's own 12 were written at build time) |
| Architecture | 14 | 14 (unchanged) |
| Integration (`Commerce` namespace) | 61 | 82 (+21) |

**Verification run:** `KlaraHome.IntegrationTests`, `-namespace KlaraHome.IntegrationTests.Commerce`,
serialised (`-maxThreads 1`) — the full `VendorOnboardingTests`/`VendorAuthorisationTests`/
`VendorConstraintTests`/`CatalogPublishingTests`/`CatalogAuthorisationTests`/`CatalogConstraintTests`/
`CatalogImportTests`/`SchemaMigrationTests` set from Steps 9–10 plus every `ShippingXxxTests` class
added here. **Total: 82, Failed: 0, Errors: 0** (430s). `dotnet build src/backend/KlaraHome.sln` is
clean at 0 warnings. None of the pre-existing Step 9/10 test files were modified — only the shared
harness fixes below, which they also exercise and which did not change their behaviour.

## Left open, and why

- **The notification half of "tracking updates … trigger notifications."** No handler consumes
  `Shipping.ShipmentDispatched`, `ShipmentTrackingUpdated`, `ShipmentDelivered` or `ShipmentNdrRaised`
  — recorded in the Parking Lot on 2026-09-06 against Step 16, owner Notifications, and not
  reinvented as a new gap here. The order timeline *is* written (proved above), so "where is my
  order" is answerable; nobody is pushed.
- **The aggregator adapter against a genuinely live sandbox.** No Shiprocket account exists (Known
  Gaps, Parking Lot Step 16A → Step 32). Every operation is proved against `FakeShippingProvider`,
  which is the same substitute the module itself was built and unit-tested against, and the
  token-refresh policy is proved as a contract test against the adapter's own HTTP handling. Neither
  is a live-sandbox proof, and this report does not claim one.
