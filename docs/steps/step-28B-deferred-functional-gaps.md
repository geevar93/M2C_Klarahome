# Step 28B — Deferred functional gaps from the build sprint

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** H · **Depends on:** Step 28A
- **Objective:** Build the functional gaps the build sprint recorded and never had a step to build
  in, so that no open item in [`PARKING_LOT.md`](../PARKING_LOT.md) is waiting on a step that does
  not exist.
- **Why it exists:** Steps 9–28 each parked what fell outside their own deliverables, which is the
  protocol working as intended (§6, no scope creep). But **every remaining step is a hardening
  step** — 29 is tests, 30 is aesthetics, 31 is operations, 32 is deployment, 33 is UAT — so a
  parked *feature* had nowhere to land, and several were declined by omission two or three times
  running. Three of them block a genuine first-run path: a store cannot change its shipping
  prices, a purchase order cannot be raised because no supplier can be added, and support has no
  way to reproduce a customer's problem. This step gives all of them one owner.

  It sits **before Step 29** deliberately. Step 29 works `TEST_DEBT.md` top to bottom; landing
  features after it would reopen the ledger it had just closed.

---

## Deliverables

### A. API contract gaps

These are backend changes. Each was found by a frontend step that could not proceed without it and
worked around it instead.

1. **Impersonation.** `07-security-compliance.md` §2 requires time-boxed, reason-carrying, audited
   support impersonation, and it has been parked since **Step 7** and named as a deliverable of
   **Step 26**. The API has no impersonation endpoint, no permission and no session flag. Build the
   endpoint, the permission, the audit entry and the session marker, then the banner and the exit
   control in the admin shell that Step 26 left unbuilt.
2. **Guest checkout.** `/store/checkout` carries `.RequireAuthorization()` on the whole group and
   `StartCheckoutCommand` refuses an anonymous caller, so Step 25 could not build it. Decide
   whether `CartsOptions.RequireSignInToCheckout` is a real switch; if it is, the group's
   authorization has to move off the whole group and onto the endpoints that need it.
