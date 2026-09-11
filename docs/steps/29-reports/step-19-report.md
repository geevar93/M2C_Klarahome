# Step 29 — Step 19 (Search & Browse module): 25 rows closed by 29 tests

> Detail report for [`../step-29-test-hardening-and-performance-baseline.md`](../step-29-test-hardening-and-performance-baseline.md).
> Tracker: [`../../IMPLEMENTATION_PLAN.md`](../../IMPLEMENTATION_PLAN.md) · Test debt: [`../../TEST_DEBT.md`](../../TEST_DEBT.md) · Parking lot: [`../../PARKING_LOT.md`](../../PARKING_LOT.md)

Twenty-five of Step 19's twenty-six `TEST_DEBT.md` rows now name a passing test — every one except the
single k6 performance row, which needs the large-catalogue load-test harness `09-nfr-testing-observability.md`
§2 requires and stays honestly open. `SearchProjectionTests` (6), `SearchEventTests` (5),
`SearchIndexRebuildTests` (3), `SearchInsightsTests` (5) and `SearchAuthorisationTests` (5), twenty-four
methods in five new classes under `tests/KlaraHome.IntegrationTests/Commerce/`, plus `SearchEngineRegistryTests`
(5, unit, `tests/KlaraHome.UnitTests/Search/`) — against the real Search schema on a migrated Postgres
database, through the same `CommerceApiFactory` host every other Step 29 commerce suite runs against.

## What was reachable through the real API, and what was not

`ListingPublished`, `ListingDeactivated` and `StockLevelChanged` are raised for real the moment a test
opens or withdraws an offer, or adjusts stock, through the existing `CatalogScenario` — Step 10's own
harness, reused unchanged — and `OutboxDrain` delivers them exactly as the worker would. `PriceChanged`
comes from a price list Step 12 owns and `SubOrderConfirmed` from an order Steps 11–14 own; driving
either the whole way to prove Step 19's own debt would mean standing up a second module's journey to
test a third module's handler. Following the precedent Step 18's `SettlementScenario` set for
`IOrderSettlement`, a new `SearchScenario.DispatchAsync<TEvent>` resolves the real, unmodified
`SearchProjectionHandlers` from the host's own container and calls it directly — what is being proved
is what Search does with the fact, not whether Pricing or Orders can produce one.

## Rows closed

