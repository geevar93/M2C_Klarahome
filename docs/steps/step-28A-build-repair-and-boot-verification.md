# Step 28A — Build repair & boot verification

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** H · **Depends on:** Step 28
- **Objective:** Take everything written during the MVP build sprint (Steps 9–28) and make it
  **compile, migrate, boot and answer** as one system. This is the first time the whole thing is
  assembled, and it is the step that ends the sprint.
- **Why it exists:** Steps 9–28 deliberately deferred verification
  (`IMPLEMENTATION_PLAN.md` §3). Twenty steps of independently-compiled modules will not
  necessarily start together: DI registrations collide, migrations conflict, options bind to
  nothing, module boundaries drift. Fixing that is a job, and it gets its own step so it is
  budgeted rather than discovered.

- **Deliverables:**
  - **Compiles clean.** `pwsh tools/ci.ps1 -Stage build` succeeds for the whole backend solution;
    `pwsh tools/ci.ps1 -Stage frontend` builds both Angular apps. Zero errors, and analyzer
    warnings triaged rather than suppressed.
  - **Format and lint pass.** `pwsh tools/ci.ps1 -Stage format` and `-Stage lint` are green.
  - **Migrations apply.** Every module's EF migration applies in order against a **fresh**
    database via the migrator container, and a second run is a no-op. Ordering conflicts,
    duplicate schema/table names and cross-module FK gaps resolved here.
  - **The stack boots.** `docker compose` brings up Traefik, Postgres, Redis, MinIO, imgproxy,
    Mailpit, the API, the worker and the migrator. Every health check is green; the worker drains
    its queues; no container is in a restart loop.
  - **The surface answers.** The OpenAPI document generates for both surfaces (`/store`, `/admin`)
    without duplicate operation ids or unresolvable schemas; the storefront and admin apps serve
    and reach the API.
  - **A smoke walk, by hand.** One pass through the MVP journey against the running stack — sign
    in, list a catalog page, add to cart, place an order, see it in admin. Done with `curl` or the
    browser, **not** as an automated test. Where it breaks, fix or record.
  - **CI floors restored.** `tools/ci.ps1` per-suite minimums updated to the real post-sprint
    counts, and the coverage minimum documented with the number Step 29 must restore it to.
  - **A repair record.** Every defect found and fixed here, listed in Outcome / Notes, so Step 29
    knows which areas were fragile.

- **Explicitly out of scope:**
  - **No new features.** Anything missing from Steps 9–28 is a gap to record, not to build. If a
    deliverable was genuinely not written, reopen that step with the User — do not fill it in here.
  - **No new tests.** A failing behaviour found by hand is fixed and written into
    [`TEST_DEBT.md`](../TEST_DEBT.md); the test that would have caught it is Step 29's.
  - **No aesthetics** (rule 8 still holds until Step 30).

- **Acceptance criteria:**
  1. `pwsh tools/ci.ps1 -Stage build`, `-Stage format`, `-Stage lint` and `-Stage frontend` all
     pass from a clean checkout.
  2. The migrator applies every migration against an empty database, and re-runs as a no-op.
  3. The full dev stack comes up with all health checks green and stays up.
  4. The MVP smoke walk completes end to end by hand.
  5. Every defect fixed is recorded in Outcome / Notes; everything not fixed is in
     [`PARKING_LOT.md`](../PARKING_LOT.md) or [`TEST_DEBT.md`](../TEST_DEBT.md) with an owner step.

