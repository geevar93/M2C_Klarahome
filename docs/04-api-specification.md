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
GET /store/products                         ?q= &category= &brand= &vendor= &minPrice= &maxPrice=
                                            &attr.color=beige &rating= &discount= &inStock=
                                            &sort= &cursor= &size=
                                            → served from the Search projection (Step 19), not from
                                              the catalog schema: the facet counts need it.
                                              `category` is a category id and matches that category
                                              and everything beneath it. `brand`, `vendor` and each
                                              `attr.*` accept a repeated parameter or one
                                              comma-separated value. `sort` is one of relevance |
                                              price-asc | price-desc | newest | discount | rating |
                                              popularity. Facets are returned on the first page
                                              only — they do not change as a shopper pages
GET /store/products/{slug}                  → product + variants + buy-box listing + offers,
                                              with the mandatory disclosures (MRP, net quantity,
                                              country of origin, manufacturer/importer)
GET /store/products/{slug}/offers           ?variantId= → all vendor listings for a variant, in
                                              buy-box order; omit variantId for the default variant
GET /store/vendors/{slug}                   → a seller's public profile; 404 unless they are Active
GET /store/products/{slug}/reviews          ?sort= &rating= &cursor=
GET /store/products/{slug}/questions
GET /store/search/suggest                   ?q= &limit= -> products, popular searches, brands
                                              and categories, in that order
POST /store/search/click                    { queryToken, position, variantId } -> 204. Anonymous,
                                              and the handle comes from the search response; it is
                                              what makes click-through measurable
GET /store/collections/{slug}
GET /store/listings/{id}/delivery-estimate  ?pincode=
```

`GET /store/products` returns `facets` alongside `items`. Each group's counts are computed with
**that group's own filter lifted and every other filter applied** — a shopper who has chosen beige
still has to be told how many creams there are, within whatever price band they also chose. The one
exception is `category`, which is a drill-down rather than a multi-select and therefore keeps its
own filter. The response also carries `query` (the search as it was interpreted, after stop words
and synonyms), `corrected` (true when the exact search matched nothing and these results came from
the fuzzy fallback) and `queryToken` (the handle `POST /store/search/click` reports against):
```json
"facets": [
  { "key": "brand",      "label": "Brand",     "values": [{ "value": "0192...", "label": "Klara", "count": 42 }] },
  { "key": "price",      "label": "Price",     "values": [{ "value": "0-499", "label": "0-499", "count": 120, "from": 0, "to": 499 }] },
  { "key": "rating",     "label": "Customer rating", "values": [{ "value": "4", "label": "4 & up", "count": 61, "from": 4 }] },
  { "key": "attr.color", "label": "Colour",    "values": [{ "value": "beige", "label": "Beige", "count": 18 }] }
]
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

### 3.4a Returns
```
GET    /store/sub-orders/{id}/returnable   → what may still be sent back from one seller's part,
                                             each line with how many units are left and what they
                                             are worth, plus the reasons this store accepts
GET    /store/returns/reasons              → the reason codes offered, with whether each needs a
                                             photograph and whether a courier collects
GET    /store/returns                      ?status= &cursor= &size= → the caller's own returns
POST   /store/returns                      { subOrderId, type?, reasonCode, reasonNote?,
                                             lines[], evidenceFileIds?, refundMode? }
GET    /store/returns/{id}                 → one return in full, with its lines and its evidence
POST   /store/returns/{id}/cancel          { reason? } withdraws a return not yet collected
GET    /store/returns/{id}/credit-note     → the credit note raised against it, when one has been
```

Every route is scoped to the caller's own returns by the token, never by a parameter, and an id
belonging to somebody else answers `404` exactly as an invented one does.

`returnable` is the question asked **before** a return exists, which is why it hangs off the
sub-order rather than off a return. It lists every line including the ones that cannot be sent back,
each with the reason — a screen that silently omitted them would leave the shopper hunting for an
item they can see in their order.

`lines` on a request is `[{ orderLineId, quantity }]`. The same line twice is a validation failure
rather than a sum: a shopper naming it twice means a quantity, and guessing which they meant is
worse than asking.

**The eligibility window is resolved from three policies in a fixed order** — the product's own
window frozen on the order line, then the seller's promise, then the store's default
(`CommerceSettings.ReturnWindowDays`). The first with an opinion wins, deliberately not the
shortest: the shortest is not what the shopper read. A product sold as non-returnable is the one
veto in the chain, because that was a mandatory listing disclosure. The refusals are
`RETURN_WINDOW_CLOSED`, `RETURN_ITEM_NOT_RETURNABLE` and `RETURN_NOT_DELIVERED`, and they are
distinct because a storefront shows a different screen for each.