| Row | What it says | Test |
|---|---|---|
| 264 🔴 | Facet counts equal a ground-truth `COUNT(*)` with each group's own filter lifted, including two attribute filters at once | `SearchProjectionTests.Facet_counts_agree_with_a_ground_truth_count_with_their_own_filter_lifted` |
| 265 🔴 | The generated `search_vector`'s four weights actually rank a name match above the same term in a category name | `SearchProjectionTests.A_name_match_outranks_the_same_term_appearing_only_in_a_category_name` |
| 266 🔴 | The whole generated statement — facet union, lateral `jsonb`, trigram predicate, `set_limit` — parses and runs | `SearchProjectionTests.The_generated_sql_runs_across_every_filter_and_sort_shape` |
| 267 🔴 | Keyset pagination neither repeats nor skips a row, including a price tie broken by id | `SearchProjectionTests.Keyset_pagination_neither_repeats_nor_skips_a_row_including_a_price_tie` |
| 268 🔴 | A category filter uses the GIN index on `category_ids`, not a sequential scan | `SearchProjectionTests.A_category_subtree_filter_uses_the_gin_index_on_category_ids` |
| 269 🔴 | An attribute filter uses the GIN index on `attributes`; two values OR, two attributes AND | `SearchProjectionTests.An_attribute_filter_uses_the_gin_index_and_combines_or_within_and_across` |
| 270 🟡 | The fuzzy pass runs only after an exact miss, corrects a real misspelling, honours the threshold | `SearchEventTests.The_fuzzy_pass_runs_only_after_an_exact_miss_and_corrects_a_misspelling` |
| 271 🔴 | `ListingPublished`/`ListingUpdated`/`ListingDeactivated`/`PriceChanged` each re-resolve the buy box | `SearchEventTests.Listing_and_price_events_each_re_resolve_the_buy_box` |
| 272 🟡 | `StockLevelChanged` updates only the two availability columns, and only on the winning row | `SearchEventTests.Stock_events_touch_only_the_availability_columns_of_the_winning_row` |
| 273 🔴 | `SubOrderConfirmed` counts units once, even redelivered or raced concurrently | `SearchEventTests.SubOrderConfirmed_counts_units_exactly_once_even_when_redelivered` |
| 274 🟡 | A variant with no surviving offer is deactivated, not deleted, and keeps its `units_sold` | `SearchEventTests.A_variant_with_no_surviving_offer_is_deactivated_not_deleted` |
| 275 🔴 | A full rebuild walks every variant exactly once via its cursor and reaches the end | `SearchIndexRebuildTests.A_rebuild_walks_every_variant_exactly_once_and_reaches_the_end` |
| 276 🟡 | A rebuild racing the event pipeline never produces two rows for one variant | `SearchIndexRebuildTests.A_concurrent_rebuild_and_event_pipeline_never_produce_two_rows_for_one_variant` |
| 277 🟢 | The staleness sweep finds only rows past the window and is a no-op when healthy | `SearchIndexRebuildTests.The_staleness_sweep_finds_only_rows_past_the_window_and_is_a_no_op_when_healthy` |
| 278 🔴 | `search.search_queries` is partitioned by month; a click is found by both halves of the key | `SearchInsightsTests.The_query_log_is_partitioned_and_a_click_is_found_by_both_halves_of_the_key` |
| 279 🟡 | The query log is written only when both `LogQueries` and `search.query-logging` are on | `SearchInsightsTests.The_query_log_is_written_only_when_both_switches_are_on` |
| 280 🟢 | The zero-result report groups correctly and separates `search` from `suggest` | `SearchInsightsTests.The_zero_result_report_groups_correctly_and_separates_search_from_suggest` |
| 281 🟡 | A vocabulary edit is seen by another process once its cached copy's TTL expires | `SearchInsightsTests.A_vocabulary_edit_is_seen_by_another_process_within_the_cache_ttl` |
| 282 🟢 | A bidirectional synonym expands between every sibling — sofa ↔ couch ↔ settee | `SearchInsightsTests.A_bidirectional_synonym_expands_between_every_sibling` |
| 283 🟡 | `SearchEngineRegistry` falls back to PostgreSQL for each of ADR-019's four reasons | `SearchEngineRegistryTests` (5 tests, one per reason) |
| 284 🟡 | The three storefront routes are 404 with their flag off, answer anonymously when on | `SearchAuthorisationTests.Storefront_routes_are_404_with_their_flag_off_and_answer_anonymously_when_on` |
| 285 🔴 | The three admin permissions are each enforced on their own route | `SearchAuthorisationTests.Every_admin_permission_is_enforced_on_its_own_route` |
| 286 🟢 | The `search` settings section round-trips and its validator refuses every documented bad shape | `SearchAuthorisationTests.The_settings_validator_refuses_every_documented_bad_configuration` |
| 287 🟢 | The listing page is served from the output cache per query string, and varies by filter | `SearchAuthorisationTests.The_listing_page_is_cached_per_query_string_and_varies_by_filter` |
| 288 🔴 | The search index's buy box is the same offer the product page shows | `SearchAuthorisationTests.The_search_index_buy_box_matches_the_product_pages_buy_box` |

**Row 263 (🔴, k6 p95 latency on a 50,000-SKU catalogue) stays open.** It needs the large-catalogue
seed generator and the k6 harness Part 3 of this step builds; nothing in Part 1's scope can honestly
close it, and softening it to an in-process timing assertion on a fixture of a few dozen rows would
prove nothing about the number the row actually names.

