# 08 — Third-Party Integrations

> Every integration sits behind a **provider interface** owned by us. No vendor SDK type ever
> leaks into domain or application code. This is what makes the platform redistributable: a
> different client can use a different courier or SMS provider by writing one adapter.

Common adapter contract: typed request/response models, Polly resilience (timeout → retry with
jitter → circuit breaker), structured logging with correlation id, raw request/response capture
for money-moving calls, sandbox/live switch by configuration, and a health check.

---

## 1. Payments — Razorpay

**Interface:** `IPaymentProvider`
`CreatePaymentIntentAsync`, `FetchPaymentAsync`, `CapturePaymentAsync`, `RefundAsync`,
`FetchRefundAsync`, `VerifyWebhookSignature`, `FetchSettlementsAsync`.

**Flow (prepaid)**

| Step | Actor | Action |
|---|---|---|
| 1 | API | `POST /v1/orders` with `amount` (paise, integer), `currency: INR`, `receipt: <orderNumber>`, `notes` |
| 2 | Storefront | Opens Razorpay Standard Checkout with `order_id` + public key |
| 3 | Customer | Pays via UPI / card / net banking / wallet / EMI |
| 4 | Razorpay | Fires `payment.captured` (and `order.paid`) webhook |
| 5 | API | Verifies signature → persists raw event → **re-fetches the payment from the API** → confirms the order |
| 6 | Worker | `OrderConfirmed` → stock commit, invoice, notifications, shipment |

The browser callback is treated as a UX signal only. **The webhook plus an API re-fetch is the
only source of payment truth.**

**Webhooks subscribed:** `payment.authorized`, `payment.captured`, `payment.failed`,
`order.paid`, `refund.created`, `refund.processed`, `refund.failed`, `settlement.processed`,
plus Route events (`transfer.processed`, `transfer.failed`) once payouts are live.

**Signature verification:** HMAC-SHA256 of the **raw body** with the webhook secret,
constant-time compare, timestamp within a 5-minute skew, duplicate `event.id` = no-op.

**Vendor payouts — Razorpay Route:** each approved vendor gets a linked account created during
KYC (Step 9), stored as `vendors.gateway_account_id`. Two supported settlement modes,
selectable in configuration:
- **Deferred transfers** (recommended): capture to the platform account, then transfer to
  vendors after the return window closes — matches our settlement ledger and TCS/TDS timing.
- **On-capture split**: split at payment time. Simpler, but poorly suited to returns and
  statutory deductions. Not the default.

RazorpayX is the fallback for direct bank payouts if Route is not enabled for the merchant.

**Reconciliation (worker job):**
1. Every 15 minutes, find orders in `PendingPayment` older than 20 minutes and query Razorpay
   for their true state — recovers from any lost webhook.
2. Daily, ingest the settlement report and match gateway settlements to our payment records;
   alert on any mismatch.
3. Daily, assert `Σ captured − Σ refunded` per order equals the order's payment summary.

**Failure handling:** gateway timeout → the order stays `PendingPayment` with a retry link;
duplicate capture attempts are idempotent; a customer can retry payment on a failed order from
the account area for a configurable window (default 24 h) before auto-cancellation releases stock.

**COD** is an internal provider (`InternalCodPaymentProvider`) implementing the same interface:
eligibility check → order confirmed without a gateway → collection recorded on delivery →
courier remittance reconciled against `cod_collections`.

---

## 2. Logistics — aggregator (Shiprocket / Delhivery / Blue Dart class)

**Interface:** `IShippingProvider`
`CheckServiceabilityAsync(pincode, weight, cod)`, `CreateShipmentAsync`, `GenerateLabelAsync`,
`GenerateManifestAsync`, `SchedulePickupAsync`, `CancelShipmentAsync`, `TrackAsync`,
`VerifyWebhookSignature`.

Recommendation for v1: **an aggregator** (one integration, many couriers, automatic courier
selection, COD supported) rather than a direct courier contract — faster to launch and easier
for a redistributed deployment. A direct Delhivery/Blue Dart adapter can be added later behind
the same interface.

| Concern | Approach |
|---|---|
| Serviceability | Cached per (pincode, courier) with a TTL; refreshed nightly; PDP and checkout both read the cache, never the live API on the hot path |
| Rate selection | Our own `shipping_rates` table decides what the **customer** pays; the aggregator decides what **we** pay. Both are recorded on the shipment for margin reporting |
| Label & manifest | PDFs stored in object storage, printed from admin |
| Tracking | Webhook-first; a polling fallback job every 30 minutes for shipments with no update in 24 h |
| NDR | Ingested as a dedicated queue in admin with re-attempt / reschedule / RTO actions |
| Weight disputes | Charged vs declared weight captured per shipment for reconciliation |
| Reverse pickup | Same interface, `isReturn: true`, AWB stored on the RMA |
| Failure | If shipment creation fails, the sub-order stays `Packed` and appears in an admin exception queue with a manual-AWB fallback |

