# 04 — API Specification

> REST over HTTPS · JSON · `/api/v1` · OpenAPI 3.1 generated from code and used to generate the
> Angular client. **The OpenAPI document is the contract** — no hand-written frontend DTOs.

---

## 1. Conventions

| Aspect | Rule |
|---|---|
| Base URL | `https://api.<domain>/api/v1` |
| Versioning | URL segment. A breaking change means `/v2`; additive changes never break `/v1` |
| Casing | JSON `camelCase`; query params `camelCase` |
| IDs | UUIDv7 strings |
| Money | `{ "amount": 1499.0000, "currency": "INR" }` — string-safe decimal, never float maths client-side |
| Dates | ISO 8601 with offset, always UTC (`2026-09-05T09:12:33Z`) |
| Auth | `Authorization: Bearer <jwt>`; refresh token in an HttpOnly, Secure, SameSite=Lax cookie |
| Idempotency | `Idempotency-Key: <uuid>` **required** on all POSTs that create orders, payments, refunds or payouts |
| Correlation | `X-Correlation-Id` echoed on every response |
| Rate limits | `X-RateLimit-Limit`, `-Remaining`, `-Reset`; `429` with `Retry-After` |
| Compression | gzip/brotli negotiated at the edge |
| Caching | `Cache-Control` + `ETag` on public catalog/content reads; `no-store` on anything authenticated |

### 1.1 Standard envelopes

Single resource — returned bare:
```json
{ "id": "0192...", "name": "Cotton Cushion Cover", "...": "..." }
```

Collections — always wrapped, always keyset-paginated on large sets:
```json
{
  "items": [ /* ... */ ],
  "page": { "size": 24, "nextCursor": "eyJpZCI6...", "prevCursor": null, "total": 1284 },
  "facets": { /* only on search endpoints */ }
}
```
`total` is omitted (null) where counting is too expensive; the UI must not depend on it.

### 1.2 Errors — RFC 9457 ProblemDetails

```json
{
  "type": "https://klarahome.dev/errors/validation-failed",
  "title": "Validation failed",
  "status": 422,
  "code": "VALIDATION_FAILED",
  "detail": "One or more fields are invalid.",
  "instance": "/api/v1/checkout/0192.../place-order",
  "correlationId": "b0c3...",
  "errors": {
    "shippingAddressId": ["Address does not belong to the current customer."],
    "lines[2].quantity": ["Only 3 units are available."]
  }
}
```

| Status | When |
|---|---|
| 400 | Malformed request |
| 401 | Missing/expired token |
| 403 | Authenticated but not permitted (incl. cross-vendor access attempts) |
| 404 | Not found **or** not visible to this caller (never leak existence) |
| 409 | Conflict: concurrency token mismatch, duplicate resource, invalid state transition |
| 410 | Expired checkout session / cart |
| 422 | Validation or business-rule failure (`code` identifies which rule) |
| 429 | Rate limited |
| 500 | Unexpected — correlation id only, never internals |
| 503 | Dependency unavailable (gateway/courier), with `Retry-After` |

Stable machine-readable codes (examples): `CART_ITEM_OUT_OF_STOCK`, `PRICE_CHANGED`,
`COUPON_INVALID`, `COD_NOT_SERVICEABLE`, `PAYMENT_FAILED`, `ORDER_NOT_CANCELLABLE`,
`RETURN_WINDOW_CLOSED`, `VENDOR_INACTIVE`. The frontend switches on `code`, never on message text.

---

## 2. API Surfaces

Three logical surfaces on one host, separated by path and policy:

| Surface | Prefix | Audience | Auth |
|---|---|---|---|
| Storefront | `/api/v1/store/...` | Guests & customers | Optional / customer JWT |
| Admin | `/api/v1/admin/...` | Platform staff & vendors | Staff/vendor JWT + permissions |
| Webhooks | `/api/v1/webhooks/...` | Third parties | Signature verification, no JWT |

Vendor endpoints live under `/api/v1/admin/...` and are **scoped by the caller's vendor**, not
by a vendor id in the path — a vendor cannot address another vendor's data at all.

---

## 3. Storefront Endpoints

