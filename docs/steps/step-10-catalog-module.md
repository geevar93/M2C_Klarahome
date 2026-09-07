# Step 10 — Catalog module

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** C · **Depends on:** Step 9
- **Objective:** The product information model.
- **Deliverables:**
  - Category tree (materialised path), brands, attribute sets, attributes (typed, filterable,
    variant-defining), attribute values.
  - Product → Variant (SKU) model; per-vendor **listings/offers** against a variant so multiple
    vendors can sell the same product; buy-box selection rule.
  - Media galleries, rich descriptions, specifications, SEO slugs/meta, canonical URLs.
  - India compliance fields: HSN code, GST rate, MRP, net quantity, country of origin,
    manufacturer/packer/importer details, expiry/shelf-life where applicable
    (Legal Metrology + Consumer Protection (E-Commerce) Rules 2020).
  - Product lifecycle/state machine (Draft → Pending Approval → Active → Archived) with
    platform moderation of vendor-submitted products.
  - Bulk import/export (CSV/XLSX) with validation report.
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
- **Full acceptance criteria (verified at Step 29, not now):** A variant with attributes, media and two competing vendor offers can
  be created, moderated, published and retrieved; bulk import of 1,000 SKUs validates and loads.
- **Outcome / Notes:**

## What was built

The **Catalog** module (`src/backend/modules/KlaraHome.Modules.Catalog`, schema `catalog`, module
order 60 — after Vendors, before every module that holds a `listing_id`). Fourteen tables, one
sequence, **55 endpoints**, one published contract and three integration events.

### Taxonomy

- **`categories`** — a **materialised path** tree. The path is built from **ids**, not slugs, so a
  rename never rewrites a subtree, and "everything under Home & Kitchen" is one index range scan
  instead of a recursive CTE on every listing page. Moving a node rewrites its descendants' paths in
  the handler; the entity returns the old prefix rather than cascading, because the descendants are
  rows it does not own. Depth is capped at six and a cycle is refused by a prefix test.
- **`brands`** — unique on both slug and name per tenant, because "Philips" and "philips" are one
  brand and two facet values.
