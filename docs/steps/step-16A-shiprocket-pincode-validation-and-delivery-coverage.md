# Step 16A — Shiprocket adapter, PIN-code validation & delivery coverage

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last — see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) §3 before starting. Write production
> code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** D · **Depends on:** Step 16
- **Raised by:** The User at the Step 16 boundary — *"We are going with Shiprocket for fulfilment …
  the shipping vendor should be switchable … add a way to validate the pin code entry using
  Shiprocket's API … local delivery restriction should be configurable, by default restrict the
  deliveries to Hyderabad."* Specification changed first under protocol rule 9; see **ADR-018** and
  the [Change Log](../CHANGE_LOG.md).
- **Objective:** Name Shiprocket in configuration and nowhere else; make a PIN code a checked fact
  rather than an assumption; and let the operator decide, without a deploy, where this store
  delivers.

---

## Deliverables

### 1. Adapter selection by courier key — the switchability the User asked for

- `ShippingProviders` gains `Shiprocket = "shiprocket"`. `Aggregator = "aggregator"` stops being a
  *selector* and survives only as a **legacy alias** that resolves to the configured adapter, so
  parcels and configuration written at Step 16 keep working.
- `ShippingProviderRegistry.Default` resolves `Shipping:Provider` to a registered
  `IShippingProvider` **by its key**, case-insensitively. Blank, unknown, or configured-but-unusable
  falls through to `manual`, which is always registered. There is still deliberately no third
  outcome: this module always has somewhere to book.
- `AggregatorShippingProvider` → **`ShiprocketShippingProvider`** under
  `Infrastructure/Courier/Shiprocket/`, publishing `Name => ShippingProviders.Shiprocket`.
- `ShippingOptions.HasAggregator` → `HasCourierApi`, and its documentation stops saying
  "aggregator" where it means "the configured courier".
- **A shipment keeps recording the key of the adapter that booked it.** Switching courier must
  leave in-flight parcels talking to the courier that holds them — `For(providerName)` already does
  this and must not regress.
- Adding a courier stays: one adapter class, one key constant, one `AddSingleton`. No caller
  changes, no interface change.

### 2. `ShiprocketShippingProvider` finished against the real API

Endpoints as tabulated in [`../08-integrations.md` §2.2](../08-integrations.md). **Confirm each path
against Shiprocket's current documentation when the account is provisioned** — they are written down
so the adapter is built once, not so they are trusted blind.

- **Auth.** `POST /v1/external/auth/login` with the API user's email and password; token cached in
  memory with its expiry, refreshed **exactly once** on a `401`, then the call fails rather than
  looping.
- **Serviceability + our cost.** `GET /v1/external/courier/serviceability/` with pickup and delivery
  postcodes, weight and the COD flag.
- **PIN-code details.** `GET /v1/external/open/postcode/details` → city and state.
- **Booking.** `POST /v1/external/orders/create/adhoc` then `POST /v1/external/courier/assign/awb` —
  the air waybill exists only after the second call, and a failure between them must leave the
  shipment recoverable rather than half-booked.
- **Label / manifest / pickup / cancel / track** as tabulated.
- **Webhook verification.** Shiprocket sends a shared secret in an **`x-api-key` header**, not an
  HMAC over the body. `VerifyWebhookSignature` accommodates both; the comparison is constant-time.
  The webhook route becomes `/webhooks/shipping/shiprocket` (the `{provider}` segment is the adapter
  key), and `aggregator` still resolves for anything already registered.
- The outbound client keeps refusing every host but `Shipping:BaseUrl`.
- The courier status map is extended to Shiprocket's own status vocabulary, preserving the
  `UNDELIVERED`-is-not-a-delivery rule Step 16 found the hard way.

### 3. PIN-code validation

- `ServiceabilityEntry` gains **`City`** and **`State`**, nullable, filled from the PIN-code-details
  call. Migration: `ShiprocketServiceabilityDetails` — additive, two nullable columns.