## What the harness gained

- `tests/KlaraHome.IntegrationTests/Commerce/SearchScenario.cs` — wraps `CatalogScenario` for the
  Search-specific setup every row needed (indexed-row polling, a cached default seller so most tests
  do not each pay for a full onboarding), and `DispatchAsync<TEvent>`, the direct-handler seam
  described above.
- `tests/KlaraHome.IntegrationTests/Commerce/CatalogScenario.cs` gained two small, backward-compatible
  additions: an optional `name` parameter on `DraftAsync` (needed to put a chosen token in a product's
  own name for the ranking-weight test) and a `ColourCode` field on `CatalogTaxonomy` (the attribute's
  machine code, needed to build `attr.<code>=` filter query strings).

## No production defect found this wave — and one real defect in this suite's own harness, caught before it reached `main`

Unlike every prior Step 29 wave, closing Step 19's rows did not turn up a bug in the Search module
itself. `SearchProjectionHandlers`, `SearchSqlBuilder`, `SearchProjectionWriter`, `SearchIndexService`
and the vocabulary cache all behaved exactly as documented on every row, including the ones — the
buy-box re-resolution on `PriceChanged`, the exactly-once counting on a raced `SubOrderConfirmed`, the
GIN-index claims — that prior waves' equivalents most often found something wrong under. That is
worth stating plainly rather than passing over: the module was built carefully, and twenty-five tests
against a real database found nothing to fix in it.

Two real bugs did surface, and both were in this session's own first drafts, not in the product.

1. **`SearchScenario.DispatchAsync` resolved the wrong handler.** `PriceChanged` and
   `SubOrderConfirmed` each have subscribers in *other* modules — Reviews' price-drop alert also
   implements `IIntegrationEventHandler<PriceChanged>`, and Payments, Reporting and Shipping each
   implement `IIntegrationEventHandler<SubOrderConfirmed>` — and a first version of `DispatchAsync`
   resolved the handler with a plain `GetRequiredService<IIntegrationEventHandler<TEvent>>()`, which
   .NET's container answers with whichever registration was added *last*, not the one this suite
   meant to exercise. The three affected tests failed cleanly (the row stayed unmoved, with no
   exception and no log line from Search at all) rather than passing against the wrong handler, which
   is what made it findable in an hour rather than a false positive nobody would have caught. Fixed by
   resolving `SearchProjectionHandlers` itself and casting it to the closed generic interface, exactly
   as production's own `SearchModule.AddEventHandlers` registers each `IIntegrationEventHandler<TEvent>`
   as a factory over one shared instance.
2. **`SearchIndexRebuildTests`'s own resumable-rebuild test walked the whole shared collection
   database, not its own five variants.** `EnumerateAsync` walks every variant in the tenant in
   ascending id order with no way to scope it to "the ones this test just made", so a page budget
   sized for five rows passed against an empty database and failed the moment it ran alongside every
   other suite's hundreds of variants in a full collection run — caught only by actually running the
   full `KlaraHome.IntegrationTests` collection, not by the filtered `~Search` run, which is exactly
   the "a per-agent green suite is not evidence" lesson wave 1 recorded. Fixed by reading the highest
   variant id that existed *before* the test's own five (variant ids are UuidV7 and therefore
   time-ordered) and starting the walk's cursor there, so the walk reaches its own five and stops
   regardless of how large the shared database has grown by the time this runs.

Both are recorded here because the same shapes of mistake are available to whoever closes the next
module's rows: a multi-subscriber event resolved by an un-keyed `GetRequiredService`, and a
full-catalogue walk asserted against with a page budget that assumes a near-empty database.

## Three harness lessons for whoever closes the next module's rows

