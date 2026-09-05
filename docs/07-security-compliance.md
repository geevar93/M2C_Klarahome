# 07 — Security, Privacy & Indian Compliance

---

## 1. Authentication

| Actor | Primary method | Secondary | MFA |
|---|---|---|---|
| Customer | Mobile + OTP (6-digit, 5-min TTL) | External identity provider; email + password | Optional TOTP |
| Vendor staff | Email + password | — | **Mandatory TOTP** |
| Platform staff | Email + password | — | **Mandatory TOTP** |

**External identity providers (customers only).** A shopper may sign in with Google, and later
with Facebook, through the **server-side authorization-code flow with PKCE**: the client secret
stays on the API, the token and userinfo endpoints come from a pinned authority's discovery
document rather than from anything a caller supplied, and the browser only ever sees a redirect.
Staff and vendor users are deliberately excluded — a compromised Google account must not reach
`platform-admin`, and this deployment trusts no staff directory (ADR-014).

Account linking is narrow, because linking by email address is both the standard approach and the
standard account-takeover vector:

1. A known `(provider, subject)` pair signs that user in. The subject is the provider's stable
   identifier and is the only thing the link is keyed on.
2. Otherwise, an email the provider states is **verified**, matching an existing user, links to
   that user. An unverified email links to nothing.
3. Otherwise a new customer account is created, with the email already verified — the provider
   made a stronger assertion than our own verification link would have.
4. A mobile number is never a linking key.

Unlinking is refused when the provider is the account's only remaining credential.

**Degraded operation without a delivery provider.** SMS and transactional email are paid
dependencies, and a deployment may run before they exist. Four flags in `platform.feature_flags`
turn the features that need them off at runtime; a disabled feature answers `404 FEATURE_DISABLED`
and nothing about it is removed from the code:

| Flag | Turns off | While off |
|---|---|---|
| `identity.mobile-otp-login` | `/store/auth/otp/*` | Customers use an identity provider, or email + password |
| `identity.email-verification` | The verification send, and confirming an email | An address stays unverified unless a provider asserted it |
| `identity.password-reset-email` | `/auth/password/forgot` and `/reset` | An administrator issues a temporary password |
| `identity.external-login` | The whole external sign-in surface | Mobile OTP and email + password only |

**Temporary passwords.** While email delivery is off, an administrator holding
`identity.user.manage` may set a password for another account. The account is then marked as owing
a change: the next sign-in returns a `password-change-required` challenge rather than a session,
using the same challenge mechanism as the second factor, and every existing session is revoked.
The act is audited. It is a knowingly weaker control than a reset link — an administrator briefly
knows a credential that signs in as somebody else — and it is withdrawn when email returns.

**Token model**
- Access token: JWT, 15 minutes, signed RS256 (key in Docker secret, rotatable with an
  overlapping `kid` window). Claims: `sub`, `tenant_id`, `user_type`, `vendor_id?`,
  `permissions` (or a compact role reference resolved server-side if the claim grows large),
  `session_id`, `jti`.
- Refresh token: opaque, 30 days, **rotated on every use**, stored hashed, bound to a session
  record with device/IP. Reuse of a rotated token revokes the entire chain and raises a
  security alert.
- Delivered in an `HttpOnly; Secure; SameSite=Lax` cookie scoped to the API host; the access
  token lives **in memory only** on the client — never `localStorage`.
- Logout revokes the session server-side; "log out everywhere" revokes all sessions.

**Password policy:** minimum 10 characters, checked against a breached-password list,
Argon2id hashing (memory-hard parameters tuned at Step 7), no forced rotation, no composition
rules theatre.

**OTP hardening:** hashed at rest, single use, max 5 verification attempts, 3 requests per
10 minutes per mobile, 20 per hour per IP, exponential back-off, never logged, never returned
in any response.

> From Step 8 the code is delivered by the Notifications module and is **not persisted either**:
> a template marked sensitive has its rendered body handed to the provider and to nothing else, and
> `notification_messages.payload` keeps the variable names with their values redacted (ADR-017).

**Brute-force & abuse:** progressive lockout, CAPTCHA on repeated failure, device
fingerprint-lite risk signals feeding COD eligibility and review-posting rules.

---

## 2. Authorisation

- **Permission-based**, not role-based, at the enforcement point:
  `[RequirePermission("orders.suborder.cancel")]`. Roles are only bundles of permissions and
  are editable data — a redistributed deployment can define its own roles without code changes.
