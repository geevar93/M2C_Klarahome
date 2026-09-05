# ADR-013 — One outbox table, in the `platform` schema, written by every module

- **Status:** ✅ Accepted
- **Raised at:** Step 4 (Database foundation, EF Core & migration pipeline)
- **Supersedes:** —
- **Related:** ADR-002 (schema per module), ADR-003 (transactional outbox instead of a broker)

---

## Context

Two rules from `01-architecture.md` §2.1 point in opposite directions here.

1. **A module owns its own Postgres schema.** No cross-schema foreign keys, no cross-module
   table reads.
2. **Modules communicate through the transactional outbox.** An integration event must be
   written in the *same transaction* as the state change that produced it — that is the whole
   guarantee (ADR-003). If the event were written anywhere else, or at any other time, a
   committed order could exist with no `OrderConfirmed` ever announced, or an `OrderConfirmed`
   could be announced for an order that rolled back.

`03-database-design.md` §4.1 places `outbox_messages` and `inbox_messages` in the `platform`
schema. A module's own `DbContext` connects with one connection and one transaction, so writing
its queue row means writing into `platform` — from, say, the Catalog module's transaction.

The alternatives considered:

| Option | Why not |
|---|---|
| **A queue table per module schema** (`catalog.outbox_messages`, …) | Preserves the boundary rule literally, but contradicts the data-design spec, and the dispatcher then has to poll N tables and merge them in occurrence order to preserve causality across modules. The ordering problem is real: "stock reserved" and "order confirmed" would sit in different tables with no total order between them. |
| **A separate `DbContext` for the outbox** | The write would land in its own connection and its own transaction. That is precisely the failure the pattern exists to prevent — the row and its announcement could disagree. |
| **A dedicated `messaging` schema** | Defensible, and cleaner on paper than reusing `platform`. Rejected only because it deviates from the written spec for no behavioural gain, and the spec is the artefact people read. |

## Decision

**There is one outbox and one inbox, both in the `platform` schema, and every module's
`DbContext` maps them so it can append in its own transaction.**

The DDL is owned by exactly one context — the one whose `OwnsMessagingTables` is `true`, which
today is `PlatformDbContext`, the context that owns that schema. Every other module context maps
the same two tables with EF's `ExcludeFromMigrations()`, so the queue is **created once** and
**appended to by all**. A startup validator refuses to boot a host where zero or more than one
context claims ownership, because either would be discovered as a missing table or a duplicate
migration rather than as a configuration mistake.

## Why this is not a boundary violation

The rule in §2.1 forbids a module reading another module's **business** tables and joining across
schemas. The outbox is none of those things:

- it is **infrastructure**, defined in `KlaraHome.Infrastructure`, not in any module;
- it has **no foreign keys**, so it creates no coupling the schema separation was protecting;
- a module **only ever appends** to it — no module reads it, and no module reads another
  module's messages;
- the **only** reader is the dispatcher in the worker, which is a host, not a module.

Extraction into a service later — the reason the boundary rules exist — is unaffected: an
extracted module takes its own outbox rows with it, or is pointed at a broker behind the same
`IOutbox` interface, which is the upgrade path ADR-003 already anticipates.

## Consequences

- **Good:** one queue, one total order by `occurred_at`, one partial index to poll, one
  dispatcher. Causality across modules is preserved for free.
- **Good:** `IOutbox` is resolved per `DbContext`, so a handler cannot accidentally publish
  outside its own transaction — the wrong thing is not merely discouraged, it is unavailable.
- **Cost:** one sanctioned cross-schema write exists, and it has to be explained. This record is
  that explanation.
- **Cost:** a module that is extracted into its own service must be given its own queue at that
  point. That is a one-off migration, not ongoing friction.
- **Watch:** `ExcludeFromMigrations()` on every non-owning context is a convention that would fail
  quietly if forgotten — the second module's migration would try to create a table that already
  exists. It is applied centrally in `KlaraHomeDbContext`, not per module, so there is nothing for
  a module author to remember.
