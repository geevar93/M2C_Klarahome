# ADR-019 — PostgreSQL full text answers queries, behind a seam a dedicated engine can take over

- **Status:** ✅ Accepted
- **Raised at:** Step 19, by the deliverable *"documented, feature-flagged upgrade path to a
  dedicated engine (Meilisearch / OpenSearch) if catalogue size or latency demands it"*
- **Supersedes:** — (it makes ADR-007 operational rather than replacing it)
- **Related:** ADR-007 (PostgreSQL FTS before adopting a dedicated search engine),
  ADR-018 (adapters are keyed by the thing they are, and the key is configuration),
  ADR-017 (an unconfigured provider is suppressed, not failed)

---

## Context

ADR-007 decided at Step 0 that this platform would use PostgreSQL's own full-text search before
adopting a dedicated engine, and `01-architecture.md` §10 recorded the risk that goes with it —
*"Postgres FTS limits at large catalogue → slow search → projection table + measured thresholds;
Meilisearch swap behind a feature flag"*. Step 19 has to build the search, and therefore has to
decide what "behind a feature flag" actually means before there is anything to switch.

Two things make the decision less obvious than it looks.

**The projection is the expensive half, and it is not the engine's.** What makes a faceted listing
page fast is one denormalised row per variant, kept current by the event pipeline, holding the
words, the money, the availability and the buy-box winner. That table has to exist whichever engine
answers; a dedicated engine would be fed from it rather than instead of it. Treating "swap the
engine" as "swap the module" would throw away the part that took the work.

**A half-abstracted engine is worse than none.** The tempting seam is a method that takes a query
string in the engine's own dialect and passes it through. The moment one exists, the storefront
starts sending Lucene syntax, and the abstraction is decorative: swapping the engine then means
rewriting the storefront, which is the thing it was supposed to prevent.

There is also a smaller question with a real operational cost. A deployment can be *configured* for
a dedicated engine and still need it turned off in a hurry — because it is returning stale results,
because it is down, or because somebody typed a provider name into an environment file that has no
credentials behind it.

## Decision

### 1. The engine is behind `ISearchEngine`, which is shaped by the storefront, not by an engine

Two methods: answer a parsed search, and answer the autocomplete box. Both take a
`SearchRequest` — a fully parsed, fully bounded object carrying the normalised query, the filters,
the sort, the cursor and the store's ranking weights — and neither takes anything an engine would
recognise as its own syntax. There is deliberately **no** method that accepts a raw expression.

Everything an engine would otherwise have to know is done above it: reading the store's settings,
applying its synonyms and stop words, deciding to retry a failed search fuzzily, and writing the
query log. A second engine is therefore one class, not a second copy of this module.

Nothing on the interface writes. Keeping an index current is the projection's job and it happens
through the event pipeline, so an engine that has fallen behind is repaired by a rebuild rather than
by a write path in the query layer.

### 2. Adapters are keyed by the engine they are, exactly as couriers are (ADR-018)

`PostgresSearchEngine` publishes the key `postgres`. `Search:Provider` names which engine answers.
A second engine is a class, a registration line, that configuration value and a feature flag.

### 3. Two independent switches, and every failure falls back to PostgreSQL

`Search:Provider` is a deployment's statement about **what it has**. The `search.external-engine`
feature flag is an operator's statement about **whether to use it**. Both must be on for a
dedicated engine to answer.

`SearchEngineRegistry` falls back to PostgreSQL, with a log line naming the reason, when the
provider is blank, when the flag is off, when the key names an engine this build has no adapter
for, and when the named adapter reports itself unconfigured. A store whose search answers 502
because of a typo in an environment file is a far worse outcome than a store whose search is merely
not as fast as it could have been — the same judgement the payout and courier registries already
made.

### 4. PostgreSQL is never "the fallback engine" in the pejorative sense

It needs no credentials, no second container and no network hop, so — unlike the payment, courier
and payout adapters — there is **no degraded mode** in this module at all. Search works in every
deployment on the day it is installed, which is what ADR-007 was buying.

## Consequences

**Good.** The upgrade path is real and cheap, and it is testable before it is needed: the seam is
exercised by the only implementation there is, and the registry's fallback logic is the same code
path a real swap would take. The storefront cannot become coupled to an engine's dialect, because
there is no parameter through which it could. An operator can withdraw a misbehaving engine from
an admin screen in seconds.

**Bad.** The interface is narrower than a dedicated engine's own API, so a future engine's
distinctive features — vector similarity, learning-to-rank, engine-side personalisation — will not
be reachable without widening it. That is accepted: the day one of those is genuinely wanted is the
day for an ADR that widens the seam deliberately, rather than a seam left wide enough for it now
and coupled to by accident in the meantime.

**Bad.** Two switches is one more thing to get wrong, and the failure mode — a store running on
PostgreSQL when somebody believes it is running on something else — is silent apart from a log
line. `GET /admin/search/index` reports which engine actually answered, so the question is at least
answerable from a screen rather than from a log search.

**Neutral.** The thresholds at which a dedicated engine becomes worth it are not decided here.
`09-nfr-testing-observability.md` sets the target — p95 within 400 ms on 50,000 SKUs — and Step 29
measures it. This ADR only ensures that acting on the answer is a configuration change.