A return that the reason code or the store's value threshold auto-approves is approved in the same
request, so a shopper whose reason the business trusts is answered immediately.

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

### 3.6 Engagement (Step 21)
```
# Reviews. Reading is anonymous and cacheable; writing needs an account and a delivered purchase.
GET          /store/products/{productId}/reviews     ?rating= &withImages= &sort= &cursor= &size=
GET          /store/products/{productId}/rating      -> average + the 1..5 histogram
GET          /store/products/{productId}/reviews/eligibility
POST         /store/products/{productId}/reviews     { orderLineId, rating, title, body, images[] }
PUT          /store/reviews/{id}                     rewrites; returns to moderation
POST/DELETE  /store/reviews/{id}/helpful             { isHelpful }
GET          /store/me/reviews                       ?cursor= &size=   pending + refused included

# Product Q&A, behind reviews.questions (ships OFF).
GET          /store/products/{productId}/questions   ?unanswered= &cursor= &size=
POST         /store/products/{productId}/questions   { body }
POST         /store/questions/{id}/answers           { body }

# Wishlist.
GET          /store/wishlist                         ?listId=   cards priced from today's buy box
GET          /store/wishlist/lists
POST         /store/wishlist/items                   { variantId, wishlistId?, note?, priority }
DELETE       /store/wishlist/items/{variantId}       ?listId=
POST/PUT/DELETE /store/wishlist/lists[/{id}]         { name }
POST         /store/wishlist/lists/{id}/share        { share }  mints a token; off revokes it
GET          /store/wishlist/shared/{token}          anonymous; no notes and no token in the reply

# Stock alerts, behind reviews.stock-alerts (ships OFF).
POST         /store/stock-subscriptions              { variantId, kind, targetPrice?, email? }
GET          /store/stock-subscriptions              ?activeOnly=
DELETE       /store/stock-subscriptions/{id}

# Reporting abuse. Anonymous, and deliberately behind no feature flag at all.
POST         /store/content-reports                  { target, targetId, reason, note? }
```

**`POST /store/products/{productId}/reviews` takes an `orderLineId` and the product in the route is
not used.** The product a review is filed against comes from the purchase the caller quotes, resolved
through `IOrderPurchases` — which is the only version of it a caller cannot choose. A caller who
could name their own product would be able to write a five-star review of anything by quoting one
line they genuinely bought. The four ways to fail the rule — no such line, somebody else's line, a
cancelled line, a line still in transit — all answer `REVIEW_PURCHASE_REQUIRED`, because telling them
apart would confirm which order line ids are real.

**Two endpoints are anonymous *writes*, and both are rate limited harder than anything else here.**
Subscribing to a stock alert, because somebody who has not signed up yet is exactly the person a
back-in-stock alert is for; and reporting content, because a store that made itself hard to tell
about unlawful content would be a worse store. Neither publishes anything: a report goes into a
queue a moderator works, and a subscription sends one message to an address the caller supplied.

**There is no way to read an unmoderated review, question or answer from this surface, and no query
parameter that changes that.** An author reading their own pending review does it through
`/store/me/reviews` with their own token.

### 3.7 Content
```
GET /store/content/pages/{slug}       → blocks, resolved and windowed to now
GET /store/content/home
GET /store/content/menus/{code}       → nested, every target resolved into a path
GET /store/content/banners            ?placement= live now, for this visitor's audience
GET /store/content/blog               ?tag= &cursor= &size=   behind `content.blog`
GET /store/content/blog/{slug}
GET /store/content/redirects/resolve  ?path= -> { statusCode, location } on a 404
GET /store/content/seo/config         → canonical origin, title template, indexing switch
GET /store/content/seo/robots         → text/plain, served verbatim at /robots.txt
GET /store/content/seo/sitemap        → the sitemap index
GET /store/content/seo/sitemap/{section} ?page=   pages|blog|collections|categories|products
GET /store/content/seo/structured-data ?path= -> the schema.org @graph for one path
GET /store/config                     → public store config (branding refs, currency, policies,
                                        feature flags, enabled payment methods)
GET /store/pincodes/{pincode}         → city/state autofill (platform reference data)
```

