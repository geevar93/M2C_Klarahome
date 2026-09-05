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

> **Backlog:** ADR-001 – ADR-010 exist only as table rows. Expanding them into individual files
> is in the Parking Lot of `../IMPLEMENTATION_PLAN.md`; it is documentation debt, not a decision
> gap — the decisions themselves are recorded and accepted.