- **Scope enforcement in the data layer.** Vendor-scoped and customer-scoped queries apply
  their filter inside the repository/DbContext, not the controller. A developer who forgets a
  filter gets no data rather than everyone's data.
- Every endpoint is deny-by-default; an endpoint without an explicit policy fails an
  architecture test.
- An **authorisation matrix test** (every endpoint × every role) runs in CI and is a Step 29
  acceptance criterion.
- Object-level checks on every `{id}` route: does this resource belong to the caller's scope?
  A 404 (not 403) is returned for out-of-scope resources so existence is not leaked.
- Admin impersonation of a customer is permitted for support, is time-boxed, requires a
  reason, is fully audited, and is visibly flagged in the session.

---

## 3. Application Security Controls

| Threat | Control |
|---|---|
| Injection | Parameterised queries only; EF Core or Dapper with parameters; no dynamic SQL from user input; strict allow-lists for sort/filter fields |
| XSS | Angular's default escaping; `DomSanitizer` for the only intentional HTML (CMS rich text), sanitised **server-side first**; CSP with nonces, no `unsafe-inline`/`unsafe-eval` |
| CSRF | Bearer tokens for state-changing calls (not cookie-authenticated) + `SameSite` cookie for refresh + origin checks on the refresh endpoint |
| Mass assignment | Explicit request DTOs; entities are never model-bound |
| IDOR | Scope filters + object-level authorisation; UUIDv7 ids (unguessable, non-enumerable) |
| SSRF | Outbound allow-list for webhook/callback URLs; no user-supplied URL fetching. The identity provider is the first outbound call: its endpoints come from a pinned authority's discovery document, and the post-sign-in `returnUrl` is checked against a configured allow-list so the redirect cannot be aimed elsewhere |
| File upload | MIME + magic-byte validation, extension allow-list, size caps, image re-encode on ingest (strips EXIF and embedded payloads), stored outside the web root, served from a separate host, virus-scan hook |
| Rate abuse | Per-IP, per-user, per-endpoint limits (see `04-api-specification.md` §6) |
| Enumeration | Uniform responses on login/OTP/forgot-password regardless of account existence |
| Secrets | Docker secrets / SOPS; never in the repo, images, logs or client bundles; secret scanning in CI |
| Dependencies | Automated update PRs, CVE gate in CI, SBOM generated per release |
| Headers | HSTS, `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, CSP, `frame-ancestors 'none'` (except the Razorpay checkout frame allowance) |
| Logging hygiene | Structured logging with PII masking; no tokens, OTPs, card data, full mobile numbers or addresses in logs |

Target: **OWASP ASVS Level 2**, verified as a Step 29 acceptance criterion. OWASP Top 10 is
mapped to specific controls and tests in the Step 29 checklist.

---

## 4. Payment Security

- **We never see card data.** Razorpay's hosted/standard checkout collects it; our servers
  receive only tokens and identifiers. This keeps the merchant in **PCI-DSS SAQ-A** scope.
- No storage of PAN, CVV, or full card numbers under any circumstances. Card tokenisation and
  RBI mandate compliance remain Razorpay's responsibility, contractually confirmed at Step 0.
- Webhook integrity: HMAC-SHA256 over the **raw** body, constant-time comparison, timestamp
  skew window, replay protection via unique event id, raw payload persisted before processing.
- Amount verification: before confirming an order, the captured amount is re-fetched from
  Razorpay's API and compared to the order total. Webhook-reported amounts alone never confirm
  an order.
- Refunds require a permission plus an audit reason; refunds above a configurable threshold
  require a second approver (maker–checker).
- Payout approval is always maker–checker, with the batch total displayed and confirmed.

---

## 5. Data Protection & Privacy (DPDP Act 2023)

**Classification**

| Class | Examples | Handling |
|---|---|---|
| Sensitive | Password hashes, TOTP secrets, bank accounts, KYC documents, API secrets | Encrypted at rest (column-level or KMS-wrapped), never logged, access-audited |
| Personal | Name, mobile, email, addresses, order history, IP | Encrypted in transit, masked in logs and admin lists, access controlled |
| Business | Products, prices, stock, CMS | Standard controls |
| Public | Published catalog and content | Cacheable |

**DPDP obligations implemented**
1. **Notice & consent** — clear purpose statement at collection; separate, unbundled opt-in for
   marketing; consent timestamped and versioned; withdrawal as easy as granting.
2. **Purpose limitation** — each PII field's purpose recorded in the data inventory maintained
   from Step 6.
3. **Data principal rights** — self-service **access/export** (machine-readable) and
   **erasure/correction** endpoints; erasure anonymises rather than deletes where records must
   survive for tax law (orders and invoices are retained 8 years, with personal identifiers
   replaced by a pseudonym).
4. **Retention** — defined per data class (`03-database-design.md` §8); automated purge jobs.
5. **Breach readiness** — incident response runbook, notification templates, and a defined
   72-hour escalation path (Step 31).
6. **Processors** — Razorpay, courier, SMS/email providers documented in a processor register
   with their data-sharing scope; DPAs collected at Step 0.
7. **Children's data** — not knowingly collected; no under-18 targeting.
8. **Cross-border** — data resides in India by default; any transfer is a documented decision.

---

## 6. Indian Regulatory Compliance (commerce)

| Requirement | Implementation |
|---|---|
| **GST** | HSN per product, rate table with effective dating, place-of-supply logic, CGST/SGST vs IGST split, tax-inclusive display with breakdown on invoice, per-vendor GSTIN on each invoice |
| **Invoice numbering** | Gapless, sequential, per vendor per financial year (Apr–Mar), never reused, never deleted; credit notes on their own gapless series |
| **E-invoicing (IRN)** | Schema fields (`irn`, `qr_payload`, `ack_no`) present from Step 14; IRP integration activated per vendor when their turnover crosses the threshold |
| **TCS under GST §52** | 1 % (configurable) of net taxable supplies collected at settlement, per vendor per month, with GSTR-8-ready extract |
| **TDS under §194-O** | 1 % (configurable) on gross sales by the e-commerce operator, deducted at settlement, with a reporting extract |
| **Legal Metrology (Packaged Commodities) Rules** | Mandatory display of MRP, net quantity, manufacturer/packer/importer name and address, consumer-care contact, country of origin. Enforced as a **publish-time validation** — a product cannot go Active without them |
| **Consumer Protection (E-Commerce) Rules 2020** | Seller legal name and address on the PDP, clear return/refund/exchange policy, no dark patterns, published grievance officer with name, contact and 48-hour acknowledgement / 1-month resolution SLA, complaint ticketing and audit trail |
| **Country of origin** | Mandatory field, displayed on the PDP |
| **TRAI DLT** | All transactional SMS via registered headers and pre-approved DLT templates; template ids stored against notification templates |
| **RBI** | No card storage; recurring mandates out of scope for v1; COD handling documented |
| **IT Act / SPDI** | Reasonable security practices documented; privacy policy, terms, and returns policy pages are CMS-managed and version-tracked |
| **Accessibility** | WCAG 2.2 AA target (aligned with RPwD Act expectations for digital services) |

> **Not legal advice.** Tax rates, thresholds and statutory duties are implemented as
> **configuration**, and must be confirmed with the client's chartered accountant and legal
> counsel before go-live (a Step 33 launch-checklist item).

---

## 7. Auditing

Every privileged or money-affecting action writes an immutable `platform.audit_logs` entry:
actor, actor type, action, entity type and id, before/after snapshot, IP, user agent,
correlation id, timestamp.

Always audited: login success/failure, permission and role changes, price and stock changes,
order state transitions, cancellations, refunds, payouts, vendor status changes, settings and
feature-flag changes, CMS publishing, data export/erasure requests, impersonation start/stop.

Audit logs are append-only (no UPDATE/DELETE grant for the application role), retained
7 years, partitioned monthly, and queryable in the admin UI.

---

## 8. Security Testing & Operations

| Activity | Cadence |
|---|---|
| SAST + dependency + container scanning | Every CI run |
| Secret scanning | Every commit |
| Authorisation matrix test | Every CI run |
| DAST (OWASP ZAP baseline) against staging | Weekly and pre-release |
| Manual penetration test | Before go-live (Step 33) and annually |
| Access review (who has what) | Quarterly |
| Secret rotation | Quarterly and on staff change |
| Incident response drill | Annually |

**Incident response:** detect → contain → eradicate → recover → post-mortem, with a defined
on-call contact, a communication template for customers and vendors, and the DPDP notification
path. Documented as a runbook at Step 31.