### 3.8 Delivery
```
GET /store/shipping/serviceability/{pincode}
```
Anonymous, cached, and read on the PDP and in the cart. It answers **both** questions an address has
to pass (ADR-018) and says which one failed:

```json
{
  "pincode": "500081",
  "deliverable": true,
  "covered": true,
  "isServiceable": true,
  "prepaidOk": true,
  "codOk": true,
  "etaDays": 3,
  "courier": "Delhivery",
  "city": "Hyderabad",
  "state": "Telangana",
  "reason": null,
  "message": null,
  "checkedAt": "2026-09-06T04:10:00Z"
}
```

`deliverable` is `covered && isServiceable` and is the only field a storefront needs to decide what
to show. When it is false, `reason` carries `DELIVERY_AREA_NOT_COVERED` — the store does not sell
there, with the operator's own `message` beside it — or `PINCODE_NOT_SERVICEABLE`, meaning no courier
will carry it. The route **never calls a courier**; it reads the serviceability cache and the
coverage settings (`08-integrations.md` §2.3).

The same two checks are applied at `PUT /store/checkout/{id}/address`, in cart validation, in
`GET /store/checkout/{id}/shipping-options` (an uncovered destination is offered none) and at
`place-order`, each refusing with the same codes. `GET /store/config` carries the public coverage
summary so the storefront can state where the store delivers before anyone types a PIN code.

---

## 4. Admin & Vendor Endpoints (representative)

