# ADR-014 — External identity providers for customers, and deferred SMS/email delivery

- **Status:** ✅ Accepted
- **Raised at:** Step 7 boundary (after Identity & Access, before Media & Notifications)
- **Supersedes:** —
- **Related:** ADR-006 (one deployment, one tenant), `07-security-compliance.md` §1,
  `08-integrations.md` §3, `04-api-specification.md` §3.1

---

## Context

Step 7 shipped three ways to authenticate, and two of them cannot reach a real person without a
paid provider:

| Credential | Needs | Status |
|---|---|---|
| Mobile number + OTP (customers, **primary**) | An SMS provider on a DLT-registered route | No budget |
| Email + password (staff, vendors, customers) | SMTP only for **verification** and **reset** | No budget in production |
| TOTP second factor | Nothing — the phone computes it offline | Works |

The client has no budget for an SMS gateway or a transactional email provider at this point, and
expects to have one later. Today `LoggingOtpDispatcher` writes one-time codes to the API log,
which is a development-only arrangement that `07-security-compliance.md` §3 forbids outright in a
deployed environment. Left as it is, the product cannot register a customer in production at all.

Three ways out were considered.

| Option | Why not |
|---|---|
| **Ship with the log dispatcher and a warning** | It is not a stopgap, it is a credential-disclosure bug: anyone who can read the logs can sign in as anyone. Refused. |
| **Email/password only, no verification** | Removes the SMS dependency but not the email one — a forgotten password still has no route back, and an unverified address means the account cannot be recovered *or* trusted for an invoice. Also asks an Indian shopper to invent a password on a phone, which is exactly what mobile-OTP-first was chosen to avoid. |
| **Sign in with an external identity provider** | Free at the volumes in question, verifies the email address on our behalf, and asks the shopper for no new secret. |

The catch is that an external provider is an availability and privacy dependency the specification
did not previously carry, and it does not solve the problem for staff and vendors — a decision
taken deliberately below.

## Decision

**1. Customers may sign in with an external identity provider.** Google is wired now; Facebook is
designed for and left configured-but-disabled. Both are free; only Google can be switched on
without the client first completing Business Verification and publishing a privacy-policy URL.

**2. The flow is the server-side authorization-code flow with PKCE.** The client secret never
reaches the browser, one code path serves every provider, and the same endpoints will serve a
mobile app. The cost is the first outbound HTTP call this system makes, which arrives with the
allow-list `07-security-compliance.md` §3 requires: the token and userinfo endpoints come from a
pinned authority's discovery document, never from anything a caller supplied.

**3. External sign-in is for customers only.** Staff and vendor users keep email, password and a
mandatory second factor. Two reasons: the blast radius of a compromised Google account should not
extend to `platform-admin`, and the client's staff do not share one directory this deployment
could trust. It follows that staff have no external route back into a locked account, which is
what decision 5 exists to answer.

**4. SMS and email delivery are switched off by feature flag, not removed.** Four new flags in
`platform.feature_flags` gate the mobile-OTP endpoints, email verification, the email password
reset, and external sign-in itself. Every one of them is a runtime toggle: the day a provider is
paid for, the feature returns without a deploy, and the code behind it is the code that was
written and tested at Step 7. A disabled feature answers `404 FEATURE_DISABLED`, the pattern the
Platform module already established.

**5. While email is off, an administrator sets a temporary password.** They cannot choose a
password somebody keeps: the account is marked as owing a change, and the next sign-in returns a
`password-change-required` challenge instead of a session — the same challenge mechanism the
second factor already uses. The act is audited and it revokes every existing session.

## Consequences

**What this buys.** A customer can register and sign in with no paid provider and no password.
The email address arrives already verified by the provider, which is a stronger assertion than
our own verification link would have been. Nothing built at Step 7 is removed or rewritten:
external sign-in ends at the same `SignInCoordinator` as every other credential, so sessions,
rotation, revocation, auditing and the mandatory-2FA rule apply to it without being re-stated.

**What it costs.**

- *An availability dependency.* If Google is unreachable, customers who registered that way
  cannot sign in. Email + password remains available to them as a second credential, and the flag
  can be turned off.
- *A privacy disclosure.* The shopper's email address and name reach us from Google, and Google
  learns that they use this store. This belongs in the privacy policy and in the processor
  register (`07-security-compliance.md` §5.6) before launch, not after.
- *An account-linking hazard.* Linking a provider identity to an existing account by email
  address is the standard approach and the standard vulnerability: a provider that asserts an
  unverified address would let an attacker claim somebody else's account. The rule is therefore
  narrow — link only on an email the provider states is verified, never on a mobile number, and
  otherwise create a separate account.
- *A known gap for Facebook.* A Facebook account may carry no email address at all. The
  `ck_users_has_identifier` constraint requires a mobile number or an email, so an email-less
  Facebook user cannot be created today. Recorded rather than pre-emptively worked around: it
  costs nothing while Facebook is disabled, and the fix belongs to whoever enables it.
- *A weaker recovery story for staff, chosen knowingly.* An administrator briefly knows a
  credential that would sign in as another person. Decision 5's forced change, single use and
  audit entry narrow it; they do not close it. It closes when email delivery returns.

**What must be true before launch.** `08-integrations.md` §7 gains a row for the identity
provider, and it is `⛔ BLOCKED` until the client owns the OAuth client and has published the
privacy policy the consent screen links to.