### 3.1 Auth & account
```
POST   /store/auth/otp/request           { mobile, purpose }        [flag: identity.mobile-otp-login]
POST   /store/auth/otp/verify            { mobile, code } → tokens   [flag: identity.mobile-otp-login]
POST   /store/auth/login                 { email, password }
POST   /store/auth/refresh               (cookie) → new access token
POST   /store/auth/logout
POST   /store/auth/register
POST   /store/auth/password/forgot | /reset                          [flag: identity.password-reset-email]
POST   /store/auth/password/change       { challengeToken?, currentPassword, newPassword }

GET    /store/auth/external/providers    → which providers are enabled, for the buttons
GET    /store/auth/external/{provider}/start     ?returnUrl=         [flag: identity.external-login]
GET    /store/auth/external/{provider}/callback  ?code= &state=      → 302 back to the storefront
GET    /store/me/external-logins
DELETE /store/me/external-logins/{id}    refused if it is the only credential left
GET    /store/me
PATCH  /store/me
GET    /store/me/addresses
POST   /store/me/addresses
PUT    /store/me/addresses/{id}
DELETE /store/me/addresses/{id}
GET    /store/me/notification-preferences
PUT    /store/me/notification-preferences
GET    /store/me/wallet                  → store-credit balance      [flag: pricing.store-credit]
GET    /store/me/wallet/transactions     ?cursor= &size=             [flag: pricing.store-credit]
POST   /store/me/data-export            (DPDP)
POST   /store/me/delete-request         (DPDP)
```

### 3.2 Catalog & discovery
```
GET /store/categories                       ?parentId= &depth=
GET /store/categories/{slug}
GET /store/brands
GET /store/products                         ?category= &brand= &q= &minPrice= &maxPrice=
                                            &attr.color=beige &rating= &sort= &cursor= &size=
                                            → served from the Search projection (Step 19), not from
                                              the catalog schema: the facet counts need it
GET /store/products/{slug}                  → product + variants + buy-box listing + offers,
                                              with the mandatory disclosures (MRP, net quantity,
                                              country of origin, manufacturer/importer)
GET /store/products/{slug}/offers           ?variantId= → all vendor listings for a variant, in
                                              buy-box order; omit variantId for the default variant
GET /store/vendors/{slug}                   → a seller's public profile; 404 unless they are Active
GET /store/products/{slug}/reviews          ?sort= &rating= &cursor=
GET /store/products/{slug}/questions
GET /store/search/suggest                   ?q=
GET /store/collections/{slug}
GET /store/listings/{id}/delivery-estimate  ?pincode=
```

`GET /store/products` returns `facets` alongside `items`:
```json
"facets": {
  "brand":  [{ "value": "Klara", "label": "Klara", "count": 42 }],
  "price":  [{ "from": 0, "to": 499, "count": 120 }],
  "attributes": { "color": [{ "value": "beige", "count": 18 }] }
}
```

### 3.3 Cart & checkout
```
GET    /store/cart                         (cookie token or JWT) → lines, per-vendor groups,
                                             the itemised quote, and one validation issue per
                                             line that has one
DELETE /store/cart                         empties it
POST   /store/cart/items                   { listingId, quantity }
PATCH  /store/cart/items/{lineId}          { quantity, savedForLater }
DELETE /store/cart/items/{lineId}
POST   /store/cart/merge                   (after login) { strategy? } merges the cookie cart into
                                             the shopper's own and clears the cookie
POST   /store/cart/coupon                  { code }
DELETE /store/cart/coupon
GET    /store/cart/quote                   → full itemised price + GST breakdown
POST   /store/quote                        { lines[{ listingId, quantity }], stateId?, couponCode?,
                                             shippingAmount?, paymentMethod? }
                                           → the same itemised breakdown for a basket the server is
                                             not holding — a PDP price, a quantity-tier preview, a
                                             coupon tried before the cart exists. Anonymous, and it
                                             writes nothing.

POST   /store/checkout                     → creates checkout session (refuses a cart that has a
                                             blocking validation issue, and says which line)
GET    /store/checkout/{id}                → the session as it stands
PUT    /store/checkout/{id}/address        { shippingAddressId, billingAddressId, gstin? }
GET    /store/checkout/{id}/shipping-options → per vendor, with the dispatch SLA and the promised
                                             delivery window
PUT    /store/checkout/{id}/shipping       { perVendor: [{ vendorId, optionCode }] }
GET    /store/checkout/{id}/payment-methods → what this basket may be paid by, and for COD the
                                             reason when it may not
PUT    /store/checkout/{id}/payment-method { method }           // prepaid | cod
GET    /store/checkout/{id}/review         → final quote, re-validated
POST   /store/checkout/{id}/place-order    (Idempotency-Key) → { orderId, payment }
POST   /store/checkout/{id}/abandon        the shopper backed out; releases the session
```

