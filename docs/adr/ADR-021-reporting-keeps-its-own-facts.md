# ADR-021 — Reporting keeps its own facts; there are no materialised views and no rollups

- **Status:** ✅ Accepted
- **Raised at:** Step 21, by the deliverable *"Reporting read-models & APIs"* and by
  `03-database-design.md` §4.18, which named seven materialised views
- **Supersedes:** the materialised-view sketch in `03-database-design.md` §4.18
- **Related:** ADR-019 (a projection is the expensive half, and it is not the engine's),
  ADR-020 (a rule-based collection is materialised because it cannot be queried live),
  ADR-013 (one outbox, written in the transaction of the fact it describes),
  ADR-016 (a `file_id` is a soft reference, resolved rather than joined)

---

## Context

`03-database-design.md` §4.18 described the reporting schema as *"materialised views refreshed on
schedule, plus rollup tables for anything needing history"*, and named seven: `mv_daily_sales`,
`mv_vendor_performance`, `mv_category_sales`, `mv_inventory_ageing`, `mv_return_analysis`,
`mv_conversion_funnel`, `mv_settlement_summary`. The same section says, two lines later, that
*"Reporting is read-only and may query only its own schema"*.

Those two sentences cannot both be true. A materialised view named `reporting.mv_daily_sales` that
computes anything useful has to select from `orders`, `payments` and `catalog`, and
`01-architecture.md` §2.1 is unambiguous:

> A module owns its **own Postgres schema**. No cross-schema foreign keys, no cross-module table
> reads. **Ever.**

A materialised view is a cross-module table read with a different word in front of it. It is
declared in DDL rather than in C#, so the architecture tests would not catch it, which makes it
worse than the violation the rule was written to prevent rather than better. It would also make the
reporting module's migration depend on the exact column names of six other modules' tables — so a
rename in `orders` would break a `reporting` migration, which is precisely the coupling the schema
boundary exists to remove.

Rule 5 of the same section already says what to do instead:

> Cross-module data needed for reads is **projected** into the reading module, never joined live.

That is what Search does (Step 19) and what Content does for collections (Step 20). The question
this ADR answers is not *whether* Reporting projects — that follows — but **what shape** the
projection takes, and specifically whether the module keeps facts, rollups, or both.

---

## Decision

**Reporting keeps one fact table per kind of business event, written by an integration-event
handler, denormalised at write time. It keeps no rollups and no materialised views. A report is a
filtered aggregation over exactly one fact table.**

Seven tables:

| Table | One row per | Written by |
|---|---|---|
| `fact_orders` | order | `OrderPlaced`, followed by status events |
| `fact_order_lines` | confirmed order line | `SubOrderConfirmed`, adjusted by cancellation, return, delivery and settlement |
| `fact_payments` | movement of money | `PaymentCaptured`, `RefundProcessed`, `CodCashRecorded` |
| `fact_return_lines` | returned line | `ReturnRequested`, followed by `ReturnClosed` |
| `fact_settlements` | closed settlement period | `SettlementCycleClosed` |
| `fact_funnel_events` | observable step of a shopper's journey | `CartAbandoned`, `CartConverted`, `OrderPlaced`, `PaymentCaptured`, `SubOrderConfirmed` |
| `fact_inventory_ageing` | stock line per day | the nightly snapshot, through `IInventoryAgeing` |

Every fact row carries the columns a report groups by — the category, the brand, the seller, the
payment method, the reason code — resolved once when the event lands and **frozen**. There is no
join anywhere in the module.

### Why no rollups

A rollup is precomputation, and precomputation is worth its cost when the underlying query is too
slow to run on demand. These queries are not. Each is a `GROUP BY` over one table filtered to a date
range on a stored `DateOnly` column with a covering index; for a single-VPS store this is
milliseconds, and it stays milliseconds for years of history.

Against that, a rollup layer costs three things that are not obvious:

1. **A second set of numbers to explain.** The day a rollup and a fact disagree — a refresh that
   half-ran, a late event, a bug in one of two aggregations — somebody has to work out which is
   right, and the answer is always "the facts". A layer whose output is only ever trusted when it
   agrees with the layer below it is not earning its place.
2. **A staleness window.** "Why is today's figure different from the one I looked at an hour ago"
   is the question a scheduled refresh generates, and it has no good answer.
3. **A back-fill problem.** A rollup schema that changes needs every historical row recomputed. A
   fact schema that changes needs a migration, and the facts are still there.

If the query cost ever becomes real, a rollup can be added over these facts *without* touching a
schema boundary, because the facts are already local. That is the option this decision keeps open,
and it is why the facts are the primary store rather than a staging area for the rollups.

### Why the facts are denormalised at write time

A report cuts sales by category, and a category is the catalogue's. The alternative to freezing it
on the row is resolving it at read time through `IProductProjectionSource`, once per row, on every
report. That is a contract call per line of history.

Freezing is also the more *correct* answer, not merely the faster one. A product moved to a
different category next year did not retrospectively sell in that category last March. A report that
re-resolved the category at read time would rewrite its own history every time a merchandiser tidied
the tree, and the store's March figures would stop matching the March report somebody printed.

### The one exception: stock ageing

Ageing cannot be assembled from events. `Inventory.StockLevelChanged` says what the balance *is*;
no sequence of those messages says when the units currently on the shelf arrived. That fact is in
Inventory's stock ledger.

So `IInventoryAgeing` is added to `KlaraHome.Contracts` and implemented by Inventory — a read-only
walk, keyset-paged — and a worker takes a snapshot once a day into `fact_inventory_ageing`. It is a
series rather than a state on purpose: *"how much stock is over ninety days old"* is a number a query
could compute at any moment, and *"is that getting better or worse"* is the question a buying team
actually asks.

### CSV exports do not go into the media library

A produced report is written to object storage under `reporting/exports/` in the private bucket and
served by a signed URL minted per request. It is deliberately *not* registered in `media.files` like
every other generated document on this platform.

The media library identifies what it accepts by sniffing the magic number of the bytes
(`FileInspector`), which is what stops the store serving executable content from its own domain.
CSV has no magic number. Teaching the library to accept a format it cannot recognise would weaken
that control for the benefit of one file type, so the export keeps its own storage prefix and its own
lifecycle instead.

---

## Consequences

**Accepted:**

- The fact tables are the largest write amplification in the platform: every confirmed order line
  produces a second row. That is the price of the schema boundary, and it is one row of about
  thirty small columns against a sale that has already written a dozen.
- **There is no back-fill.** The facts begin the day the module is deployed and the ingest is on.
  A store migrating from an existing platform has no history in these reports, and the
  `reporting.fact-ingest` flag is therefore the one flag in the platform that is *not* safe to leave
  off — a period with it disabled is a permanent hole. The flag exists only so an operator can keep
  the store selling if the ingest ever causes a problem under load, and it is documented as such.
- **Commission is apportioned rather than exact on the daily cut.** `SettlementCycleClosed` carries
  one commission figure for a whole cycle; the per-line rate is frozen in `orders` where this module
  may not read it. The cycle-level figure in the settlement summary is exact; the daily take rate in
  the GMV report divides it across the cycle's lines by value, which is exactly right for a flat
  commission plan and approximate for a per-category one. The approximation is named in the code and
  in `TEST_DEBT.md`.
- **The funnel starts at the basket.** "Viewed a product" needs a client-side analytics pipeline
  this system does not have, and inventing a number for it would be worse than admitting where the
  measurable journey begins.

**Rejected alternatives:**

- **Materialised views over other schemas.** Violates §2.1, is invisible to the architecture tests,
  and couples a `reporting` migration to six other modules' column names.
- **A read-only database role that may select across schemas.** The same violation with a permission
  system in front of it. It would also make "which module reads this table" unanswerable from the
  code, which is the property the boundary is worth having for.
- **Reporting reads through the existing contracts on every request.** `IOrderSettlement`,
  `IOrderReturns` and the rest are built for single-aggregate reads. A year of sales through them is
  a contract call per order, and the contracts would have to grow aggregate methods that are
  reporting's questions asked in somebody else's module.
- **One wide fact table.** A single `facts` table with a discriminator and forty nullable columns.
  Cheaper to write and unreadable to query; every report would carry a `WHERE kind = …` and half the
  columns would be null on every row.

---

## Specification changes this ADR carries

`03-database-design.md` §4.18 is rewritten from the seven materialised views to the seven fact
tables, with the reasoning above summarised in it. `02-domain-model.md` §6 gains the Reporting
consumer on the rows it now subscribes to. Both are recorded in `CHANGE_LOG.md`.
