# Step 12 — Pricing, Tax & Promotions module

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** C · **Depends on:** Step 11
- **Objective:** Deterministic, explainable price and tax calculation.
- **Deliverables:**
  - Price lists (base, sale, scheduled), per-vendor pricing, MRP vs selling price, tiered
    quantity pricing.
  - **GST engine:** HSN → rate resolution, place-of-supply logic, CGST/SGST vs IGST split,
    inclusive-of-tax pricing (Indian retail norm), rounding rules, cess support.
  - Promotion engine: coupon codes, automatic cart rules, category/brand/vendor scoping,
    BOGO/bundles, free shipping, first-order, stacking rules and priority, usage limits
    (global / per-customer), validity windows, flash-sale scheduling.
  - Loyalty / store-credit wallet (accrual + redemption) — design now, enable via feature flag.
  - **Price quote API** returning a fully itemised, auditable breakdown (line, discount, tax,
    shipping, total) — one calculation engine shared by cart, checkout, orders and invoices.
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
- **Full acceptance criteria (verified at Step 29, not now):** A golden-file test suite of ~30 pricing/tax scenarios (intra-state,
  inter-state, coupon + tax interaction, rounding) passes exactly.
- **Outcome / Notes:**

### What was built

The **Pricing module** (`pricing` schema, seven tables), and the contracts that make it the only
place in this platform permitted to compute a price, a discount or a tax figure.

**Price lists** — `price_lists` / `price_list_items`. A list carries a type (`base|sale|scheduled`),
a priority, a window and an optional `vendor_id`; that last column is the whole of *per-vendor
pricing*. The resolution rule is stated once, in `PriceResolver`: among the lists that are active,
in window, and either platform-wide or the offer's own seller's, the **lowest priority number
wins**; within the winner, the **highest quantity tier at or below the requested quantity** wins;
and an offer with no item in any applicable list keeps `catalog.listings.selling_price`. That
fallback is what lets a deployment run with no price lists at all, which is how most will start.
Tiered quantity pricing is several rows differing only in `min_quantity`. There is deliberately
**no MRP column** — MRP is statutory and belongs to the product, and a second copy would be a
second answer on an invoice.

**The GST engine** — `tax_rates` plus `GstCalculator`. A rate is a row per period rather than a
column that gets edited, so an invoice reprinted next year reproduces the rate that was in force
when the supply was made; resolution is always "as of" a date. An HSN with no row falls back to the
product's own `gst_rate`, so the table centralises the rates a store chooses to manage rather than
being a precondition for selling. The arithmetic is **inclusive-of-tax back-calculation**: taxable
value is `gross / (1 + (rate + cess) / 100)` — both levies sit on the same base, so both belong in
the divisor — cess is rounded and GST takes the residue, which is what guarantees the parts add
back up to the gross exactly. Place of supply is decided **per line against that line's seller's
GSTIN**, because each seller invoices under their own registration; a basket spanning two states
produces one CGST/SGST line and one IGST line, which is exactly what the two invoices will say.
Rounding is half-away-from-zero (not .NET's banker's default), and the grand total rounds to a
whole rupee with the difference reported as `roundingAdjustment` rather than absorbed.

**The promotion engine** — `promotions` / `promotion_redemptions` plus `PromotionEvaluator`. One
table for coupon codes and automatic cart rules, because they differ only in whether a shopper has
to type something. Six types (`percentage|fixed|free_shipping|bogo|bundle|tiered`), an `applies_to`
column (`line|order|shipping`) because "10% off sofas" and "10% off your order" allocate and tax
differently, `scope jsonb` (categories including descendants, brands, sellers, listings,
exclusions, segments) and `conditions jsonb` (minimum quantity, first order, payment method, the
buy-X-get-Y table, the bundle members, the tier ladder). Stacking is enforced **in both
directions**: an exclusive promotion cannot apply once anything else has, and nothing may apply
after one has. Order-level discounts are allocated pro rata across matching lines with the residue
on the largest, so the tax on a discount lands on the lines whose rates it changed. A **flash sale
is a promotion** with a short window and a low priority number; there is no scheduling machinery,
because a campaign that opens because the clock moved needs no job to open it.

**The store-credit wallet** — `wallets` / `wallet_transactions`, designed now and gated on the
`pricing.store-credit` flag exactly as the card asked. Balance is a derived cache maintained in the
same transaction as the movement; the amount is always positive and the type carries the sign;
every mover is keyed on the caller's reference so a refund event delivered twice credits once.