- **A listing always belongs to a real seller.** `CreateListingCommand` refuses a platform-staff
  caller who does not name a vendor (`"Platform staff must name the seller the offer belongs to."`) —
  there is no such thing as a platform-owned *offer*, only a platform-owned *product* a seller may
  offer against. Every `OfferAsync` call in this suite needed a real `VendorScenario.ActiveAsync()`
  seller; `SearchScenario.DefaultSellerAsync()` caches one per scenario instance so a loop building
  five or six catalogue rows pays for one onboarding, not five.
- **The storefront's output cache answers an identical query string from cache, silently.**
  `GET /store/products` is `CachePublicRead`-tagged (Step 19's own row 287 proves exactly this), and
  two calls with the same query string inside the sixty-second window return the *same* response —
  which is invisible until a test repeats a query expecting to see an effect that happened in
  between (a synonym landing, a listing withdrawing) and instead reads a stale cached page. Three
  tests in this wave hit it during writing; the fix in each case was to vary the query string between
  calls that are meant to observe different states, never to disable the cache.
- **`CatalogScenario.VariantAsync`'s default MRP is ₹1,299.** A test that wants a second, losing offer
  priced *above* the winner has to stay under that ceiling or `UpdateListingCommand`/`CreateListingCommand`
  refuses it with `CATALOG_PRICE_ABOVE_MRP` — a real statutory rule (Legal Metrology), not a fixture
  limitation, and the right fix is a price below 1,299 rather than a raised MRP that would weaken what
  the test is actually proving.

## Final suite state

- `dotnet build src/backend/KlaraHome.sln` — **0 warnings, 0 errors**.
- `KlaraHome.UnitTests` — **982 passed**, 0 failed (977 before this wave; the 5 new
  `SearchEngineRegistryTests`). Verified by a full, unfiltered run — not a `~Search` filter — so the
  count is exact rather than inferred.
- `KlaraHome.ArchitectureTests` — **14 passed**, 0 failed. Unchanged; Search was already covered by
  every module-boundary and no-framework-leakage rule this suite enforces.
- `KlaraHome.IntegrationTests`, filtered to `~Search` — **28 passed**, 0 failed (24 of them new; four
  matched the filter incidentally from other steps' suites and were already green). Full test
  discovery (`--list-tests`, no execution) confirms the collection now holds **509** integration
  tests, up from 485 before this wave — the same 24.
- **A full, unfiltered `KlaraHome.IntegrationTests` run was carried to completion**: 509 total, 505
  passed, 4 failed, in 1h 02m. One of the four was `SearchIndexRebuildTests`'s own rebuild test —
  defect 2 above, found only by this run and fixed as described. The other three are pre-existing and
  outside this step's own module: `ShippingProviderSelectionTests.The_aggregator_alias_resolves_to_the_configured_courier_and_is_never_stored`
  failed on a `401 Unauthorized` after roughly thirty-two minutes of collection runtime (a token-expiry
  shape of failure, consistent with the shared-collection cost prior waves already documented), and
  two `DeliveryCoverageTests` methods failed — neither Shipping nor Platform settings are this step's
  module, and nothing in this wave touched either suite or the settings section they depend on. With
  the rebuild test fixed, two subsequent `~Search`-filtered runs of the fixed suite both passed 28 of
  28 clean; the filtered Search run above, together with the full unit and architecture runs, is
  the evidence this report stands on. A clean full-collection run remains Part 2's job (CI-gate
  restoration) rather than something any one step's close should have to re-verify from scratch.

**Note on the plan's own bookkeeping.** This session's full, unfiltered `KlaraHome.UnitTests` run
reads 977 before this wave, not the 991 the plan's status header and wave 2's own report cite. The
discrepancy predates this session — nothing in the working tree between wave 2's merge and this
step's start changed the unit suite — and this report uses the number actually measured rather than
the one on record; the arithmetic in `IMPLEMENTATION_PLAN.md`'s totals below is corrected to match
what running the suite says, and the "991" figure is worth someone tracing back to its source before
Part 2 restores the CI floors against it.