`Idempotency-Key` on `place-order` is **required**, not optional: the request reserves stock and
creates an order, and a shopper double-tapping *Pay* on a flaky connection must produce one order
and two identical responses. The key is stored with a hash of the request; replaying it against the
same basket returns the original response, and replaying it against a different one is `409`.

`place-order` response for prepaid:
```json
{
  "orderId": "0192...",
  "orderNumber": "KH-2609-000184",
  "payment": {
    "provider": "razorpay",
    "providerOrderId": "order_Nxxxx",
    "publicKey": "rzp_live_xxx",
    "amount": { "amount": 2499.0000, "currency": "INR" },
    "prefill": { "name": "...", "contact": "+91...", "email": "..." }
  }
}
```
For COD, `payment` is `null` and the order is already `Confirmed`.

### 3.4 Orders & invoices
```
GET    /store/orders                       ?status= &cursor= &size= → the caller's own orders,
                                             newest first, each with its per-seller breakdown
GET    /store/orders/{id}                  → one order in full: a section per seller, the frozen
                                             lines and tax, and the visible timeline
GET    /store/orders/{id}/timeline         → what has happened, oldest first. Internal entries are
                                             never included
POST   /store/orders/{id}/cancel           { reason? } cancels every part that may still be
                                             cancelled, and the response says which could not
POST   /store/sub-orders/{id}/cancel       { reason?, lines? } cancels one seller's part, or the
                                             units `lines` names
GET    /store/orders/{id}/invoices         → the tax invoices raised against it, one per seller
GET    /store/invoices/{id}/download       → { url } a short-lived signed link to the PDF
```

Every route is scoped to the caller's own orders by the token, never by a parameter, and an id
belonging to somebody else answers `404` exactly as an invented one does.

A shopper may cancel up to and including `Packed`; after dispatch only Operations may, with a
reason (`02-domain-model.md` §5.1). A refusal is `ORDER_NOT_CANCELLABLE`.

`lines` on a cancellation is `[{ orderLineId, quantity }]`. A quantity greater than what is left is
clamped rather than refused: "cancel this line" and "cancel five of the three that remain" mean the
same thing. A partial cancellation does not move the sub-order — it is still being packed — and
what is still owed is derived from the lines rather than written back over the agreed totals.

Minting a download link **is** the grant: the URL carries no authorisation of its own beyond its
expiry, so the ownership check happens before one is signed.

### 3.5 Payments
```
GET    /store/payments/orders/{orderId}    → where the money for this order stands: status, what
                                             has been captured and refunded, and whether it may
                                             still be retried
POST   /store/payments/orders/{orderId}/retry   (Idempotency-Key) → a fresh payment instruction for
                                             an order whose payment failed or was never completed,
                                             within the retry window
POST   /store/payments/orders/{orderId}/verify  { providerPaymentId, providerOrderId, signature }
                                           → records the browser's callback as an attempt
```

Every route is scoped to the caller's own orders by the token. An order id belonging to somebody
else answers `404` exactly as an invented one does.

**`verify` confirms nothing.** The browser callback is a UX signal: the handshake signature is
checked, the attempt is recorded, and the response says only what the platform currently believes.
An order moves to `Confirmed` on the webhook plus an API re-fetch, and on nothing else
(`08-integrations.md` §1, `07-security-compliance.md` §4) — which is why the storefront polls
`GET /store/payments/orders/{orderId}` after the widget closes rather than acting on its own result.

`retry` is refused with `PAYMENT_RETRY_WINDOW_CLOSED` once the window has passed, and with
`PAYMENT_ALREADY_CAPTURED` for an order that is in fact paid. A cash-on-delivery order has nothing
to retry and answers `PAYMENT_NOT_PAYABLE`.

### 3.6 Engagement
```
GET/POST/DELETE /store/wishlist[/items/{listingId}]
POST            /store/products/{id}/reviews         { rating, title, body, media[] }
POST            /store/reviews/{id}/helpful
POST            /store/products/{id}/questions
POST            /store/products/{id}/stock-subscription
```

### 3.7 Content
```
GET /store/content/pages/{slug}       → blocks
GET /store/content/home
GET /store/content/menus/{code}
GET /store/content/banners            ?placement=
GET /store/config                     → public store config (branding refs, currency, policies,
                                        feature flags, enabled payment methods)
GET /store/pincodes/{pincode}         → city/state autofill + serviceability
```

---

## 4. Admin & Vendor Endpoints (representative)