3. **Reference data on the admin surface.** Four screens ask an operator to type a raw `stateId`
   (warehouses, pickup addresses, serviceable regions, the promotion simulator's place of supply)
   because the admin client has no endpoint listing the platform's states. Expose the Step 6
   reference data on the admin surface, or accept a state code on those endpoints.
4. **`GET /admin/reports/{key}` declares two 200 bodies.** `format=json` answers `ReportResult`,
   `format=csv` answers `ReportRunResponse`; OpenAPI carries one body per status, so the generator
   never emitted `ReportResult` at all and `ReportingAdminService.table()` casts through `unknown`.
   Split it into two operations, or declare the JSON body and move the CSV one.
5. **`VendorResponse` carries no `nextStatuses`.** Onboarding is six separate endpoints rather than
   one transition table, so `vendor-vocabulary.ts` holds the only client-side copy of a server rule
   left in the back office. Give a vendor its allowed transitions, as an order, a return and a
   payout batch already do, and delete the copy.
6. **`GET /store/products` declares no query parameters.** The endpoint reads its filters off the
   request by prefix because attribute facets are open-ended, so the generated client takes no
   query at all and the storefront listing uses a documented transport escape hatch. Declare the
   closed half — `q`, `sort`, `page`, `size`, price and rating bounds — and leave the open half
   undeclared.
7. **Signed URLs for the two streamed documents.** `GET /admin/shipments/{id}/label` and
   `GET /admin/products/import/template` answer bytes behind a bearer token, and the access token
   lives in memory only, so a plain link downloads a 401 page. `DocumentPrintService` works around
   it. Make both answer a short-lived signed URL, as `GET /admin/invoices/{id}/download` does, and
   delete the workaround.
8. **Bulk product status.** The products screen's bulk action fans out one request per selected
   row — forty rows is forty requests. Add `POST /admin/products/bulk-status`.
9. **Add/remove for a collection's pinned items.** `POST /admin/collections/{id}/items` replaces
   the whole pinned set and the screen sends back only the fifty rows it has on screen, so pinning
   a fifty-first product would drop the rest.
10. **`GET /admin/settings/schema`.** The store-settings screen walks the JSON it was returned and
    renders a control per leaf, so it knows a field is a number but not that the number must be
    positive. Serve the schema the validators already encode, the way the CMS block types do.
11. **Type the client vocabularies.** Nineteen enumerations are hard-coded in the admin client
    across `promotion-vocabulary.ts`, `content-vocabulary.ts` and `vendor-vocabulary.ts` — QC
    dispositions, NDR actions, status filter lists, promotion type/appliesTo/stacking, price-list
    type, `prepaid`/`cod`, page type and status, banner placement and audience, menu link type,
    collection kind/rule field/rule operator/sort, settlement-cycle and payout-batch status,
    ledger entry type and the redirect status codes. The seller vocabularies show what the rest
    should look like: they are real enums in the generated client and therefore compiler-checked.
12. **Warehouse scope on the fulfilment queue and the pick list.** `GET /admin/sub-orders` filters
    by status and vendor, and `PickListLineResponse` carries no warehouse. A single-warehouse
    deployment is unaffected; **a second warehouse makes the pick list wrong for both pickers.**

### B. Back-office screens

13. **A supplier screen.** `InventoryAdminService` already covers create, update and list, and the
    purchase-order drawer reads the list — there is simply no way to add one, and **a purchase
    order cannot be raised until a supplier exists.** Declined by omission at Steps 27 and 28.
14. **A shipping rate-card editor.** `ShippingZonesService` covers `createRate` and `updateRate`
    and no screen calls them, so **a store cannot change its shipping prices from the back
    office** — rates have to be seeded.
15. **An entity picker in `ui-admin`.** Seven places ask for a raw identifier: a promotion's seller
    and listing scope, price-list rows, a collection's pinned product, a seller's staff user, a
    ledger adjustment's seller, and the report and ledger seller filters. The endpoints all search
    already; what is missing is one typeahead component that yields an id.
16. **A schema-driven repeater for CMS block items.** `BlockTypeResponse.itemFields` declares the
    schema of a repeated child — an FAQ's questions, a grid's tiles — and the composer renders a
    JSON textarea for it. It is the rendering problem the field controls already solve, one level
    down.
17. **Open a stock item from the stock screen.** `POST /admin/stock/open` exists and nothing calls
    it, so a listing that will never be purchased in has no way to start being tracked.
18. **A replacement return can create its replacement order.** `POST /admin/returns/{id}/replace`
    takes a `replacementOrderId` and the back office has no way to place an order, so it is sent
    null and the replacement goes untracked. Needs either an admin order-placement endpoint or a
    replacement flow inside the returns module.

### C. Storefront gaps

19. **Banners and the announcement bar.** Built in the CMS at Step 20 and rendered nowhere. Carried
    across Steps 23, 24 and 25, each of which named the next step as the owner.
20. **Asking a question and writing a review.** Step 24 deferred both to Step 25 because they need
    a signed-in customer; Step 25 built the account surface and not these. One form and one
    endpoint each — `storeAskQuestion` and `storeWriteReview` are already in the generated client.

### D. Money and settlement gaps

21. **`IVendorPayoutAccounts`.** Step 9 left the seam with a no-op and said Step 18 would fill it;
    Step 18 did not. No seller has a `gateway_account_id`, so **every Route payout item would be
    recorded `Skipped`** with "the seller has no payout account at the gateway". The implementation
    belongs inside the Vendors module, which owns the data.
22. **`transfer.processed` / `transfer.failed` webhooks.** The Payments module owns the Razorpay
    webhook endpoint and does not know these event types, so a payout's outcome is learnt only by
    the fifteen-minute reconciliation sweep.
23. **The platform's own commission invoice.** The `platform_tax` ledger entry records the GST the
    platform charged a seller and no document is produced, so **the seller cannot claim input
    credit against it.** A marketplace is required to raise this invoice. The numbers exist; the
    document does not.

### E. One internal refactor

24. **A shared claim helper for `FOR UPDATE SKIP LOCKED`.** Step 28A found ten loops that had each
    written `SELECT *` where the model needs `SELECT *, xmin`, and fixed all ten. A helper that
    owns the claim would make an eleventh impossible; it is a small refactor across five modules.

---

## Explicitly out of scope

- **No aesthetics** (rule 8 still holds until Step 30).
- **Nothing that needs a third-party credential.** The Razorpay, Shiprocket and payout credentials
  are Step 32's, and items 21 and 22 are built and left unproven exactly as Steps 15–18 were.
- **No test hardening.** Deliverable 24 is a refactor, not a test. Everything this step builds adds
  rows to [`TEST_DEBT.md`](../TEST_DEBT.md) for Step 29, on the same terms as the sprint.
- **The items the Parking Lot records as accepted decisions** rather than debts: the Facebook
  no-email constraint, `FeatureRollout.Segments`, the delivery-coverage operator override
  (ADR-018), the sitemap's variant walk, and the daily-take-rate commission approximation
  (ADR-021). Each names why it is accepted; none is waiting on a step.

---

## Acceptance criteria

1. Every deliverable above is built, or explicitly declined by the User and re-parked with the
   decision recorded.
2. `pwsh tools/ci.ps1 -Stage build`, `-Stage format`, `-Stage lint`, `-Stage codegen` and
   `-Stage frontend` all pass; the stack still boots and the Step 28A smoke walk still completes.
3. The generated API client is regenerated and committed for every contract change, and the
   `codegen` gate proves it.
4. Every client-side vocabulary deleted by deliverable 11 is replaced by a generated enum, and none
   is reintroduced.
5. **No open row in [`PARKING_LOT.md`](../PARKING_LOT.md) names a closed step as its owner, or no
   owner at all.**
6. Every deferred test is recorded in [`TEST_DEBT.md`](../TEST_DEBT.md).

---

- **Outcome / Notes:** _(to be filled on completion)_
