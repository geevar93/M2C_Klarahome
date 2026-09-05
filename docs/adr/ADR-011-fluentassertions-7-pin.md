# ADR-011 — Pin FluentAssertions to the 7.x line

- **Status:** ❌ **REJECTED** by the User at the Step 3 boundary — superseded by
  [ADR-012](ADR-012-no-assertion-library.md)
- **Date:** 2026-09-05
- **Raised at:** Step 3 (backend solution skeleton)
- **Relates to:** ADR-009 (avoid commercially licensed libraries),
  `09-nfr-testing-observability.md` §2.1 (test tooling)

## Outcome

The User rejected this in favour of removing the third-party assertion dependency entirely.
The reasoning below is kept because it is the evidence that led there, and because it records
the two options that were weighed and set aside.

The decisive objection: pinning a version leaves the product depending on a package that has
already changed its licence once. **ADR-012** removes the dependency instead, so there is
nothing left to re-license. See ADR-012 for the option comparison, including a measured
migration cost for the fork.

---

## Context

`09-nfr-testing-observability.md` §2.1 names **FluentAssertions** as the assertion library for
unit and integration tests. ADR-009 separately decides that this product carries **no per-client
licence liability**, because it is redistributed to other businesses on their own infrastructure
— which is why MediatR and AutoMapper were rejected.

Verified against the feed while pinning packages for Step 3: FluentAssertions changed owner
(Xceed) and **version 8.0.0 onward requires a paid licence for commercial use**. The 7.x line
remains Apache-2.0.

So the two documents are in tension only at the version level, not at the library level. Taking
the latest version would create exactly the liability ADR-009 exists to prevent.

## Decision

Use **FluentAssertions, pinned to the 7.x line** (currently `7.2.2`, the last Apache-2.0
release). Do not upgrade to 8.x or later.

The pin is enforced in two places that a contributor will actually encounter:

- `src/backend/Directory.Packages.props` — the single `PackageVersion` entry, with the reason
  written next to it, under the same "licensing guard" comment as MediatR and AutoMapper.
- This ADR, linked from the guard comment.

## Consequences

**Accepted:**
- Test assertions stay free to redistribute. No client of this platform inherits a licence bill
  from the test suite.
- The 7.x line receives no new features. For an assertion library used only in tests, that is a
  cost close to zero.
- An automated dependency-update PR (Step 5) will eventually propose 8.x. **It must be
  rejected.** A CI guard on this specific package is in the Parking Lot.

**Rejected alternatives:**
- *Take FluentAssertions 8.x and buy licences.* Contradicts ADR-009 and puts a recurring,
  per-seat cost on a product meant to be handed to other businesses.
- *Switch to Shouldly or bare xUnit asserts.* Viable and free, but it contradicts a named choice
  in `09-nfr-testing-observability.md` §2.1 without a technical reason. If the User prefers to
  drop FluentAssertions entirely, that is a documentation change plus a mechanical rewrite of
  the assertions — a new ADR superseding this one.

## Reversal

Reversible at low cost while the test suite is small. The longer the suite grows on the
FluentAssertions syntax, the more mechanical work a switch becomes — which is the reason for
recording the decision now rather than at Step 29.