```
# Catalog
GET/POST/PUT/DELETE /admin/categories | /brands | /attributes | /attribute-sets
GET/POST/PUT        /admin/products[/{id}]         (+ /submit, /approve, /reject, /archive)
GET/POST/PUT        /admin/products/{id}/variants[/{variantId}]
POST                /admin/products/import         (multipart) → job id
GET                 /admin/jobs/{id}               → import/export progress + error report
GET/POST/PUT        /admin/listings[/{id}]          (vendor-scoped)

# Inventory
GET   /admin/stock                                  ?warehouseId= &lowStock=
POST  /admin/stock/adjustments                      { stockItemId, change, reason, note }
GET   /admin/stock/{id}/ledger
GET/POST /admin/purchase-orders[/{id}]              (+ /receive)
GET/POST /admin/stock-takes[/{id}]                  (+ /submit)
GET/POST /admin/warehouses

# Orders & fulfilment
GET   /admin/orders                                 ?status= &vendorId= &from= &to= &q=
GET   /admin/orders/{id}
POST  /admin/sub-orders/{id}/transition             { toStatus, reason }
POST  /admin/sub-orders/{id}/shipments              { lines[], weight, dimensions, courier }
GET   /admin/shipments/{id}/label | /manifest       → PDF
POST  /admin/shipments/{id}/schedule-pickup | /cancel
GET   /admin/ndr                                    → queue
POST  /admin/ndr/{id}/action                        { action, remark }

# Payments
GET   /admin/payments                               ?status= &method= &provider= &orderId=
                                                      &from= &to= &q= (order number, gateway id)
GET   /admin/payments/{id}                          → the collection, its attempts and its refunds
POST  /admin/payments/{id}/sync                     re-fetches the payment from the gateway and
                                                      applies what it says. The repair for a lost
                                                      webhook, and the only way an operator moves
                                                      a payment at all
POST  /admin/payments/{id}/capture                  { amount? } captures an authorised payment
POST  /admin/payments/{id}/refunds                  (Idempotency-Key) { amount, reason, speed? }
GET   /admin/refunds                                ?status= &from= &to=
POST  /admin/refunds/{id}/approve                   the second signature, above the threshold
POST  /admin/refunds/{id}/reject                    { reason }
POST  /admin/refunds/{id}/sync                      re-fetches the refund from the gateway

GET   /admin/gateway-events                         ?status= &type= &from= — the webhook log and
                                                      the dead-letter queue, one list
GET   /admin/gateway-events/{id}                    → the raw payload as it arrived
POST  /admin/gateway-events/{id}/replay             re-queues a failed or dead-lettered event

GET   /admin/settlements                            ?from= &to=
GET   /admin/settlements/{id}                       → the report and its reconciliation counts
GET   /admin/settlements/{id}/entries               ?matchStatus= — the mismatches, addressable
POST  /admin/settlements/import                     { from, to } → pulls reports from the gateway
POST  /admin/payments/reconcile                     runs the reconciliation sweep now

GET   /admin/cod-collections                        ?status= &vendorId= &from= &to=
POST  /admin/cod-collections/{id}/collect           { amount, collectedAt } cash taken at the door
POST  /admin/cod-collections/remit                  { ids[], reference, amount, remittedAt } the
                                                      courier's remittance, matched in one batch

# Returns
GET   /admin/returns                                ?status=
POST  /admin/returns/{id}/approve | /reject | /schedule-pickup
POST  /admin/returns/{id}/qc                        { result, disposition, notes }
POST  /admin/returns/{id}/refund                    { mode, amount }

# Pricing & promotions
GET/POST/PUT /admin/price-lists[/{id}]
GET/POST/PUT /admin/promotions[/{id}]               (+ /activate, /deactivate)
POST         /admin/promotions/simulate             { cart } → discount preview
GET/POST/PUT /admin/tax-rates

# Vendors & settlements
GET/POST/PUT /admin/vendors[/{id}]                  (+ /approve, /suspend, /activate)
GET/POST     /admin/vendors/{id}/kyc-documents      (+ /verify)
GET/POST/PUT /admin/commission-plans
GET          /admin/settlements/cycles              ?vendorId= &period=
POST         /admin/settlements/cycles/{id}/close
GET          /admin/vendors/{id}/ledger             ?from= &to=
POST         /admin/payout-batches                  { cycleIds[] }
POST         /admin/payout-batches/{id}/approve | /process
GET          /admin/reports/tcs-tds                 ?period=

# Content
GET/POST/PUT /admin/pages[/{id}]                    (+ /publish, /schedule, /versions, /rollback)
GET/POST/PUT /admin/banners | /menus | /collections | /redirects

# Platform
GET/PUT      /admin/settings
GET/PUT      /admin/feature-flags
GET/POST/PUT /admin/users | /roles
GET          /admin/audit-logs                      ?entityType= &entityId= &actorId=
GET          /admin/reports/{reportKey}             ?from= &to= &groupBy= &format=json|csv

# Media
GET          /admin/media                           ?visibility= &contentType= &cursor= &size=
POST         /admin/media                           (multipart) -> { fileId, url, variants }
GET          /admin/media/{id}
GET          /admin/media/{id}/link                 -> { url, expiresAt }  short-lived, private files
DELETE       /admin/media/{id}                      soft delete; the object is removed too

# Notifications
GET          /admin/notification-templates          ?channel= &eventKey=
GET/PUT      /admin/notification-templates/{id}
GET          /admin/notifications                   ?status= &channel= &eventKey= &from= &to=
GET          /admin/notifications/{id}
POST         /admin/notifications/{id}/retry        re-queues a failed message
POST         /admin/notifications/test              { eventKey, channel, to, variables } - staff only
```

