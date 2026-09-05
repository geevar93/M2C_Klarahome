# ADR-012 — No third-party assertion library

- **Status:** ✅ Accepted — decided by the User at the Step 3 boundary
- **Date:** 2026-09-05
- **Raised at:** Step 3 (backend solution skeleton)
- **Supersedes:** [ADR-011](ADR-011-fluentassertions-7-pin.md) (rejected)
- **Relates to:** ADR-009 (avoid commercially licensed libraries),
  `09-nfr-testing-observability.md` §2.1 (test tooling)

## Context

`09-nfr-testing-observability.md` §2.1 originally named **FluentAssertions**. Version 8.0.0
moved to a commercial licence for commercial use, which collides with ADR-009: this platform is
redistributed to other businesses on their own infrastructure, so any per-seat licence in the
tree becomes a bill those businesses inherit.

Three options were evaluated with real measurements, not estimates.

| Option | Licence | Migration cost | Residual risk |
|---|---|---|---|
| **AwesomeAssertions** (Apache-2.0 fork of FluentAssertions 7, 20.6M downloads, credits the original authors) | Apache-2.0 | **Measured:** one `using` line per file, 14 files. All 108 tests passed unchanged, zero warnings | A third-party assertion library that could itself re-license |
| **Shouldly** | BSD-3-Clause | ~300 assertions rewritten; `because` reason strings have no direct equivalent | Independent lineage, but still a third party |
| **xUnit built-in `Assert`** | Apache-2.0, already a required dependency | ~300 assertions rewritten; more verbose collection assertions | **None** |

The fork was the cheapest option by a wide margin and was proven to work. It was still declined,
and the reason is the point of this record: the problem is not *this* licence change, it is
holding a dependency that can produce another one.

## Decision

**The test suite depends on no third-party assertion library.** Assertions use xUnit v3's
built-in `Assert`, which is Apache-2.0 and already required to run the tests at all.

`Directory.Packages.props` carries the guard next to the MediatR and AutoMapper entries, so the
next person to reach for an assertion package sees the reason before they add it.

## Consequences

**Accepted:**
- Nothing in the assertion path can ever impose a licence on this product or on a client
  deploying it. The exposure is removed, not relocated.
- One fewer package to track in the Step 5 dependency and CVE gates.
- Collection and exception assertions are more verbose. `Assert.Equal(expected, actual)` also
  reverses the argument order relative to the fluent form — a known source of confusion, which
  the compiler does not catch when both sides are the same type.
- FluentAssertions' `because` reason strings have no direct equivalent. Where the reason is
  **load-bearing** — the architecture tests, which encode *why* a boundary exists — it is
  preserved as the message argument of `Assert.True(condition, message)`. That turned out to be
  an improvement: the message now names the rule *and* lists the offending types, which the
  fluent form did not.

**Verified at the point of decision:** the full suite was migrated and re-run — 112 tests, all
passing — and one boundary rule was deliberately broken to confirm the rewritten assertions
still fail correctly and report usefully.

## Reversal

Cheap to reverse in either direction while the suite is small; the migration was mechanical in
both directions. It gets progressively more expensive as the suite grows, which is why this was
settled at Step 3 rather than at Step 29.