- `ServiceabilityService.RefreshAsync` writes them. The read path is unchanged and still **never
  calls a courier**; the optimistic-on-miss rule stays, because it is right for serviceability.
- `IReferenceData` gains `PincodeAsync(code)` → city, district, state id and GST state code, so the
  coverage check can use the platform's own seeded reference data before anyone's API. Shipping
  reads reference data; it does not copy it.

### 4. `DeliveryCoverageSettings` — the local delivery restriction

New public settings section in `KlaraHome.Contracts/Platform/StoreSettingsSections.cs`, key
`delivery-coverage`, registered in `SettingsCatalog`, edited in admin, effective on the next request:

| Field | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Off restores national trading |
| `AllowedCities` | `["Hyderabad", "Secunderabad"]` | Matched case-insensitively against the resolved city |
| `AllowedPincodePrefixes` | `["500"]` | `501`/`502` — Rangareddy, Sangareddy — are **excluded by default and one edit away** |
| `AllowedPincodes` | `[]` | Individual exceptions |
| `BlockedPincodes` | `[]` | Wins over every allow rule |
| `Message` | *"We currently deliver within Hyderabad only."* | The operator's own words, shown to the shopper |

- A PIN code is covered when coverage is off, or when it matches any allow rule and no block rule.
- **City resolution order:** `platform.pincodes` first (seeded and authoritative), the cached courier
  answer second, unknown third. An unknown city can still be covered by a prefix or an explicit PIN.
- Validator: prefixes are 1–6 digits, PIN codes are six and do not start with zero, and **an enabled
  policy with no allow rule at all is refused** with a message saying it would stop the store
  selling to anyone.
- `GET /store/config` carries the public summary, so the storefront can say where the store delivers
  before a shopper types anything.

### 5. Enforcement at all five gates

`IShippingOptions` (the seam Cart and Orders already hold) gains
`CheckDestinationAsync(pincode, isCod)` → `covered`, `serviceable`, `city`, `state`, `reason`,
`message`. No new cross-module contract is introduced.

| Gate | Behaviour |
|---|---|
| `GET /store/shipping/serviceability/{pincode}` | Answers both questions and names the one that failed |
| `PUT /store/checkout/{id}/address` | Refuses an uncovered or unserviceable destination |
| Cart validation | A **blocking** issue naming the address, so the cart cannot reach checkout |
| `GET /store/checkout/{id}/shipping-options` | An uncovered destination is offered **no** service |
| `place-order` | The last defence, refusing rather than taking money |

Two new error codes, and they stay distinct: **`DELIVERY_AREA_NOT_COVERED`** (our decision, and
reversible in a settings screen) and **`PINCODE_NOT_SERVICEABLE`** (no courier will carry it).

### 6. Admin surface

- `GET /admin/shipping/coverage` — the policy as it stands · `GET /admin/shipping/coverage/test/{pincode}`
  — would this address be accepted, and if not, which check refused it. The policy is **edited**
  through `PUT /admin/settings/delivery-coverage`, with every other store policy, so the audit trail
  and the validator stay in one place.
- `GET /admin/shipping/serviceability/{pincode}` and its existing refresh keep working and now show
  city and state.

### 7. Configuration and documentation

`.env.example`, `docs/06-infrastructure-devops.md` §4.1, `README.md`, `docs/dev-setup.md`.
`infra/compose/docker-compose.dev.yml` already passes every `SHIPPING_*` variable through — only its
comment changes. **No new environment variable is introduced**; `Shipping__Provider=shiprocket` is
the whole of turning the courier on.

---

## Build acceptance — what closes this step during the sprint

1. Every item under **Deliverables** is written: the adapter, the settings section and its
   validator, the coverage evaluator, the five gates, the endpoints, DI wiring, and the EF migration
   for the two new columns.
2. `dotnet build src/backend/KlaraHome.sln` succeeds with no errors and no new warnings.
3. `Shipping:Provider=shiprocket` selects the Shiprocket adapter; a blank or nonsense value selects
   `manual`; and **no type, table default or caller outside `ShippingProviders` names a courier** —
   greppable and to be grepped.
