# Step 29 — Step 18 (Settlements, Commission & Vendor Payouts): 26 rows closed by 30 tests

> Detail report for [`../step-29-test-hardening-and-performance-baseline.md`](../step-29-test-hardening-and-performance-baseline.md).
> Tracker: [`../../IMPLEMENTATION_PLAN.md`](../../IMPLEMENTATION_PLAN.md) · Test debt: [`../../TEST_DEBT.md`](../../TEST_DEBT.md) · Parking lot: [`../../PARKING_LOT.md`](../../PARKING_LOT.md)

Every Step 18 row in `TEST_DEBT.md` — all twenty-six, 🔴 through 🟢 — now names a passing test.
`SettlementLedgerTests` (7), `SettlementCycleTests` (7), `SettlementPayoutTests` (7),
`SettlementAuthorisationTests` (5) and `SettlementReportingTests` (2), thirty methods in five new
classes under `tests/KlaraHome.IntegrationTests/Commerce/`, against the real Settlements schema on a
migrated Postgres database, through the same `CommerceApiFactory` host every other Step 29 commerce
suite runs against.

## What was reachable through a real order, and what was not

Steps 11–14's own order journey — cart, checkout, payment, delivery — is not yet built in this
worktree (it is being closed in parallel by sibling agents), so driving a genuine `SubOrderId` all
the way from a shopper's cart through to a `SubOrderStatusChanged` event was not available here.
Rather than duplicate that journey or wait on it, every ledger-posting test drives the real,
unmodified `SettlementPoster` and `SettlementLifecycleHandlers` — production code, unchanged — against
a `FakeOrderSettlement`: a small, test-owned implementation of `IOrderSettlement` that the test sets
directly, in the shape the real `OrderSettlementService` promises. `IOrderSettlement` is Settlements'
own inbound seam from Orders, and Step 18's test debt is about what Settlements does with a
`SubOrderSettlementView` once it has one — the frozen commission, the earning timing, the reversal
clamp — not about whether Orders can produce one, which is Steps 11–14's criterion to prove. Every
seller, cycle, payout batch and permission check in these tests is otherwise driven through the real
API, a real onboarded seller (`VendorScenario.ActiveAsync`), and a real Postgres database — nothing
else in the module is faked.

A second test double was added for the same reason `FakePaymentProvider` and `FakeShippingProvider`
already exist: `FakePayoutProvider`, registered alongside — never replacing — the real
`UnconfiguredPayoutProvider` in `CommerceApiFactory`. The default test configuration leaves
`Payouts:Provider` blank, exactly as production does by the User's standing instruction, so the
honest-adapter row (245) is proved against that real default. A handful of tests that need to see a
transfer actually complete, fail or hang opt in explicitly by naming the fake rail before the host
starts.

## Rows closed