```
# Catalog
GET/POST/PUT/DELETE /admin/categories | /brands | /attributes | /attribute-sets
GET/POST/PUT        /admin/products[/{id}]         (+ /submit, /approve, /reject, /archive)
GET/POST/PUT        /admin/products/{id}/variants[/{variantId}]
POST                /admin/products/import         (multipart) → job id
POST                /admin/products/bulk-status    { productIds[], status, reason? } → per-id outcome
                                                     one moderation transition applied to at most 200
                                                     products. Answers 200 with a row per id saying
                                                     whether it moved and why not — a partial result,
                                                     because a batch that fails whole because one
                                                     product was already archived is a batch an
                                                     operator has to un-pick by hand
GET                 /admin/products/import-template → { fileName, columns[] } — the header row as
                                                     data, not a CSV byte stream: the browser writes
                                                     the file, so no download endpoint has to be
                                                     authenticated through a bare <a> tag
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
GET   /admin/sub-orders                             ?status= &vendorId= &warehouseId= &overdueOnly=
                                                      the fulfilment worklist. warehouseId narrows it
                                                      to the parcels one location is packing, off the
                                                      warehouse each line was allocated from
POST  /admin/sub-orders/{id}/transition             { toStatus, reason }
POST  /admin/sub-orders/{id}/shipments              { lines[], weight, dimensions, courier }
GET   /admin/shipments/pick-list                    ?vendorId= &warehouseId= &size= — one row per
                                                      item waiting to be packed, soonest deadline
                                                      first, each naming its stock location
GET   /admin/shipments/{id}/label                   → { url, expiresAt, fileName } — a short-lived
                                                      link, not the PDF. Minting the link is the
                                                      grant, and it is what lets a label open in a
                                                      new tab without putting a bearer token there
GET   /admin/shipments/{id}/manifest                → PDF
POST  /admin/shipments/{id}/schedule-pickup | /cancel
GET   /admin/ndr                                    → queue
POST  /admin/ndr/{id}/action                        { action, remark }
GET   /admin/shipping/serviceability/{pincode}      → the cached answer, its age and its courier
POST  /admin/shipping/serviceability/{pincode}/refresh
                                                    asks the courier now and rewrites the cache;
                                                      the only operator-triggered live call
GET   /admin/shipping/coverage                      → the delivery-coverage policy as it stands
GET   /admin/shipping/coverage/test/{pincode}       → would this address be accepted, and if not,
                                                      which of the two checks refused it

# The delivery area is EDITED through PUT /admin/settings/delivery-coverage, where every other store
# policy is edited and where the audit trail and the validator already live. A second write path for
# one settings row would be a second place to get the audit wrong.

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
GET   /admin/returns                                ?status= &vendorId= &orderId= &from= &to=
GET   /admin/returns/{id}                           → one in full, with what may be done to it next
GET   /admin/returns/{id}/credit-note               → the credit note raised against it
POST  /admin/returns/{id}/approve                   { amount?, pickupRequired?, note? }
POST  /admin/returns/{id}/reject                    { reason }
POST  /admin/returns/{id}/schedule-pickup           { pickupAt?, manualAwb?, manualCourier? }
POST  /admin/returns/{id}/receive                   { note? } books the parcel in at the warehouse
POST  /admin/returns/{id}/qc                        { result, disposition?, notes?, lines[] }
POST  /admin/returns/{id}/refund                    { mode, amount }
POST  /admin/returns/{id}/replace                   { replacementOrderId?, note? }
POST  /admin/returns/{id}/close                     { note? } closes one with nothing owed

GET/POST/PUT /admin/return-reasons[/{id}]           the reason codes and the policy each carries
GET          /admin/credit-notes                    ?vendorId= &financialYear= &from= &to=
GET          /admin/credit-notes/{id}               → one, with its tax split

# Pricing & promotions
GET/POST/PUT /admin/price-lists[/{id}]
GET/POST/PUT /admin/promotions[/{id}]               (+ /activate, /deactivate)
POST         /admin/promotions/simulate             { cart } → discount preview
GET/POST/PUT /admin/tax-rates

# Vendors & settlements
GET/POST/PUT /admin/vendors[/{id}]                  (+ /approve, /suspend, /activate)
GET/POST     /admin/vendors/{id}/kyc-documents      (+ /verify)
GET/POST/PUT /admin/commission-plans

# Settlements (Step 18). Every route is vendor-scoped by the caller's token, never by an id in the
# query string: a seller reading their own statement and finance reading everybody's run the same
# query, and the only difference is whether the caller carries a vendor id. There is no storefront
# surface at all.
GET   /admin/settlements/cycles                     ?vendorId= &status= &from= &to= &cursor= &size=
GET   /admin/settlements/cycles/{id}
POST  /admin/settlements/cycles/{id}/close          { force? } totals a period and applies TCS/TDS
POST  /admin/settlements/cycles/close               { vendorId, force? } closes the period now due
                                                      for one seller, opening the cycle if the
                                                      scheduler has not
GET   /admin/settlements/ledger                     ?vendorId= &entryType= &cycleId= &from= &to=
POST  /admin/settlements/adjustments                { vendorId, direction, amount, reason }
                                                      an append, never an edit; the reason is
                                                      required and shows on the seller's statement
GET   /admin/settlements/commission-invoices         ?vendorId= &financialYear= &from= &to=
GET   /admin/settlements/commission-invoices/{id}    -> one, with its tax split by head
GET   /admin/settlements/commission-invoices/{id}/download
                                                      -> { url, expiresAt, fileName }, short-lived
# The platform's OWN tax invoice, raised on the seller for the commission and fees a closed cycle
# charged. It is the mirror of the seller's sale invoice and is a legal obligation, not a statement:
# commission is a taxable service (SAC 998599), so it needs an invoice number from a financial-year
# series, a place of supply, and a CGST/SGST-or-IGST split decided by the seller's state against the
# platform's. The settlement statement shows what was deducted; only this says what was billed.
GET   /admin/vendors/{id}/ledger                    ?from= &to= -> opening, movements, closing
GET   /admin/vendors/{id}/ledger/export             ?from= &to= -> text/csv
GET   /admin/vendors/{id}/balance                   -> owed now, unsettled, awaiting payout

GET   /admin/payout-batches                         ?status= &from= &to= &cursor= &size=
GET   /admin/payout-batches/{id}                    -> the run, its transfers, and what may be done
                                                      to it next, taken off the transition table
POST  /admin/payout-batches                         { cycleIds[] } builds a draft; sends nothing
POST  /admin/payout-batches/{id}/approve            never by the person who raised it
POST  /admin/payout-batches/{id}/process            resumable: call again for a large batch
POST  /admin/payout-batches/{id}/cancel             { reason } only before anything has been sent

GET   /admin/reports/tcs-tds                        ?from= &to= &vendorId=
GET   /admin/reports/tcs-tds/export                 -> text/csv, for the GSTR-8 and 26Q/27EQ filings
GET   /admin/reports/platform-revenue               ?from= &to= -> what the sellers were charged

# Content (Step 20)
GET/POST/PUT/DELETE /admin/pages[/{id}]             ?search= &type= &status= &cursor= &size=
GET          /admin/pages/block-types               the block schemas the admin editor draws from
POST         /admin/pages/{id}/transition           { status, scheduledAt, note } the one workflow
                                                      door: submit, publish, schedule, unpublish,
                                                      archive. Needs content.page.publish
GET          /admin/pages/{id}/versions[/{version}] the history, newest first
POST         /admin/pages/{id}/versions/{v}/rollback restores content as a NEW version; never
                                                      changes the status
GET          /admin/pages/{id}/preview              ?version= renders as the storefront would,
                                                      whatever the status. The preview is here, on
                                                      the caller's own token, rather than a
                                                      guessable token on the store surface
GET/POST/PUT/DELETE /admin/menus[/{id}]             the whole tree in one call
GET/POST/PUT/DELETE /admin/banners[/{id}]           (+ /{id}/active to switch one off in seconds)
GET/POST/PUT/DELETE /admin/collections[/{id}]       (+ /{id}/rule, /{id}/refresh)
GET          /admin/collections/{id}/items          the resolved cards, rule-driven or hand-picked
PUT          /admin/collections/{id}/items          { productIds[] } replaces the whole hand-picked
                                                      set, in the order given
POST         /admin/collections/{id}/items          { productId, position? } adds one
DELETE       /admin/collections/{id}/items/{productId}
                                                      removes one. PUT alone made "drop this product"
                                                      a read-modify-write of the entire list, which is
                                                      two operators overwriting each other
GET/POST/PUT/DELETE /admin/redirects[/{id}]         own permission: a redirect is routing
GET          /admin/seo/robots | /seo/sitemap       what a crawler is served, before it is
GET          /admin/seo/structured-data             ?path=

# Search (Step 19)
GET/POST     /admin/search/synonyms                 ?search= &activeOnly= &cursor= &size=
PUT/DELETE   /admin/search/synonyms/{id}            single words only; a phrase synonym is refused
GET/POST     /admin/search/stop-words               the store's own noise words, on top of the
                                                      english dictionary's
PUT/DELETE   /admin/search/stop-words/{id}
GET          /admin/search/queries                  ?from= &to= &source= &size= -> what shoppers
                                                      searched for, with click-through and average
                                                      click position
GET          /admin/search/queries/zero-results     the buying team's list: searched for, not found
GET          /admin/search/index                    -> counts, staleness, and which engine answers
POST         /admin/search/index/rebuild            { afterVariantId?, maxVariants?, variantIds? }
                                                      bounded and resumable; answers with where it
                                                      reached. Naming variantIds rebuilds only those

# There is deliberately no endpoint that edits an index row. Every column in the projection belongs
# to another module, so a hand correction would fix a symptom, leave the source wrong, and be
# overwritten by the next event. The rebuild is the supported repair, and it repairs by re-reading.

# Platform
GET/PUT      /admin/settings
GET          /admin/settings/schema                 every section's fields, their kinds, and the
                                                      bounds each value must satisfy — read off the
                                                      CLR type and the section's FluentValidation
                                                      rules, so it cannot drift from what the server
                                                      enforces. Advisory only: the write is validated
                                                      regardless, and the schema exists so the form
                                                      draws the right control instead of a textarea
GET/PUT      /admin/feature-flags
GET/POST/PUT /admin/users | /roles
POST         /admin/users/{id}/impersonate          { reason, device } → a short-lived access token
                                                      for that user and nothing else. No refresh
                                                      token is issued and the session cannot be
                                                      refreshed, so the window is the window. Both
                                                      the start and the end are audited with the
                                                      reason, and the token carries impersonator_id
DELETE       /admin/impersonation/{sessionId}       ends it now rather than waiting for the clock
GET          /admin/audit-logs                      ?entityType= &entityId= &actorId=

# Reference data (states, PIN codes) is deliberately NOT mapped again under /admin. The generated
# client is grouped by tag rather than by surface, so the back office calls GET /store/states and
# GET /store/pincodes/{pincode} directly. A second copy would have been two routes wanting no
# permission on a surface where every other route must declare one.
# Reviews & Q&A (Step 21). Reading includes what is pending and what was refused, because "where
# has my review gone" is not answerable from the storefront's view of the world. Moderating is the
# only permission that can take something down and is deliberately NOT a seller's; replying is a
# seller's and cannot remove anything. The list endpoints serve a seller and a moderator through the
# same route, confined by whether the caller's token carries a vendor id.
GET          /admin/reviews                          ?status= &productId= &vendorId= &rating= &reported=
GET          /admin/reviews/{id}
POST         /admin/reviews/{id}/moderate            { approve, note }   note required to refuse
POST         /admin/reviews/{id}/reply               { reply }           own sale only
GET          /admin/questions                        ?status= &productId= &unanswered=
POST         /admin/questions/{id}/moderate          { approve, note }
POST         /admin/questions/{id}/answers           { body }
POST         /admin/questions/{id}/answers/{answerId}/moderate  { approve }
GET          /admin/content-reports                  ?status= &reason=   unlawful first, then oldest
POST         /admin/content-reports/{id}/resolve     { uphold, resolution }  upholding takes it down

# Reporting (Step 21). No storefront surface, and there never will be: every number here is
# commercial. Every route is vendor-scoped by the caller's TOKEN, never by an id in the query
# string; a report the catalogue declares as not vendor-scoped is refused to a seller outright.
GET          /admin/reports                          the declared catalogue: columns and groupings
GET          /admin/reports/{reportKey}              ?from= &to= &groupBy= &vendorId=  -> the table
POST         /admin/reports/{reportKey}/export       { from, to, groupBy, vendorId } -> a run
GET          /admin/report-runs                      ?reportKey= &status= &cursor= &size=
GET          /admin/report-runs/{id}/download        -> { url, expiresAt, fileName }  short-lived
GET/POST     /admin/report-schedules
PUT/DELETE   /admin/report-schedules/{id}

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

**`format=csv` does not stream a file.** It produces a run through the same code the scheduler uses,
stores it, and answers with the run — which the caller then downloads through
`/admin/report-runs/{id}/download`. The indirection is deliberate: a report somebody clicked for and
one that arrives by email every Monday are then the same artefact, produced once and recorded in one
log. The download link is minted per request and lives fifteen minutes by default, because a report
is a private document and a durable link is one somebody pastes into a chat thread.

**Returns split across four permissions**, and the split is the split between four jobs that are
usually four people. `returns.return.read` is support. `returns.return.manage` is the queue —
approve, refuse, book a collection, close — and a seller holds it for their own goods.
`returns.qc.manage` is the receiving bay, and it is deliberately **not** a seller's: grading decides
whether a shopper is refunded and whether a seller is charged, and a seller who could grade their
own returns would be deciding their own liability. `returns.refund.manage` is finance, and it sits
on top of the refund approval threshold rather than replacing it. `returns.reason.manage` is
staff-only, because who pays the freight and what is auto-approved are commercial decisions.

`receive` and `qc` are two calls rather than one on purpose. The parcel arriving and somebody
opening it are different days and different people, and a platform that collapsed them could not
answer how much is sitting in the receiving bay uninspected — the number a returns operation is
actually run on.

`qc` takes `lines` as `[{ returnLineId, quantityAccepted, disposition?, note? }]`, with an empty
list meaning "all of it, at the store's default disposition". A `disposition` is one of `Restock`,
`Scrap` or `Quarantine`; quarantine moves no stock at all, because goods that are physically present
and commercially undecided are neither back on sale nor written off. Where
`ReturnsSettings.AutoRefundOnQcPass` is on, a pass refunds and raises the credit note in the same
request.

---

## 5. Webhooks (inbound)

```
POST /api/v1/webhooks/razorpay        X-Razorpay-Signature   (HMAC-SHA256 over raw body)
POST /api/v1/webhooks/shipping/{provider}   {provider} is the adapter key — `shiprocket` in v1
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

**Shiprocket specifically.** There is no HMAC. Shiprocket sends a shared secret in an `x-api-key`
header, set by the merchant in its dashboard and held here as `Shipping:WebhookSecret`; it is
compared in constant time and everything else in the contract above is unchanged. A deployment with
no webhook secret cannot verify origin, so it stores the event, marks it `Ignored` and answers
`401` — and the 30-minute tracking poll is what keeps parcels moving in the meantime.

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
| Auth (login/refresh) | 30 / min per IP |
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
- Query parameters are declared in `camelCase`, by a document transformer rather than by an
  attribute on each property. A query record bound with `[AsParameters]` is otherwise declared under
  its C# property names, and since the document is what the client is generated from, the declared
  spelling is the spelling that goes on the wire — `?Q=chair&MinPrice=2000` on the storefront's
  most-shared URL, against the `camelCase` this document states in §1. The binder matches
  case-insensitively either way, so this changes what is generated, not what is accepted.
- CI step: build API → export `openapi.json` → generate the Angular client into
  `libs/data-access/api` → fail the build if the generated output differs from what is committed.
  This makes a breaking API change impossible to merge unnoticed.