4. With coverage at its defaults, a `500081` address is accepted and a `560001` address is refused
   with `DELIVERY_AREA_NOT_COVERED` at each of the five gates.
5. Every behaviour named in the full acceptance criteria below that has not been proved has a row in
   [`../TEST_DEBT.md`](../TEST_DEBT.md).
6. Outcome / Notes filled in, Parking Lot updated, permission requested.

## Full acceptance criteria (verified at Step 29, not now)

1. Against a **Shiprocket sandbox account**: a confirmed order books an air waybill, fetches a label
   and a manifest, schedules a pickup, cancels, and tracks — with exactly one token refresh on a
   `401`.
2. A Shiprocket webhook carrying the configured `x-api-key` is verified, stored, answered `200`,
   drained and applied exactly once; one carrying a wrong key is stored, ignored and answered `401`.
3. A PIN code Shiprocket refuses is refused by the storefront with `PINCODE_NOT_SERVICEABLE`, and its
   city and state appear on the cached row.
4. With the default coverage policy, an order to a PIN code outside Hyderabad **cannot be created**
   through any of the five gates; adding `560` to the prefixes makes Bengaluru orderable with no
   deploy and no restart; disabling coverage restores national trading.
5. Changing `Shipping:Provider` from `shiprocket` to `manual` leaves parcels already booked with
   Shiprocket tracking, labelling and cancelling through Shiprocket.

## Explicitly out of scope

- A second live courier adapter (Delhivery, Blue Dart). The seam is the deliverable; a second
  implementation is not.
- An operator override that ships one parcel outside the coverage area. Recorded in the Parking Lot
  if it is wanted — it is a permission and an audit trail, not a settings field.
- Serviceability-driven **pricing**. The rate card still decides what the customer pays; Shiprocket
  still decides what we pay. Unchanged from Step 16.
- Storefront presentation of either refusal. The codes and the operator's message are produced here;
  Steps 24 and 25 render them.
- Reverse pickups, which remain Step 17's.

## Preconditions and risk

- **Shiprocket credentials do not exist yet** (`08-integrations.md` §7 row is open). Everything in
  §2 is therefore written against documented behaviour and proved at Step 29 or when a sandbox
  account arrives, whichever is sooner. The `manual` adapter carries fulfilment until then, so this
  is not a `⛔ BLOCKED` condition — but **no acceptance criterion touching a live courier can be
  closed without it**.
- The coverage half has no such dependency and is fully verifiable on the day it is written.

## Outcome / Notes

**Closed 2026-09-06.** All six build-acceptance items are met. `dotnet build src/backend/KlaraHome.sln`
is clean, `dotnet format` passes, **722 unit tests** (11 new) and **14 architecture tests** are green.
The credentials are deliberately blank, as the User instructed — *"configuration placeholders for the
specifics, we can plug them in after onboarding"* — so nothing here has been exercised against
Shiprocket.

### What was built

**1. Adapters are keyed by the courier they are.** `ShippingProviders.Shiprocket` (`"shiprocket"`)
is the new key; `Aggregator` survives only as an alias. `ShippingProviderRegistry.Default` now
resolves `Shipping:Provider` against the registered adapters instead of looking for one hard-coded
constant, and falls through to `manual` for a blank, unknown, misspelt or credential-less key — a
degradation rather than an outage. `HasAggregator` became `HasCourierApi` on both the registry and
the options. `Infrastructure/Courier/Aggregator/` is now `Courier/Shiprocket/`, and
`AggregatorShippingProvider` is `ShiprocketShippingProvider`. **Adding Delhivery is one class, one
key and one `AddSingleton`.**

**2. The Shiprocket adapter.** Routes carry the `v1/external/` prefix and the client's base address
is reduced to the configured URL's **origin**, so a path pasted into `SHIPPING_BASE_URL` cannot move
an endpoint while the host allow-list still bites. `PostcodeAsync` calls
`open/postcode/details` alongside serviceability and never fails the caller with it.
`VerifyWebhookSignature` accepts Shiprocket's `x-api-key` shared secret through a new constant-time
`ShiprocketWire.MatchesSecret`, and keeps the HMAC path for an adapter that signs properly. The
webhook endpoint reads `X-Api-Key` first, then the two signature headers.