- **`attributes`** / **`attribute_options`** / **`attribute_sets`** / **`attribute_set_members`** —
  six data types, four typed value columns rather than one text column (a range facet on "under 500
  grams" is a numeric predicate, and a cast in the `WHERE` clause defeats every index). A `CHECK`
  refuses `is_variant_defining` for anything but `select`/`multiselect`: only a closed list can be an
  axis. Requiredness lives on the set membership as well as on the attribute, because wattage is
  optional in general and mandatory for a lamp. Options are reconciled **in place, matched by id**,
  so a variant pinned to "Beige" is not orphaned by an edit, and an option something points at cannot
  be removed at all.

### Product → Variant → Listing

- **`products`** — the marketable concept, with a five-state life cycle
  (Draft → PendingApproval → Active ⇄ Inactive → Archived) declared as a transition table rather than
  a chain of `if`s. Vendor-scoped through a **nullable** `vendor_id`: a seller sees their own products
  *and* the platform's shared ones, and may write only to their own — a distinction a query filter
  cannot express, so it is checked once in `CatalogScope`. `search_vector` is a **stored generated
  tsvector** with a GIN index, declared as an EF **shadow property** so the Domain layer never names
  an Npgsql type and the architecture test still passes.
- **`variants`** — the sellable thing. `attribute_hash` is a SHA-256 of the sorted
  `(attribute_id, option_id)` pairs, with a unique index on `(tenant_id, product_id, attribute_hash)`.
  That is what enforces "a variant's defining combination is unique within its product", because no
  index can span child rows. SHA-256 rather than `string.GetHashCode`, which is randomised per process
  and would make the index silently stop working after a restart.
- **`listings`** — a vendor's offer, unique per `(vendor, variant)`. `selling_price <= mrp` is a
  **database `CHECK`**, not a validator, because in India it is not a preference.

### Buy box

`BuyBoxResolver` is a pure function over the candidates and the configured rule, so the decision that
is made dozens of times on a listing page touches no database and is testable without one. The rule
is a new **`buy-box` store-settings section** (`BuyBoxSettings` in Contracts, one line in Platform's
`SettingsCatalog`): an ordered criteria list plus `treat_unrated_as_average`. Two decisions worth
naming — an **unrated seller scores 3.0 by default**, because ranking them last is the cold-start
failure that makes a marketplace impossible to join; and the final tie-break is the **oldest offer**,
without which two identically priced sellers would swap the buy box between page loads and the price
in a search result would disagree with the price on the product page.

### India compliance

`Product.ComplianceGaps()` and `Variant.ComplianceGaps()` return **human sentences, not a boolean**,
because the operator pressing "publish" has to be told what to go and fix. HSN, GST rate, country of
origin and the manufacturer are on the product — facts about the goods, and two sellers cannot
disagree about an HSN code. MRP, net quantity and weight are on the variant, because they are per
pack. An importer is demanded only for goods whose origin is not `IN`.
`Catalog:EnforceComplianceOnPublish` exists for seeding a demo catalogue and defaults to on.

### Moderation

A **row per submission** rather than a status column, so a product rejected twice and approved on the
third attempt keeps both refusals and their reasons — which is exactly what an operator needs when
the seller asks why. A partial unique index allows one pending row per product, and `Decide` refuses
a second decision: two moderators pressing approve and reject a second apart is a conflict to report,
not a race to win. Platform staff publish their own drafts outright (`RequireModeration` still gates
a *seller's* submission), and the moderation row still records who decided, so the trail is complete
either way.

### Bulk import / export

`POST /admin/products/import` stores the upload in object storage and returns **202 and a job id**;
the worker claims `catalog_jobs` rows with `FOR UPDATE SKIP LOCKED` and writes progress and a capped
validation report back. **Row at a time, each in its own save** — a single transaction over a
thousand rows would let one bad row discard the other 999, and the merchandiser's afternoon with it.
Re-running the same file converges rather than duplicating (a known SKU is updated), and a blank cell
means "leave what is there", so a two-column price file cannot wipe a catalogue's descriptions. The
export writes the importer's own column layout, streamed in keyset pages, so the round trip is
genuinely a round trip; `GET /admin/products/import-template` hands out the header row.

> **Corrected at Step 29.** This section originally said "the first row for a product creates it;
> later rows for the same slug add variants to it". It never could: the template has no attribute
> columns, so every variant an import creates carries the same "no options" combination hash, and
> the unique index over `(tenant, product, attribute_hash)` refuses the second row. One SKU per
> product row is what the importer actually loads. The second row is now refused with a message a
> merchandiser can act on; attribute columns are a Parking Lot item.
>
> Two further claims here needed code rather than a correction, and got it at Step 29: a genuine
> two-column price file (`sku,mrp`) was **rejected**, because the product was resolved by slug only
> and a row with no name and no slug named nothing — it now falls back to the SKU's own variant;
> and a row the database refused **detached the job** along with the failed entity, so the report
> was never written and the job sat in `Running` for ever, unreportable and never re-claimed.

### Published contract and events

- **`IProductCatalog`** (`KlaraHome.Contracts.Catalog`) — `FindListingAsync` / `FindListingsAsync`
  returning a `ListingSummary`. Inventory keys stock on a `listing_id`, a cart line holds one and an
  order line freezes a snapshot of one; none of them may join to this schema. `IsPurchasable` is
  computed in SQL rather than reconstructed by each caller, so three callers cannot combine the three
  underlying booleans three different ways.
- **`ListingPublished` / `ListingUpdated` / `ListingDeactivated`**, published through the **keyed**
  per-context outbox the Step 9 Parking Lot entry insists on.
- **`VendorActivated` / `VendorSuspended` / `VendorOffboarded` are now consumed** — the first
  integration-event consumer this platform has. A suspension withdraws every live offer of that
  seller, which is the only way "a Listing cannot be Active if its Vendor is not Active" can be
  maintained across a module boundary. Reactivation deliberately does **not** put them back: which
  side paused an offer is not recorded on the listing, and guessing would republish prices a
  suspended seller never got to review.

## Files and folders created

```
src/backend/modules/KlaraHome.Modules.Catalog/
  CatalogModule.cs · KlaraHome.Modules.Catalog.csproj · packages.lock.json
  Domain/          Category, Brand, Attributes, Product, Variant, Listing,
                   CatalogMedia, ProductModeration, CatalogJob, SeoMetadata
  Application/     Taxonomy/ (Category, Brand, Attribute, AttributeSet)
                   Products/ (Contracts, Reader, Writer, Feature, Variant x2, Moderation, Lifecycle)
                   Listings/ · Storefront/ · Import/ · Validation/ · CatalogErrors
  Endpoints/       CatalogPermissions, AdminTaxonomy, AdminProduct, AdminListing,
                   AdminCatalogJob, StoreCatalog
  Infrastructure/  CatalogOptions, CatalogScope, BuyBox/, Directory/, Events/,
                   Import/ (Csv, ProductImportRunner, ProductExportRunner, CatalogJobDispatcher),
                   Persistence/ (+ Migrations/20260905191520_InitialCatalogSchema)
src/backend/shared/KlaraHome.Contracts/Catalog/  CatalogEvents.cs · IProductCatalog.cs
src/backend/tests/KlaraHome.UnitTests/Catalog/   4 files, 59 tests
```

Amended elsewhere: the solution and all three hosts plus `ModuleAssemblies.cs`; `tools/ef.ps1` and
`tools/ef.sh`; `appsettings.json` in the API and the worker (`Catalog:*`, with `JobRunnerEnabled`
false in the API and true in the worker, exactly as the notification dispatcher is arranged);
Identity's `PermissionCatalog` (8 permissions, and the `catalog-manager` role bundle that has been
empty since Step 7 is finally filled in); Platform's `SettingsCatalog`;
`KlaraHome.Contracts/Platform/StoreSettingsSections.cs`; `KlaraHome.Contracts/Vendors/IVendorDirectory.cs`;
`OutputCacheExtensions` (`CachePublicRead`, the sibling its own comment said Step 10 would add).

## Deviations from the specification

1. **`GET /store/products` — the faceted listing — is not built here.** §3.2 files it under Catalog &
   discovery, but it reads a denormalised projection with facet counts, which is the `search` schema
   and **Step 19's** deliverable. Serving it from this schema would be a five-table join per page and
   no facets at all. §3.2 now says so explicitly.
2. **XLSX import/export is not built; CSV is.** The deliverable says "CSV/XLSX". Every mature .NET
   XLSX library is either under a licence `Directory.Packages.props` forbids or carries a transitive
   dependency tree whose licences could not be verified offline, and adding one is the User's
   decision rather than mine. What is built is a hand-written ninety-line RFC 4180 reader and writer
   with no dependency at all. **Parked, and raised at this boundary.**
3. **The `listings` buy-box index has no `INCLUDE` column.** §5 asks for `selling_price` to be
   included so the ranking read is index-only. EF's `IncludeProperties` takes a scalar property name,
   and the price is a complex property — neither the CLR path nor the column name resolves. It is a
   one-line raw-SQL step at Step 29, once a query plan says the heap fetch actually costs something.
4. **`product_media` and `variant_media` are one table**, `media_assets`, discriminated by which
   owner column is set. Identical columns, identical behaviour, and the product page needs their
   union in one ordered read. §4.4 updated.
5. **`ProductAttribute`, not `Attribute`.** `System.Attribute` is in every file's implicit usings, and
   a domain type that shadows it produces error messages nobody can read.
6. **`VendorSummary` gained a `Rating`.** The buy box ranks partly on the seller's rating and cannot
   read the sellers' table. One field on the contract and one line in `VendorDirectory`.
7. **Four tables §4.4 did not list** — `attribute_set_members`, `media_assets`, `product_moderations`
   (as rows rather than a column) and `catalog_jobs`. All four are structure a named deliverable
   requires. §4.4 records them now, along with the combination-hash rule and the buy-box settings
   section.

## Known gaps and technical debt

- **No integration tests, by sprint rule.** 15 rows in `TEST_DEBT.md`. 59 unit tests were written
  under rule 1 for the things cheapest to get right while writing them: the three transition tables,
  the buy-box comparison, the combination hash, the CSV parser and the compliance rules.
- **Stock is not consulted by the buy box.** Inventory arrives at Step 11; the resolver treats
  "unknown" as neutral rather than as out of stock, so the criterion is a no-op until it exists and
  needs no change when it does.
- **`products.rating_average` is written by nobody** — Reviews (Step 21) owns it, as with
  `vendors.rating`.
- **Nothing validates that a `file_id` in a gallery exists.** Deliberate (ADR-016): the reference is
  soft, and an id that resolves to nothing renders as a gap rather than failing the page.
- **Descriptions are stored as given.** HTML sanitisation on the way in is not implemented, and the
  field is documented as sanitised. Recorded in the Parking Lot as a security item for Step 29.
- 14 Parking Lot items and 15 `TEST_DEBT.md` rows recorded.

## Verification

- `dotnet build src/backend/KlaraHome.sln` — **0 errors, 0 warnings** (sprint rule 3).
- `tools/ef.ps1 add Catalog InitialCatalogSchema` — generated; the migrator project compiles with it.
  Whether it *applies* against a live database is Step 28A's question (sprint rule 4).
- Unit tests: **508 total, 0 failed** — 449 before this step, 59 new.
- Architecture tests: **14 total, 0 failed** — the module-boundary, schema-ownership, internal
  `DbContext` and Domain-layer-purity rules all hold for the new module.

