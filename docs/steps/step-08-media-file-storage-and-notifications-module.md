# Step 8 — Media, file storage & Notifications module

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** B · **Depends on:** Step 7, Step 7A
- **Objective:** Central services every later module depends on.
- **Deliverables:**
  - Media service: S3-compatible upload (MinIO), validation, virus-scan hook, image
    derivatives/responsive variants (imgproxy), CDN-ready public URLs, signed private URLs.
  - Notification service: templated Email / SMS / WhatsApp with provider abstraction,
    per-event templates, localisation, queued + retried delivery, delivery status log,
    customer notification preferences, DLT-compliant SMS template registry (India TRAI).
  - Document generation service (invoices, credit notes, labels) — PDF pipeline.
- **Acceptance criteria:** An image uploads and returns responsive variants; a templated
  transactional email and SMS are dispatched and logged; failures retry with backoff.
- **Outcome / Notes:** ✅ **DONE 2026-09-05.** All three criteria met and demonstrated against the
  containerised stack: an image uploaded through `POST /admin/media` came back with three renditions
  and imgproxy served one of them from the real MinIO object; a templated email was queued by the
  API, dispatched by the worker and recorded as `Sent`; a transient refusal retried three times with
  growing, jittered delays and then gave up. The SMS half is met in the only way it can be —
  see the deviation below and ADR-017.

  **Two modules, not one.** `Media` owns the `media` schema and `Notifications` owns
  `notifications`. Media was not in the module list at all, though eight columns across six schemas
  hold a `file_id` and `BrandingSettings` already referred to "the Media module"; the alternatives
  and the choice are recorded in **ADR-016**. Specification changed first, under protocol rule 8:
  docs `01`, `02`, `03`, `04`, `07` and `08` were amended before any code was written.

  **Media.** `IFileStorage` over the S3 API in the shared layer — MinIO today, S3 or R2 by
  configuration — and everything that knows what a product image *is* in the module above it. A file
  is identified from its own bytes rather than from what the caller called it, so a `.png` carrying a
  PHP script is refused and the extension a download is offered under comes from the content. The
  storage key is generated and date-prefixed, never built from the uploaded name: that one decision
  answers the path-traversal, collision and bucket-listing questions at once. Renditions are computed
  and never stored — the imgproxy URL *is* the instruction — and private documents have no URL at
  all until somebody's authorisation has been checked and one is signed.

  **Notifications.** Templates are data an operator edits without a deploy; a caller names an
  *event*. The delivery log is partitioned monthly like the audit trail, and is the queue: the worker
  claims due rows with `FOR UPDATE SKIP LOCKED`, so more than one worker is safe by construction.
  Backoff doubles with jitter — the jitter matters more than the doubling, because without it a
  backlog released by a recovering SMTP host arrives in one second.

  **The Step 7 debt is closed, not moved.** `LoggingOtpDispatcher` is deleted. A one-time code is
  rendered from an editable template and handed straight to a provider **inline** — it cannot wait
  for a poll, and its body must not be stored for one — and the row that records the attempt keeps
  neither the body nor any variable's value. Proven against the real database: `body` null, `payload`
  `{"code":"[redacted]", …}`, and the code readable only in the message that was sent.

  **Endpoints added**

  | Method | Route | Notes |
  |---|---|---|
  | `GET`, `POST` | `/admin/media` | Multipart upload; `?visibility=private` for the documents bucket |
  | `GET`, `DELETE` | `/admin/media/{id}` | Deleting retires the row and removes the object |
  | `GET` | `/admin/media/{id}/link` | Short-lived signed URL, minted after the authorisation check |
  | `GET`, `PUT` | `/admin/notification-templates[/{id}]` | Lists the placeholders each expects, and why an SMS would be dropped |
  | `GET` | `/admin/notifications[/{id}]` | The delivery log. Recipients masked (§5) |
  | `POST` | `/admin/notifications/{id}/retry` | Re-queues a failed or suppressed message |
  | `POST` | `/admin/notifications/test` | Proves a channel through the real pipeline, not round it |
  | `GET`, `PUT` | `/store/me/notification-preferences` | The §3.1 endpoint Step 7 deliberately left to this module |

  **Deviations from the specification, and why**

  1. **PDFsharp + MigraDoc, not QuestPDF** (ADR-015, User's decision). QuestPDF is free only below
     USD 1 M revenue, which is an obligation that would travel with every redistributed copy —
     exactly what ADR-009 exists to prevent. `01-architecture.md` §7 amended.

  2. **A channel with no provider is `Suppressed`, a third terminal state** (ADR-017, User's
     decision). `08-integrations.md` §7 says missing credentials are a `⛔ BLOCKED` condition, and
     taken literally the programme would stop here until the client buys an SMS account. Instead the
     pipeline is built, and a message nobody could send is recorded with a reason rather than
     retried against nothing or dropped silently.

  3. **A sensitive message is sent inline rather than queued.** The queue cannot hold what must not
     be stored, and a sign-in code cannot wait five seconds for a poll. The consequence is accepted
     and recorded: a failed OTP is not retried, because by then the person has pressed "resend".

  4. **The DLT rule moved from the table to the sender.** It was first a database `CHECK`: an active
     SMS template must carry a registered id. That is only true where an operator can drop the
     message — and it made a local mobile sign-in impossible, which is precisely what ADR-017
     promised it would not. The rule now lives where it bites: a sender that talks to an operator
     declares `RequiresProviderTemplate`, the admin editor still refuses to activate an unregistered
     SMS template, and the seeder still seeds them inactive **in Production**.

  5. **Outside Production, an SMS is delivered to Mailpit** as `<digits>@sms.invalid`. It is bound to
     `IHostEnvironment`, not to a setting, because a mistake here would send one customer's sign-in
     code to an operations mailbox.

  6. **Presigned URLs are signed against a second, public endpoint.** SigV4 covers the host, so a URL
     signed for `http://minio:9000` is unreachable from a browser and cannot be rewritten. Found in
     the live demonstration, not in a test — the tests substitute storage at the network boundary,
     which is exactly the seam this defect hid behind.

  7. **`notification_messages` carries no `xmin` concurrency token.** PostgreSQL cannot return a
     system column from a partitioned table. A new `IPartitioned` marker says so — `IAppendOnly`
     would have been a lie, because these rows *are* updated — and concurrency is pessimistic
     instead.

  #### What is still owed

  Nothing scans an upload: `IVirusScanner` has one implementation that records `Skipped` rather than
  pretending, and `Media:RequireVirusScan` turns that into a refusal. The SMS and WhatsApp rows of
  `08-integrations.md` §7 are still incomplete, so both channels are suppressed in production; the
  day an account exists it is one adapter and one setting. Retention — ninety days for message
  bodies, and partition maintenance for both partitioned tables — belongs with the other operational
  jobs at **Step 31**.
