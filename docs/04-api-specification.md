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
GET /store/products/{slug}                  → product + variants + buy-box listing + offers
GET /store/products/{slug}/offers           → all vendor listings for a variant
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
GET    /store/cart                         (cookie token or JWT)
POST   /store/cart/items                   { listingId, quantity }
PATCH  /store/cart/items/{lineId}          { quantity, savedForLater }
DELETE /store/cart/items/{lineId}
POST   /store/cart/merge                   (after login)
POST   /store/cart/coupon                  { code }
DELETE /store/cart/coupon
GET    /store/cart/quote                   → full itemised price + GST breakdown

POST   /store/checkout                     → creates checkout session
PUT    /store/checkout/{id}/address        { shippingAddressId, billingAddressId, gstin? }
GET    /store/checkout/{id}/shipping-options
PUT    /store/checkout/{id}/shipping       { perVendor: [{ vendorId, optionId }] }
PUT    /store/checkout/{id}/payment-method { method }           // prepaid | cod
GET    /store/checkout/{id}/review         → final quote, re-validated
POST   /store/checkout/{id}/place-order    (Idempotency-Key) → { orderId, payment }
```

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

### 3.4 Orders & returns
```
GET  /store/orders                          ?status= &cursor=
GET  /store/orders/{orderNumber}
GET  /store/orders/{orderNumber}/invoice    → PDF stream
POST /store/orders/{orderNumber}/sub-orders/{id}/cancel   { reason, lines? }
GET  /store/orders/{orderNumber}/tracking
POST /store/returns                         { subOrderId, lines[], type, reasonCode, media[] }
GET  /store/returns
GET  /store/returns/{returnNumber}
POST /store/payments/{id}/retry             → new payment session for a failed order
```

### 3.5 Engagement
```
GET/POST/DELETE /store/wishlist[/items/{listingId}]
POST            /store/products/{id}/reviews         { rating, title, body, media[] }
POST            /store/reviews/{id}/helpful
POST            /store/products/{id}/questions
POST            /store/products/{id}/stock-subscription
```

### 3.6 Content
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