**The quote engine** — `IPriceQuoteEngine`, the single implementation. The order it does things in
is the design: price, then promotions, then tax on **what is left**, then shipping and the COD fee,
then rounding, then store credit. Tax before a discount overstates what a customer owes; credit
before rounding makes the rounding line lie. It returns the itemised breakdown `03` §4.6 specifies
— per line, per seller group, per order — plus every promotion it considered and, for the ones that
did nothing, **why**. Quoting writes nothing: it evaluates promotions without redeeming them and
clamps a wallet request without debiting it, because a cart is rendered many times and bought once.

**33 endpoints**: 11 price-list (including `GET /admin/prices/resolve`, which explains which list
won), 6 tax-rate (including `/resolve`), 9 promotion (including `POST /admin/promotions/simulate`),
4 wallet, and 3 storefront — `POST /store/quote`, `GET /store/me/wallet`, `/transactions`.

### Files and folders

`src/backend/modules/KlaraHome.Modules.Pricing/` — `Domain/` (PriceList, TaxRate, Promotion,
Wallet), `Application/` (PriceLists, Tax, Promotions, Quotes, Wallets), `Endpoints/`,
`Infrastructure/Calculation/` (GstCalculator, PromotionEvaluator, PriceResolver, TaxRateResolver,
QuoteEngine), `Infrastructure/Promotions/`, `Infrastructure/Wallets/`, `Infrastructure/Events/`,
`Infrastructure/Persistence/` with the `InitialPricingSchema` migration.
`src/backend/shared/KlaraHome.Contracts/Pricing/` — `IPriceCatalog`, `IPriceQuoteEngine`,
`IPromotionLedger`, `IStoreCredit`, `PricingEvents`.
Tests: `tests/KlaraHome.UnitTests/Pricing/` — `GstCalculatorTests`, `PromotionEvaluatorTests`.

### Deviations from the specification

1. **`03-database-design.md` §4.6 and `04-api-specification.md` §3.1, §3.3 and §4 were amended
   before the code was written** (protocol rule 9), and logged in `CHANGE_LOG.md`. §4.6 gains the
   columns, uniqueness rules and constraints of the seven tables it previously named in one line
   each; §4 gains the routes that now exist; §3.1 and §3.3 gain the customer wallet reads and the
   stateless quote.
2. **`ListingSummary` gained `CategoryId`, `CategoryPath` and `BrandId`** — a shared-contract
   change. Promotions are scoped to categories and brands and a promotion on a parent category must
   reach everything beneath it; the materialised path turns that into a substring test rather than
   a tree walk per cart line. Parked.
3. **A `pricing` store-settings section was added** (COD fee, delivery tax rate, rupee rounding,
   loyalty rate and cap, wallet ceiling, credit expiry). Commercial levers belong where a shopkeeper
   can change them; the platform's own limits stayed in `appsettings` as `PricingOptions`. Parked.
4. **`QuoteRequest.IsFirstOrder` is supplied by the caller**, not looked up. Order history belongs
   to Orders, and asking it on every cart render would be a query across a schema boundary on the
   hottest path in the basket. It defaults to `false` — the safe direction — and **Steps 13 and 14
   must pass it** or a first-order campaign will silently never apply. Parked.
5. **The store's own `FreeShippingThreshold`** (a Step 6 setting nothing had read) is applied by the
   quote engine, measured against the *discounted* total. Parked.
6. **`GET /admin/prices/resolve` and `GET /admin/tax-rates/resolve`** were added beyond the card's
   list, and into §4. They answer "why was this priced at that" using the same resolvers the engine
   uses, which is what makes their answer authoritative rather than an approximation of it.

### Known gaps and technical debt

- **No consumer for `Pricing.PriceChanged` or `Pricing.PromotionRedeemed`.** Both reach the outbox
  and stop; the Search projection is Step 19's and the alerts and reporting are Step 21's. Same
  shape as `StockRunningLow` from Step 11.
- **Loyalty accrual is designed and not wired.** The settings and the credit path exist; the
  trigger is an order completing, which is Step 14's event.
- **Nothing sweeps expired store credit.** The `Expiry` transaction type exists for a job that does
  not; parked at Step 31 with the other scheduled work.
- **`RedeemAsync` can refuse a promotion the quote applied** — somebody may take the last use
  between quoting and placing. It returns the refusals rather than throwing, and **Step 14 must act
  on that list**.
- **24 rows added to `TEST_DEBT.md`**, including the card's own full acceptance criterion: the
  ~30-scenario golden-file suite is Step 29's. What was tested now, under sprint rule 1, is the two
  pure algorithms — 46 unit tests over the GST arithmetic and the promotion walk, because both have
  a right answer independent of everything else and both fail silently in money.

### Verification

`dotnet build src/backend/KlaraHome.sln` succeeds with no errors and no warnings.
**591 unit tests green** (46 new) and 14 architecture tests green. The `InitialPricingSchema`
migration is generated and the migrator compiles; whether it applies is Step 28A's question
(sprint rule 4).