---

## 5. Webhooks (inbound)

```
POST /api/v1/webhooks/razorpay        X-Razorpay-Signature   (HMAC-SHA256 over raw body)
POST /api/v1/webhooks/shipping/{provider}
POST /api/v1/webhooks/sms/{provider}   delivery receipts
POST /api/v1/webhooks/email/{provider} bounces/complaints
```

Mandatory handling contract for every webhook:
1. Read the **raw body** before any model binding; verify the signature against it.
2. Persist the raw event immediately (`gateway_events`) — before processing.
3. Return `200` fast; process asynchronously via the outbox/worker.
4. De-duplicate on the provider event id; a replay is a no-op.
5. Reject events older than the configured skew window.
6. Never trust amounts or status from the payload alone for high-value transitions — re-fetch
   from the provider API before confirming an order.

**Razorpay specifically.** The signature is HMAC-SHA256 of the raw body under the webhook secret,
compared in constant time; the subscribed events are `payment.authorized`, `payment.captured`,
`payment.failed`, `order.paid`, `refund.created`, `refund.processed` and `refund.failed`, plus
`settlement.processed` for reconciliation. An event whose type is not subscribed is stored and
marked `Ignored` rather than refused — a `400` to a gateway is a retry storm, and an event nobody
handles today is evidence tomorrow. The endpoint answers `200` for a duplicate, a stale event and an
unhandled type alike, and `401` only for a signature that does not verify.

---

## 6. Cross-cutting Behaviours

**Pagination:** keyset (`cursor`) everywhere a list can grow unbounded; offset paging is allowed
only in admin screens that genuinely need page numbers, with a hard `maxOffset`.

**Filtering & sorting:** an explicit allow-list of filterable/sortable fields per endpoint.
No generic query language — it becomes an injection and performance liability.

**Partial responses:** `?fields=` is deliberately not supported; endpoints return
purpose-shaped DTOs instead (list DTO vs detail DTO).

**Bulk operations:** async by default — return `202` with a job id, poll `GET /admin/jobs/{id}`.

**File uploads:** `POST /admin/media` (multipart) → `{ fileId, url, variants }`. Max size,
MIME allow-list and dimension checks enforced server-side.

**Rate limits (defaults, per tenant, tunable):**

| Bucket | Limit |
|---|---|
| OTP request | 3 / 10 min per mobile, 20 / hour per IP |
| Auth (login/refresh) | 10 / min per IP |
| Storefront reads | 300 / min per IP |
| Cart mutations | 60 / min per session |
| Place order | 5 / min per customer |
| Admin writes | 120 / min per user |
| Webhooks | Unlimited but signature-gated and queued |

**Deprecation:** announced via `Sunset` and `Deprecation` headers plus release notes; minimum
one release of overlap.

---

## 7. OpenAPI & Client Generation

- Generated by ASP.NET Core 10's built-in OpenAPI support; every endpoint declares its response
  types, status codes, and examples.
- Enriched with `operationId` values that read well as client method names
  (`storeCartAddItem`, `adminOrdersTransitionSubOrder`).
- CI step: build API → export `openapi.json` → generate the Angular client into
  `libs/data-access/api` → fail the build if the generated output differs from what is committed.
  This makes a breaking API change impossible to merge unnoticed.