**3. PIN-code validation.** `ServiceabilityEntry` gained nullable `City`/`State`, filled from
Shiprocket's answer and **kept when a later answer comes back thin** — a place name does not change
because one call was thin. Migration `20260906072848_ShiprocketServiceabilityDetails`: two nullable
columns, additive. `IReferenceData.PincodeAsync` and `PincodeInfo` were added to the contracts, and
`ReferenceDataService` queries `platform.pincodes` rather than caching nineteen thousand rows.

**4. `DeliveryCoverageSettings`.** Public settings section `delivery-coverage`, seeded automatically
by the existing catalogue seeder and carried into `GET /store/config` by the existing public-section
projection — neither needed a change, which is the design working. Defaults: enabled, cities
Hyderabad and Secunderabad, prefix `500`. Its validator refuses **an enabled policy with no allow
rule at all**, which is the one edit that would silently stop the store selling to anybody.

**5. The five gates.** `IShippingOptions` gained `CheckDestinationAsync` and the
`DeliveryCheck`/`DeliveryRefusal` types; no new cross-module contract was introduced. Enforced at:
the storefront PIN check (`GetServiceabilityQueryHandler`), checkout address selection
(`SetCheckoutAddressCommandHandler`), cart validation (`CartRenderer`, as a **basket-level blocking
issue** — an out-of-area address is nobody's seller's fault), shipping-option quoting
(`RatedShippingOptions`, which returns no options), and `place-order`. Two error codes that stay
distinct: `DELIVERY_AREA_NOT_COVERED` (with the operator's own message) and
`PINCODE_NOT_SERVICEABLE`.

**6. Admin surface.** `GET /admin/shipping/coverage` and `GET /admin/shipping/coverage/test/{pincode}`.

**7. Eleven unit tests**, under sprint rule 1 — the coverage rule and the registry are both pure,
both have a right answer, and every failure mode in them is silent: a rule that matches too little
refuses every order and looks exactly like a courier outage; a registry that resolved a stored
provider name from configuration would orphan every parcel in flight the day a store changes courier.

### Deviations from the specification as written at the step boundary

1. **The delivery area is edited through `PUT /admin/settings/delivery-coverage`, not
   `PUT /admin/shipping/coverage`.** `docs/04` §4 named the latter; a second write path for one
   settings row would be a second place to get the audit trail and the validator wrong. Shipping
   reads and tests it, Platform owns the write. `docs/04`, `docs/08` and `dev-setup` were corrected.
2. **The coverage test route is `GET /coverage/test/{pincode}`**, not a `POST` with a body. It reads
   nothing and writes nothing, so it is a GET.
3. **The coverage rule is written twice** — in Shipping, and in the Cart module's degenerate
   `StandardShippingOptions`, which must still honour a trading decision that has nothing to do with
   whether a courier integration exists. The two modules may not reference one another. Parked.
4. **`CourierServiceability` gained `City`/`State`.** The interface had nowhere to carry the answer
   Shiprocket gives, and the coverage check needs a fallback for an unseeded deployment.

### Known gaps

- **No Shiprocket account.** Every path in §2 of the card is written from published documentation
  and unproved: booking, label, manifest, pickup, cancel, tracking, token refresh and webhook
  verification. **Confirm the paths on the day credentials arrive.** Until then `Shipping:Provider`
  stays blank and the manual adapter carries fulfilment.
- **No operator override** ships one parcel outside the coverage area. Out of scope by decision; it
  is a permission and an audit trail rather than a settings field. Parked.
- **The coverage check runs on every cart render.** Cheap, and measured at Step 29.
- **Twelve integration tests deferred** — see [`../TEST_DEBT.md`](../TEST_DEBT.md).