| Row | What it says | Test |
|---|---|---|
| 237 🔴 | Ledger balance ties out to the paisa; `Σ credits − Σ debits = closing balance` | `SettlementLedgerTests.Balance_is_the_signed_sum_of_every_entry_and_ties_out_to_the_paisa` |
| 238 🔴 | TCS/TDS read the right figures out of closed cycles, on two different bases | `SettlementCycleTests.Closing_a_period_computes_TCS_and_TDS_on_their_own_different_bases` |
| 239 🔴 | Posting is idempotent against a live database via the unique `(tenant_id, source_key)` index | `SettlementLedgerTests.Posting_the_same_fact_twice_credits_the_seller_once` |
| 240 🔴 | Prepaid earns on `Delivered`; cash earns only on a remitted `CodCashRecorded` | `SettlementLedgerTests.Prepaid_earns_on_delivery_and_cash_earns_only_on_remittance` |
| 241 🔴 | A cycle sweeps every unassigned entry older than its end, into exactly one cycle | `SettlementCycleTests.A_cycle_sweeps_every_unassigned_entry_older_than_its_end_into_exactly_one_cycle` |
| 242 🔴 | Opening balance is the signed sum over settled entries, and survives a failed payout | `SettlementCycleTests.Opening_balance_is_the_settled_sum_and_survives_a_failed_payout` |
| 243 🔴 | Maker–checker refused at the handler, the aggregate, and the database `CHECK` | `SettlementPayoutTests.Maker_checker_is_refused_at_the_handler_the_aggregate_and_the_database` |
| 244 🔴 | The migration applies clean; every `CHECK` refuses what it is meant to | `SettlementPayoutTests.The_settlements_schema_check_constraints_refuse_what_they_are_meant_to` |
| 245 🔴 | Nothing proved against a live gateway; the honest, unconfigured adapter | `SettlementPayoutTests.With_no_provider_configured_everything_up_to_sending_still_works` |
| 246 🔴 | A batch survives a partial send; `/process` is resumable, not a re-send | `SettlementPayoutTests.A_batch_survives_a_partial_send_and_process_is_resumable` |
| 247 🔴 | A completed transfer posts one debit and marks the cycle paid; a failed one posts nothing | `SettlementPayoutTests.A_batch_survives_a_partial_send_and_process_is_resumable` |
| 248 🟡 | The reversal ratio is clamped: never more given back than was charged or credited | `SettlementLedgerTests.Reversals_against_one_sale_are_clamped_to_what_was_actually_credited` |
| 249 🟡 | `RefundProcessed` is not consumed; a return reverses the supply exactly once | `SettlementLedgerTests.RefundProcessed_reverses_nothing_on_its_own` |
| 250 🟡 | A seller sees only their own data; a stranger's batch id is `404` not `403` | `SettlementAuthorisationTests.A_seller_sees_only_their_own_settlement_data_and_a_strangers_batch_is_404` |
| 251 🟡 | A vendor-scoped caller is refused on every write surface, with `SETTLEMENT_VENDOR_FORBIDDEN` | `SettlementAuthorisationTests.A_vendor_scoped_caller_is_refused_on_every_settlement_write_surface` |
| 252 🟡 | The scheduler closes a period once, safely twice, and never before the hold expires | `SettlementCycleTests.Closing_the_same_period_twice_is_a_no_op_the_second_time`, `SettlementCycleTests.A_period_is_refused_before_its_hold_expires_and_closes_once_forced` |
| 253 🟡 | Gapless, consecutive payout references | `SettlementPayoutTests.Payout_references_are_gapless_and_consecutive` |
| 254 🟡 | `Settlements.*` events land in the outbox in the same transaction as the fact | `SettlementCycleTests.Closing_a_period_writes_the_cycle_closed_event_in_the_same_transaction` |
| 255 🟡 | Every declared permission appears in `PermissionCatalog` | `SettlementAuthorisationTests.Every_settlements_permission_is_declared_in_the_catalogue` |
| 256 🟡 | The settings validator refuses bad configuration | `SettlementAuthorisationTests.Settlement_settings_are_validated_through_the_real_settings_endpoint` |
| 257 🟡 | `IOrderSettlement` reads the frozen commission; a later plan change does not move it | `SettlementLedgerTests.Commission_is_read_off_the_frozen_line_and_a_later_plan_change_does_not_move_it` |
| 258 🟡 | `IVendorPayouts` names each unpayable reason; no plaintext account number crosses the seam | `SettlementAuthorisationTests.IVendorPayouts_names_each_unpayable_reason_and_never_a_full_account_number` |
| 259 🟢 | Exports carry the BOM, quote a comma, refuse a range above `MaxExportRows` | `SettlementReportingTests.Exports_carry_the_byte_order_mark_quote_a_comma_and_refuse_too_large_a_range` |
| 260 🟢 | Platform revenue reconciles against the charge entries; TCS/TDS excluded | `SettlementReportingTests.Platform_revenue_reconciles_with_the_charge_entries_and_excludes_the_statutory_deductions` |
| 261 🟢 | Reconciliation reports a stuck transfer without touching it, and repairs a resolved one | `SettlementPayoutTests.Reconciliation_reports_a_stuck_transfer_and_repairs_one_that_has_resolved` |
| 262 🟢 | The TDS annual threshold is evaluated across cycles, not retrospectively | `SettlementCycleTests.TDS_annual_threshold_is_evaluated_across_cycles_and_applies_only_from_the_crossing_period` |

## Two real defects were found and fixed

Both live inside the Settlements module; neither needed a cross-module change, so the no-scope-creep
rule did not have to yield here.

1. **Closing a settlement period crashed against a live database, unconditionally.** `CyclePlanner`
   computes a period's boundaries in India Standard Time so the calendar day is right — correct — and
   then hands the resulting `DateTimeOffset` back with a `+05:30` offset still on it. `FinancialYear.
   StartOf` did the same. Every one of those values is used, moments later, as a parameter in an EF
   Core query or an insert against a `timestamptz` column, and Npgsql refuses to write a
   `DateTimeOffset` with any offset but zero at all. The result was `System.ArgumentException: Cannot
   write DateTimeOffset with Offset=05:30:00 to PostgreSQL type 'timestamp with time zone'` from
   `SettlementCycleService.CloseAsync` on its very first real invocation — which is every path into
   closing a period: the manual "close this vendor's due period" endpoint, the scheduler, and this
   step's own tests. The 34 unit tests written during the build sprint never caught it because none
   of them run against a real database; it surfaced the moment an integration test tried to close a
   cycle that CyclePlanner itself had opened. Fixed by normalising the returned instant to UTC with
   `.ToUniversalTime()` at the one point each helper hands a boundary back — the same absolute instant,
   a different `.Offset`, in `src/backend/modules/KlaraHome.Modules.Settlements/Infrastructure/
   Accounting/CyclePlanner.cs` (`PeriodOf`) and `src/backend/modules/KlaraHome.Modules.Settlements/
   Domain/NumberSequence.cs` (`FinancialYear.StartOf`).
