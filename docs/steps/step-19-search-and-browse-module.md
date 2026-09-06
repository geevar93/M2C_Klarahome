# Step 19 — Search & Browse module

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** E · **Depends on:** Step 18
- **Objective:** Fast, faceted product discovery.
- **Deliverables:**
  - PostgreSQL full-text search (`tsvector`, weighted fields) + `pg_trgm` fuzzy matching,
    synonyms, stop words; ranking that blends relevance, availability and popularity.
  - Faceted filtering (category, brand, price bands, attributes, vendor, rating, discount,
    availability) with facet counts; sorting; keyset pagination.
  - Autocomplete/suggestions; zero-result handling and query logging for merchandising.
  - Denormalised search projection table kept current via the outbox/event pipeline.
  - Documented, feature-flagged upgrade path to a dedicated engine (Meilisearch / OpenSearch)
    if catalogue size or latency demands it.
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
- **Full acceptance criteria (verified at Step 29, not now):** p95 search latency within the NFR target on a seeded catalogue of
  50,000 SKUs; facet counts provably correct against SQL ground truth.
- **Outcome / Notes:** _(to be filled on completion)_
