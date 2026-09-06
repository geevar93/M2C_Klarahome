# Architecture Decision Records

One file per decision, named `ADR-<nnn>-<slug>.md`. A record is never edited once accepted:
a decision that changes gets a **new** ADR that supersedes the old one, and the old one is
marked `Superseded by ADR-<nnn>`.

The initial set **ADR-001 – ADR-010** is tabulated in
[`../01-architecture.md` §9](../01-architecture.md) and accepted there. Records from **ADR-011**
onward are written here as full files, at the step that raises them.

| ADR | Decision | Status | Raised at |
|---|---|---|---|
| 001–010 | See `01-architecture.md` §9 | Accepted | Step 0 |
| [011](ADR-011-fluentassertions-7-pin.md) | Pin FluentAssertions to the 7.x line | ❌ Rejected — superseded by 012 | Step 3 |
| [012](ADR-012-no-assertion-library.md) | No third-party assertion library; use xUnit's built-in `Assert` | ✅ Accepted | Step 3 |
| [013](ADR-013-one-outbox-in-the-platform-schema.md) | One outbox table in the `platform` schema, written by every module in its own transaction | ✅ Accepted | Step 4 |
| [014](ADR-014-external-identity-providers.md) | External identity providers for customers; SMS and email features deferred behind runtime flags | ✅ Accepted | Step 7 boundary |
| [015](ADR-015-pdfsharp-migradoc-for-documents.md) | PDFsharp + MigraDoc (MIT) for generated documents, not QuestPDF | ✅ Accepted | Step 8 |
| [016](ADR-016-media-is-its-own-module.md) | Media is its own module, with its own `media` schema | ✅ Accepted | Step 8 |
| [017](ADR-017-unprovisioned-channels-are-suppressed.md) | A notification channel with no provider is suppressed, not failed | ✅ Accepted | Step 8 |
| [018](ADR-018-shiprocket-and-delivery-coverage.md) | Shiprocket is the named v1 courier, adapters are keyed by courier, and delivery coverage is a policy rather than a zone | ✅ Accepted | Step 16 boundary |
| [019](ADR-019-postgresql-full-text-behind-a-search-engine-seam.md) | PostgreSQL full text answers queries, behind a seam a dedicated engine can take over | ✅ Accepted | Step 19 |
| [020](ADR-020-materialised-collections-and-declared-block-types.md) | A rule-based collection is materialised, and a block type is a declared schema | ✅ Accepted | Step 20 |
| [021](ADR-021-reporting-keeps-its-own-facts.md) | Reporting keeps its own facts; no materialised views and no rollups | ✅ Accepted | Step 21 |

> **Backlog:** ADR-001 – ADR-010 exist only as table rows. Expanding them into individual files
> is in `../PARKING_LOT.md`; it is documentation debt, not a decision
> gap — the decisions themselves are recorded and accepted.