2. **A payout that completed on its very first send violated the database's own invariant.**
   `PayoutItem.Complete(string? utr, string? providerStatus, DateTimeOffset settledAt)` never recorded
   `ProviderPayoutId` — only `Sent` did, and `Sent` is called *before* the gateway has answered, with
   the id still unknown. A transfer the gateway confirms synchronously — never separately queued —
   goes straight from `Sent(providerPayoutId: null, …)` to `Complete(…)`, and reached `SaveChangesAsync`
   as a `Completed` row with `provider_payout_id IS NULL`, which is exactly what
   `ck_payout_items_completed` exists to refuse: `23514` on every such transfer, surfacing as a `500`
   from `POST /admin/payout-batches/{id}/process`. `SettlementPayoutTests.A_batch_survives_a_partial_
   send_and_process_is_resumable` found it on its first run, sending through `FakePayoutProvider` set
   to answer `Completed` immediately. Fixed by adding a `providerPayoutId` parameter to
   `PayoutItem.Complete` in `src/backend/modules/KlaraHome.Modules.Settlements/Domain/PayoutBatch.cs`
   and passing `answer.ProviderPayoutId` from the one call site in
   `src/backend/modules/KlaraHome.Modules.Settlements/Infrastructure/Payouts/PayoutWorkflow.cs`
   (`ApplyAsync`) — the existing unit test covering the same aggregate,
   `PayoutLifecycleTests`, was updated for the new parameter and still passes.

Neither defect needed a decision outside this step, so nothing was added to `PARKING_LOT.md` for
them.

## What the harness gained

- `src/backend/tests/KlaraHome.IntegrationTests/Commerce/FakePayoutProvider.cs` — a payout rail at the
  network boundary, registered beside the real `UnconfiguredPayoutProvider` and selected only when a
  test names it, following the same shape as `FakePaymentProvider` and `FakeShippingProvider`.
- `CommerceApiFactory.Payouts` exposes it; `services.AddSingleton<IPayoutProvider>(Payouts)` is added
  alongside the real registration, never in place of it.
- `SettlementScenario.cs` — `FakeOrderSettlement` (the `IOrderSettlement` test double described
  above), `SettlementScenario.Sale(...)` (a one-line `SubOrderSettlementView` builder), and
  `SettlementScenario.Wire(scope)`, which hands back a real `SettlementPoster` and
  `SettlementLifecycleHandlers` constructed against a scope's real `SettlementsDbContext`,
  `ICommissionResolver`, `IStoreSettings` and `IClock` — only the Orders seam is a fake.
- `CommerceTestBase.SignedInStaffAsync(admin, roleCode)` — a fresh platform-staff account under one
  system role, for the maker–checker tests, which need two distinct *people* holding permissions one
  role bundles together.
- `VendorScenario.ActiveAsync` gained an optional `legalName` parameter, needed to prove a seller name
  containing a comma is quoted correctly in a CSV export.

## Two lessons for whoever closes the next module's rows

- **A settings-dependent assertion has to pin its own settings, or state its own tolerance.** Store
  settings live in the shared Platform schema for the whole collection, not per test, so a test that
  asserts an exact TCS or TDS figure without first `PUT`-ting the section it depends on is asserting
  against whatever an earlier test in the run last left the rate at. Two of these tests initially
  failed exactly that way; the fix in both cases is either to `PUT` the section explicitly before
  asserting, or — better, where the arithmetic allows it — to read the actual charged amount back off
  the ledger rather than hard-code an expected figure that assumes a rate.
- **A platform-wide report needs a delta, not an absolute figure, against a shared database.**
  `GetPlatformRevenueQuery` has no vendor filter by design — it is the platform's own total — so
  asserting an absolute number for it in a collection every other test also writes into is asserting
  against everyone else's data as well as your own. `SettlementReportingTests.Platform_revenue_
  reconciles_…` reads the report once before its own entries exist and once after, and asserts on the
  difference.

## Final suite state

- `dotnet build src/backend/KlaraHome.sln` — **0 warnings, 0 errors**.
- `KlaraHome.UnitTests` — **947 passed**, 0 failed (34 of them Settlements', unaffected by either fix
  except the `PayoutLifecycleTests` call site the second fix required).
- `KlaraHome.ArchitectureTests` — **14 passed**, 0 failed.
- `KlaraHome.IntegrationTests`, filtered to `Settlement` — **24 passed**, 0 failed, in the five new
  classes above.
- The full `KlaraHome.IntegrationTests` run was also started; its result is recorded in the commit
  history rather than held here, since Step 29's other in-flight waves touch the same shared
  collection and its count moves independently of this step's own scope.