- **Outcome / Notes:**

  **Status: complete.** All five acceptance criteria are met. The whole system was assembled for
  the first time, and it did not start: eight compiler and analyzer errors, three unbuildable
  images, an unreadable `.env.example`, two containers in restart loops, every background job
  failing on its first poll, and one endpoint answering 500. All of it is fixed and listed below.

  ### 1. Compiles clean

  `pwsh tools/ci.ps1 -Stage build` reported **8 errors, all in `KlaraHome.Modules.Payments`** —
  the module compiled during Step 15 and drifted afterwards under `-warnaserror`.

  | File | Diagnostic | Repair |
  |---|---|---|
  | `Application/Payments/AdminPaymentFeature.cs` | CS8619 | A ternary mixed `Result<ProviderPayment?>` with `Result<ProviderPayment>`. The two branches genuinely differ — a fetch by payment id answers a payment or an error, a fetch by order id answers a payment, no payment, or an error — so a `Widen` helper states that rather than hiding it |
  | `Application/Payments/AdminPaymentFeature.cs` | CA1826 | `FirstOrDefault()` on an `IReadOnlyList`; indexed instead |
  | `Infrastructure/Processing/GatewayEventProcessor.cs` | CA1826 | The same, on the webhook's order lookup |
  | `Infrastructure/Events/OrderLifecycleHandlers.cs` | CS9113 | `PaymentsEventPublisher events` was never read. It was the wrong dependency: `CashRecorded` means cash was *taken or remitted*, and this handler only expects and waives it. Removed, with its doc line |
  | `Infrastructure/Events/OrderLifecycleHandlers.cs` | CA1873 | `Status.ToString()` evaluated eagerly into a `LoggerMessage`; the parameter is now `RefundStatus` |
  | `Infrastructure/Refunds/RefundInitiationService.cs` | CA1873 | The same |
  | `Infrastructure/Gateway/Razorpay/RazorpayPaymentProvider.cs` | CA1068 | `SendAsync` took `CancellationToken` before a required parameter; reordered, with both call sites |
  | `Infrastructure/Gateway/Razorpay/RazorpayPaymentProvider.cs` | CA1859 | A private method took `IReadOnlyDictionary` where every caller passes a `Dictionary` |

  Backend: **0 warnings, 0 errors.** `-Stage format` and `-Stage lint` pass (65 frontend files were
  reformatted by Prettier; 23 Nx projects lint clean). `-Stage frontend` builds both apps —
  storefront **162.4 kB** gzipped against 180 kB, admin **132 kB** against 300 kB.

  ### 2. The images did not build at all

  **No image had built since Step 8.** All three Dockerfiles restore from a hand-written list of
  `COPY` lines naming four module projects — Platform, Identity, Media, Notifications — and
  fourteen modules had been added since. `dotnet publish` failed with `NETSDK1004` for every one of
  them.

  The list was a second declaration of which modules exist, kept by hand, and it fell fourteen
  behind without anything noticing. It is replaced in all three files by a wildcard copy and a loop
  that puts each project file back in its own directory, so **a module added later needs no change
  to any Dockerfile**.

  ### 3. Compose could not read its environment

  `docker compose --env-file .env` refused the file outright: `line 371: key cannot contain a
  space`. A literal newline escape inside a comment in **`.env.example`** had at some point become
  a real newline, splitting the comment in two and leaving `. Blank in` as a bare line. Rejoined
  into one comment that says what it meant to say.

  The local `.env` was separately a Step 8-era copy holding **29 of 86 keys** — no signing key, no
  encryption keys, no imgproxy secrets, no SSR settings. Regenerated from the corrected example
  with the three local values preserved (`POSTGRES_PORT=5433` and the bootstrap admin credentials).

  ### 4. Migrations apply, and re-apply as a no-op

  Against a **fresh** database (`down -v`, then a new volume), the migrator applied
  **26 migrations across 18 module contexts** and ran **12 seeders**, exit 0. A second run:
  **18 contexts inspected, 0 migrations applied, 12 seeders** (idempotent by design), exit 0. Run a
  third time at the end of the step with the same result.

  No ordering conflict, no duplicate schema or table name, no cross-module FK gap. The
  `SELECT ... FROM <schema>.__ef_migrations_history` errors in the first run's log are EF probing a
  table it is about to create, and the `libgssapi_krb5.so.2` line is the one the migrator's
  Dockerfile already documents.

  ### 5. The stack boots — after two start-up failures

  **The API failed dependency-injection validation.** `CatalogModule` never registered four
  collaborators its own handlers take: `VariantWriter`, `ProductLifecycle`,
  `StorefrontCatalogService` and `CatalogJobProjection`. Registered beside their siblings.

  **The worker failed with `Duplicate health checks were registered with the name(s): storage`.**
  `AddKlaraHomeStorage` carried a comment calling itself idempotent and was not: Catalog and Media
  both call it, and the second `AddCheck("storage")` is fatal — the health service refuses two
  registrations under one name. The whole method now runs once per container, remembered by a
  marker registration, and the comment now says why.

  Final state: **traefik, postgres, redis, minio, imgproxy, mailpit, api and worker all up**,
  `minio-init` exited 0 as the one-shot it is, no restart loops. `/health/ready` through Traefik is
  `Healthy` on all five checks — self, postgres, redis, platform-tenant and storage.

  The worker has **no** health check to be green (see the Parking Lot); its readiness was read from
  its own logs instead — every background job announced its start, and after the repairs below it
  logs **zero errors**.

  ### 6. Every background job was failing

  The worker started but nothing it did worked. Two distinct defects, both only visible against a
  real database:

  **`42703: column k.xmin does not exist`, in ten places.** Every worker loop claims rows with
  `SELECT * FROM ... FOR UPDATE SKIP LOCKED` wrapped as a subquery. `SELECT *` does not return
  system columns, and the model maps `xmin` as the concurrency token, so EF asked for a column the
  subquery had not exposed. `OutboxDispatcher` — written first, at Step 4 — says `SELECT *, xmin`
  and explains why in a comment; **the nine loops written after it did not copy that line.** Fixed
  in the abandoned-cart sweeper (twice), the catalogue job dispatcher, the reservation sweeper, the
  order lifecycle sweeper, the gateway event worker, the courier event worker, and all three
  gapless numbering counters (orders, returns, settlements), each with the reason written beside
  it. `notification_messages` is partitioned, maps no `xmin`, and was already correct.

  **`The required column 'cached_on_hand' was not present`.** `StockReconciliationJob.DriftQuery`
  aliased its columns in PascalCase, but `SqlQuery<T>` maps a result to the model's *column* names
  and this model applies the snake-case naming convention to every entity type it builds — a query
  type included. Aliases changed to snake_case.

  After both: **`Stock reconciliation clean: every cached quantity matches its ledger`**, and every
  other loop running quietly.

  ### 7. The surface answers

  The OpenAPI document generates and serialises: **391 paths, 489 operations, 489 operation ids
  with no duplicate, 494 schemas**, exactly the counts Step 22's generator was built against.
  `-Stage codegen` confirms the committed client still matches — including after the Content module
  visibility change below, which left the document byte-identical.

  ### 8. The smoke walk, by hand

  One pass through the MVP journey against the running stack, over HTTP against the API through
  Traefik. It found one more defect and otherwise passed:

  1. **Admin sign-in** — `POST /admin/auth/login` correctly answers a `two-factor-enrolment`
     challenge rather than a session, because the bootstrap administrator has no second factor.
     Enrolled, computed the TOTP, verified: an access token with `platform-admin` and its
     permissions.
  2. **Vendor onboarding** — created, submitted, approved. `activate` was **refused**, correctly,
     and `readiness` named all three blockers. Uploaded and verified four KYC documents, added and
     verified a bank account, added a pickup location — `readiness` then `{"isReady": true}` and
     activation succeeded. The refusals along the way are the state machine working: a public media
     file was refused for a KYC scan ("must be uploaded as a private file"), and `Purchase` was
     refused as a hand-posted stock reason ("written by the system").
  3. **Catalog** — brand, category, product, variant, warehouse, listing, stock item. Opening the
     stock item answered **500**: `ProductCatalogDirectory` applied its `Where` *after* projecting
     into a `ListingSummary` record, and a predicate over a constructed record is not translatable,
     so `FindListingAsync` and `FindListingsAsync` threw on every call. The caller now narrows the
     listings before the projection. Checked the five sibling directories: none repeats it.
  4. **Media** — upload returns a stored object, a public URL and an imgproxy `thumb` variant, so
     MinIO and imgproxy are both wired correctly. A private upload lands in the private bucket.
  5. **Search** — the index carried the listing and `/store/products` answered it. An earlier empty
     answer was an **output-cache hit** (`Age: 49`), not a defect.
  6. **Shopper** — registered, added two to the cart, created a checkout session, set the address,
     took the one shipping option the seeded rate card offers, chose COD, reviewed.
  7. **Order placed** — `KH-2609-000001`, `InProgress`, one sub-order `KH-2609-000001-01` at
     `Confirmed`. **INR 1798 with INR 85.62 tax**, which is 5% back-calculated from an inclusive
     price, correct to the paisa.
  8. **Seen in admin** — the order lists and opens, its sub-order offers `["Processing",
     "Cancelled"]` off the API's own transition table, and the timeline carries both events.
  9. **Stock moved** — 25 on hand before, **23 after**, 0 reserved: the reservation was taken at
     placement and released into a sale, and the ledger and the cache agree.

  ### 9. The two frontends

  **Storefront.** Built, then served from its SSR bundle against the live API. `/`, `/p/{slug}`,
  `/c/{slug}` and `/search` all return 200 and **server-render real data** — the product's name and
  its INR 899 price are in the HTML, not fetched after. An unknown URL is a genuine 404. The full
  SEO surface is in the server's document: canonical, `robots` (correctly `noindex, nofollow`,
  because this deployment's `allowIndexing` is false), Open Graph, description, and JSON-LD
  carrying `BreadcrumbList` + `Product` on a PDP and `BreadcrumbList` + `ItemList` on a category.

  One real defect: **every page's title rendered a literal `{store}`.** The settings contract says
  "`{title}` and `{store}` are substituted"; `SeoService` substituted only `{title}`. Fixing it
  exposed the reason it had never worked — the two halves of the configuration arrive from two
  different endpoints (the template from the SEO config, the store name from branding), and nothing
  orders them against a page's own `apply`. So `configure` now merges instead of replacing, and
  re-applies the last page's metadata; a configuration that lands after a page rendered is now
  honoured rather than lost, which during server rendering is the difference between shipping the
  right title and the wrong one. All four routes now render `... | Klara Home`.

  **Admin.** Built and served; it boots, loads its `config.json`, routes, and its auth guard
  redirects an anonymous visitor to `/login?returnUrl=/dashboard` with the sign-in form rendered.
  Its **authenticated** round trip was not driven through a browser: the API's certificate is
  self-signed and must be accepted once, and no password was typed into a browser form. Every
  `/admin/*` endpoint those screens call was exercised directly during the smoke walk instead. The
  browser end of it is a Step 29 Playwright row.

  ### 10. The architecture gate, red since Step 21, is green

  `ModuleBoundaryTests.A_module_exposes_nothing_publicly_except_its_module_class` had failed at
  seven consecutive step boundaries: `KlaraHome.Modules.Content` declared **41** `Application`
  records `public` where every other module keeps them internal. Step 28's row said this step must
  fix it or delete it. Fixed — 41 declarations made `internal`; the module already had
  `InternalsVisibleTo` for both test assemblies, and the OpenAPI document and generated client are
  unchanged afterwards. **14 of 14 architecture tests pass.**

  ### 11. CI floors restored

  | Suite | Was | Now |
  |---|---|---|
  | UnitTests | 380 | **941** |
  | ArchitectureTests | 14 | 14 |
  | IntegrationTests | 180 | 180 — that suite is Step 29's work and has not grown since Step 8 |

  The coverage minimum stays at **70** in the committed defaults, as §3.2 rule 6 requires. The
  post-sprint measurement is now recorded in `tools/ci.ps1` beside it: **15.84% line, 29.08%
  branch** over the two suites that run without Docker. That is the number Step 29 must restore.

  ### Gates, at the close

  | Gate | Result |
  |---|---|
  | `-Stage build` | 0 warnings, 0 errors |
  | `-Stage format` | C# and Prettier clean |
  | `-Stage lint` | 23 Nx projects clean, module boundaries hold |
  | `-Stage codegen` | client in sync, 23 files |
  | `-Stage frontend` | both apps built, all four budgets met |
  | `-Stage test` | **955 backend tests** (941 unit + 14 architecture), frontend unit tests pass |
  | Migrator | 26 applied on empty, 0 on re-run |
  | Stack | 8 services up, all health checks green, no restart loop |
  | Smoke walk | complete, end to end |

  ### Not fixed here

  Everything found and not fixed is in [`PARKING_LOT.md`](../PARKING_LOT.md) (9 new rows) or
  [`TEST_DEBT.md`](../TEST_DEBT.md) (11 new rows). The sharpest are: **the worker has no health
  check**; **neither Angular app is in the dev stack**, so both were run by hand on loopback ports
  behind a temporary override that was removed afterwards; **ten sweepers repeated one raw-SQL
  mistake** and a shared claim helper would stop an eleventh; and **impersonation, a supplier
  screen and a shipping rate-card editor were never built** and are now past the last build step
  with no owner — this step's scope forbids building them.