---

## 3. Communications

### 3.1 SMS (India, DLT-regulated)

**Interface:** `ISmsProvider` — `SendAsync(to, templateId, variables)`, delivery-receipt webhook.
Candidates: MSG91, Kaleyra, Gupshup, Twilio (India routes), or Razorpay-adjacent providers.

DLT (TRAI) requirements handled explicitly:
- Registered entity id and sender header stored in configuration.
- Every SMS maps to a **pre-approved DLT template id** held on the notification template row.
- Variable content must match the approved template exactly — enforced by a template-render
  validation test, because a mismatch silently drops messages in production.
- OTP messages use a dedicated, high-priority route.

### 3.2 WhatsApp Business (optional, high value in India)

`IWhatsAppProvider` via a BSP (Gupshup, Interakt, WATI, or Meta Cloud API). Used for order
confirmation, dispatch, delivery, NDR follow-up and return updates. Requires pre-approved
message templates and opt-in capture. Feature-flagged — v1 can launch without it.

### 3.3 Email

`IEmailProvider` — Amazon SES (Mumbai), Postmark, Resend, or SendGrid. Transactional only in
v1: OTP fallback, order confirmation, invoice, dispatch, delivery, refund, return updates,
vendor onboarding and settlement statements, admin alerts.
Setup requirements: SPF, DKIM, DMARC on the sending domain; bounce and complaint webhooks feed
suppression handling.

### 3.4 Push notifications

Out of scope for v1 (web push is a Phase-2 decision alongside PWA installability).

---

## 4. Media & Storage

**Interface:** `IFileStorage` — `PutAsync`, `GetAsync`, `DeleteAsync`, `GetSignedUrlAsync`.

- v1: **MinIO** on the VPS, S3 API. Buckets: `media-public` (catalog, CMS), `docs-private`
  (invoices, KYC, labels, manifests, exports).
- Because the S3 API is used throughout, moving to **AWS S3 (ap-south-1)** or **Cloudflare R2**
  is a configuration change with no code impact — the documented path when media volume or
  bandwidth justifies it.
- **imgproxy** performs on-the-fly resize/crop/format (AVIF/WebP) with signed URLs, so the
  storefront can request exactly the variant a viewport needs.
- Private documents are served only via short-lived signed URLs generated after an
  authorisation check — never by a public path.

---

## 5. Analytics & Marketing (thin, swappable, per-tenant)

- A single `AnalyticsService` facade in the frontend emits a fixed event taxonomy
  (`view_item`, `view_item_list`, `add_to_cart`, `remove_from_cart`, `begin_checkout`,
  `add_payment_info`, `purchase`, `refund`, `search`, `sign_up`, `login`).
- Sinks (GA4, Meta Pixel, others) are configured **per tenant** and can be disabled entirely —
  a redistributed deployment must be able to run with no third-party tracking at all.
- Consent gating: non-essential tags fire only after consent, satisfying the DPDP posture.
- Server-side conversion events are a Phase-2 consideration.

---

## 6. Optional / Phase-2 Integrations (designed for, not built)

| Integration | Interface ready | Notes |
|---|---|---|
| E-invoicing (IRP/NIC) | Invoice fields present | Activate per vendor at the turnover threshold |
| Accounting (Tally / Zoho Books) | Export contract | Journal export from the settlement ledger |
| Search engine (Meilisearch / OpenSearch) | `ISearchProvider` | Feature-flagged swap from Postgres FTS |
| ONDC | — | Significant; separate project |
| Live chat / helpdesk | Widget slot in the shell | Freshdesk/Zoho Desk |
| Review syndication, affiliate, ads | — | Phase 2 |

---

## 7. Integration Register (to be completed at Step 0)

| Integration | Provider | Account owner | Sandbox creds | Live creds | Contract signed | Notes |
|---|---|---|---|---|---|---|
| Payments | Razorpay | Client | ☐ | ☐ | ☐ | Route enablement to be confirmed |
| Payouts | Razorpay Route / X | Client | ☐ | ☐ | ☐ | |
| Logistics | TBC | Client | ☐ | ☐ | ☐ | Aggregator recommended |
| SMS | TBC | Client | ☐ | ☐ | ☐ | DLT entity + templates needed |
| WhatsApp | TBC | Client | ☐ | ☐ | ☐ | Optional for v1 |
| Email | TBC | Client | ☐ | ☐ | ☐ | Domain DNS access required |
| VPS | TBC | Client | — | ☐ | ☐ | India region recommended |
| Domain / DNS | TBC | Client | — | ☐ | ☐ | |

**No integration work starts until its row is complete.** Missing credentials are a `⛔ BLOCKED`
condition under the execution protocol, not a reason to stub and move on.
