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
- **Outcome / Notes:**

  **Status: ✅ DONE — 2026-09-06.** Build acceptance met: everything under Deliverables is written,
  the solution builds at **0 warnings**, the endpoints are registered and declare their permissions,
  the module is referenced by all three hosts, and the migrator compiles with two new migrations.
  808 unit tests (52 new) and 14 architecture tests green.

  ### What was built

  **`KlaraHome.Modules.Search`, schema `search`, four tables.**

  `product_search_projection` — **one row per variant, carrying the offer that won its buy box.** Not
  one row per listing: a marketplace shows one offer per sellable thing, and a result set with four
  rows for the same cushion is the failure mode the shape exists to prevent. `search_vector` is a
  **generated, stored** column with four weights — A the product name, B the brand, variant name and
  SKU, C the category, D the summary and searchable attribute labels — so there is no write path,
  bulk rebuild included, that can leave it stale. Five index kinds carry the read paths: GIN on the
  vector, GIN on `attributes` for containment, GIN on `category_ids` for subtree browse, GIN trigram
  on `product_name` for the fuzzy pass, and the btrees `03-database-design.md` §5 names.

  `search_synonyms` and `search_stop_words` — the store's own vocabulary, **applied when a query is
  parsed rather than when a document is indexed**. PostgreSQL can hold a synonym dictionary inside a
  text-search configuration and it is faster, but every change to one needs a file on the database
  server and a reindex of the whole table. Expanding the query instead costs one `OR` per term and
  means a merchandiser who notices that nobody finds "settee" fixes it from a screen.

  `search_queries` — **the only original record in the schema**, partitioned monthly and retained a
  year. Everything else here is a copy of the catalogue and can be rebuilt from it; what a shopper
  typed exists nowhere else, and the queries that returned nothing are the most valuable rows in the
  table.

  ### The four properties that hold the rest together

  1. **The buy box is resolved by the module that owns the rule.** `IProductProjectionSource` — the
     one new shared contract — answers with every offer for a variant and marks the winner, using the
     same `BuyBoxResolver` and the same configured settings the product page uses. A consumer that
     picked its own winner (the cheapest, say) would eventually link a search result to a page
     showing a different seller at a different price, and nobody could say which was wrong.
  2. **Facet counts are computed with each facet's own filter lifted.** Predicates are labelled with
     the dimension they narrow, and a facet's branch applies every predicate except its own. It is
     the only definition that agrees with what happens when a shopper clicks a facet: somebody who
     has chosen beige still has to be told how many creams there are, *within* the price band they
     also chose. Two filtered attributes get a branch each, because "how many creams in size M" and
     "how many size Ls in beige" are two different result sets. Category is the deliberate exception
     — it is a drill-down, not a multi-select, so it keeps its own filter.
  3. **Every write is an upsert keyed on the variant.** That is what makes at-least-once event
     delivery and a concurrent rebuild safe on the same table, enforced by a unique index rather than
     by timing.
  4. **Ranking is a weighted sum of four normalised terms**, and the weights are settings rather than
     configuration. Popularity is `log(1 + units)` damped again by `x / (1 + x)`, so four orders of
     magnitude in sales is under four times the score — sales follow a power law, and a raw count as
     a ranking term would mean nothing but that one product ever appeared first.

  ### Keeping it current

  Six events, and the split between them is a performance decision worth stating. `ListingPublished`,
  `ListingUpdated`, `ListingDeactivated` and `PriceChanged` **re-resolve the variant from the
  catalogue**, because each of them can change *which offer wins* — a price is one of the criteria
  the rule ranks on, and a withdrawal removes a candidate. `StockLevelChanged` deliberately does not:
  the buy-box rule treats availability as unknown rather than as a criterion, so the highest-volume
  event in the platform costs one `UPDATE` of two columns instead of a five-table read.

  `SubOrderConfirmed` is the one handler that is not naturally idempotent — units sold is a counter —
  so it writes an inbox row in the same `SaveChanges` as the increment. **This is the first use of
  `platform.inbox_messages`**, which has existed since Step 4 and had no consumer.

  Seller life-cycle events are **deliberately not** subscribed to. Catalog already reacts to a
  suspension by withdrawing that seller's listings, and each withdrawal publishes
  `ListingDeactivated` — subscribing here as well would do the work twice, from a module that cannot
  see whether the listings were actually withdrawn.

  ### The engine seam (ADR-019)

  `ISearchEngine` is shaped by what a storefront asks, not by what an engine offers: it takes a fully
  parsed `SearchRequest`, and there is deliberately **no** method accepting a raw expression in an
  engine's own dialect — the moment one exists the storefront starts sending Lucene syntax and the
  seam stops being a seam. `Search:Provider` says what a deployment has; `search.external-engine`
  says whether to use it; every path that cannot be honoured falls back to PostgreSQL with a log line
  naming the reason. Unlike the payment, courier and payout adapters, there is **no degraded mode**:
  PostgreSQL needs no credentials, so search works in every deployment on the day it is installed.

  ### Files and shape

  - `Domain/` — `ProductSearchDocument` (+ `SearchDocumentContent`), `SearchSynonym`,
    `SearchStopWord`, `SearchQueryLogEntry`.
  - `Application/` — `SearchContracts`, `SearchErrors`, and the features: product search, suggest and
    click, synonyms, stop words, the query report, the index.
  - `Infrastructure/Query/` — `SearchTextNormalizer` (a pure function over text and vocabulary),
    `SearchVocabularySnapshot` / `Cache` / `Reader`.
  - `Infrastructure/Engine/` — `ISearchEngine`, `SearchSqlBuilder`, `PostgresSearchEngine`,
    `SearchEngineRegistry`.
  - `Infrastructure/Projection/` — `SearchProjectionWriter` (the single place a row is written),
    `SearchIndexService`.
  - `Infrastructure/Events`, `Logging`, `Jobs`, `Features`, `Persistence` — the six handlers, the
    query recorder, the reindex sweep, the four flags, the context and two migrations.
  - `Endpoints/` — three storefront routes, eight admin routes, three permissions.

  ### Deliberate deviations

  - **Raw SQL, read over the EF context's own connection.** The statement's shape depends on which
    filters were asked for, and it uses `tsvector` containment, `ts_rank_cd`, trigram similarity,
    lateral `jsonb` expansion and a ten-branch union — none of which EF can express, and all of which
    are why the search is fast. `01-architecture.md` §4 anticipates exactly this. Nothing a caller
    supplied is ever concatenated: the shape is generated, every value is a parameter, and a unit
    test asserts it.
  - **`category_ids uuid[]`**, derived from the materialised path and GIN-indexed, because neither
    alternative works — a prefix match needs the caller to know the ancestors, a substring match
    cannot use an index. A specification change, recorded.
  - **`search_stop_words`**, which `03-database-design.md` §4.13 did not name but the deliverable
    asked for. A specification change, recorded.
  - **Phrase synonyms are refused.** Half-supporting one would make a rule that appears to work and
    quietly means something else.
  - **The vocabulary cache is per-process with a sixty-second time to live**, not a distributed
    invalidation.

  ### Known gaps

  - **Nothing is proved against a database.** Every claim above about SQL that runs, indexes that are
    used, facet counts that add up and partitions that route is asserted in shape only —
    26 `TEST_DEBT.md` rows, including both headline full-acceptance criteria.
  - **Ratings are always null.** The projection copies them from the catalogue, which has no reviews
    until Step 21, so the rating facet and the rating sort are wired and empty. Step 21 publishes
    `Reviews.ReviewPublished` and will need to subscribe this module to it.
  - **The index starts empty.** A deployment installing this over an existing catalogue has no rows
    until `POST /admin/search/index/rebuild` is called; the sweep only refreshes rows that already
    exist. Raised for Step 28A.
  - **Popularity is never decremented** by a cancellation or a return. A ranking hint rather than a
    ledger, and the score is logarithmic — but it is a decision, not an oversight.
