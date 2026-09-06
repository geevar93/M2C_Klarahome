# Step 7A — External identity providers & degraded-delivery mode

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** B · **Depends on:** Step 7
- **Raised by:** The User at the Step 7 boundary — no budget for an SMS gateway or a
  transactional email provider yet, and both are expected later. Specification changed first
  under protocol rule 8; see **ADR-014** and the Change Log.
- **Objective:** A customer can register and sign in with no paid delivery provider, and every
  feature that needs one is switched off at runtime rather than removed.
- **Deliverables:**
  - `IExternalIdentityProvider` with a **Google** adapter wired end to end and a **Facebook**
    adapter configured but disabled. Server-side authorization-code flow with PKCE; `state` and
    the code verifier in a short-lived encrypted cookie; `returnUrl` checked against an
    allow-list.
  - `identity.external_logins`, keyed on `(provider, subject)`. Linking rules exactly as
    `07-security-compliance.md` §1 states them: subject first, a **verified** provider email
    second, a new account otherwise, never a mobile number. Unlink refused when it is the last
    credential.
  - Storefront endpoints: `GET /store/auth/external/providers`, `.../{provider}/start`,
    `.../{provider}/callback`, `GET`/`DELETE /store/me/external-logins[/{id}]`.
  - Four feature flags — `identity.mobile-otp-login`, `identity.email-verification`,
    `identity.password-reset-email`, `identity.external-login` — gating the endpoints that need a
    paid provider. A disabled feature answers `404 FEATURE_DISABLED`.
  - Temporary passwords: `POST /admin/users/{id}/password` (permission
    `identity.user.manage`, audited, revokes every session), `users.must_change_password`, a
    `password-change-required` challenge on the next sign-in, and
    `POST /{store,admin}/auth/password/change`.
  - The first outbound HTTP this system makes: a named `HttpClient` with a timeout, OIDC
    discovery cached, and the provider allow-list `07-security-compliance.md` §3 requires.
  - `AUTH_EXTERNAL_*` configuration, wired through compose and documented in `.env.example`,
    `README.md` and `dev-setup.md`.
- **Acceptance criteria:** With `identity.mobile-otp-login`, `identity.email-verification` and
  `identity.password-reset-email` all **off**, a new customer completes registration and sign-in
  through Google and reaches `GET /store/me`; an administrator locked out of a password account is
  recovered by a temporary password and is forced to change it before a session is issued; every
  flag turns its feature back on at runtime with no deploy; and nothing built at Step 7 changes
  behaviour while the flags are on.
- **Explicitly out of scope:** Staff and vendor external sign-in (ADR-014 decision 3); Apple;
  replacing `LoggingOtpDispatcher`, which stays until Step 8 and is only reachable while the
  flags are on.
- **Outcome / Notes:** ✅ **DONE 2026-09-05.** Both halves of the acceptance criterion met and
  demonstrated against a containerised stack: a customer signs in with a provider while SMS and
  email are switched off, and an administrator recovers a locked-out colleague with a temporary
  password they are then forced to replace.

  **Nothing from Step 7 changed behaviour.** Every flag ships on, so the default path is the one
  Step 7 shipped, and a test says so by name. The 140 Step 7 integration tests still pass
  unmodified apart from one assertion that gained a field.

  **External sign-in.** `IExternalIdentityProvider`, with a generic OIDC adapter that serves Google
  and is registered for Facebook too. Server-side authorization code with PKCE: the client secret
  never reaches the browser, the endpoints come from the authority's discovery document rather than
  from anything a caller supplied, and the `state` and code verifier travel in an AES-GCM encrypted
  cookie rather than a table — so there is no row to clean up and no retention job to add. The
  identity is read from the `id_token` rather than a second call to userinfo: it is signed, it
  already carries `sub`, `email` and `email_verified`, and it costs no round trip.

  **Linking is narrow, because the obvious implementation is the vulnerability.** A known
  `(provider, subject)` signs that user in whatever their email says today; a **verified** provider
  email may join an existing account; an unverified one may not, and a new account is created
  instead — or refused with a conflict when the address is already taken. A mobile number is never
  a linking key. A staff or vendor account matched by email is refused outright rather than linked,
  which is what keeps ADR-014 decision 3 from being bypassed by a Google account bearing the right
  address.

  **The first outbound HTTP this product makes**, and it arrives with the controls
  `07-security-compliance.md` §3 asks for rather than after them: a named client with a ten-second
  timeout, a cached discovery document, and a `DelegatingHandler` that refuses any host outside a
  compiled-in allow-list. Nothing in the URL is caller-supplied; the allow-list is there so a
  future mistake fails at the socket instead of at the provider.

  **Feature flags across a module boundary.** The flags live in `platform.feature_flags`, which the
  Identity module may not write to. `IFeatureFlagSource` lets any module declare its flags and the
  Platform seeder collects every registered source — so the admin UI lists every switch that
  exists, rather than only the ones somebody has already touched. `RequireFeature("...")` gates an
  endpoint the way `RequirePermission` gates one, recording the flag as metadata alongside the
  filter so the set stays enumerable.

  **Temporary passwords**, and what narrows them. `users.must_change_password`, a
  `password-change-required` challenge reusing the mechanism the second factor already had, and one
  `POST /auth/password/change` serving both the forced route and a voluntary change. Issuing one
  ends every session the account has, the password still has to meet the full policy — "temporary"
  is not a reason to accept `Password123` — and the entry is audited as the administrator's act
  rather than the system's.

  **Endpoints added**

  | Method | Route | Notes |
  |---|---|---|
  | `GET` | `/store/auth/external/providers` | Only providers that are switched on *and* configured |
  | `GET` | `/store/auth/external/{provider}/start` | 302 to the provider, state cookie set |
  | `GET` | `/store/auth/external/{provider}/callback` | 302 back to an allow-listed return URL, refresh cookie set |
  | `GET`, `DELETE` | `/store/me/external-logins[/{id}]` | Unlink refused when it is the last credential |
  | `POST` | `/{store,admin}/auth/password/change` | Not flag-gated: it is the way out of a temporary password |
  | `PUT` | `/admin/users/{id}/password` | `identity.user.manage`, audited, ends every session |

  **Deviations from the specification, and why**

  1. **The state is a cookie, not a table.** `03-database-design.md` gains `external_logins` and
     nothing else. A row for each started sign-in would need an index and a retention job for the
     ones nobody finishes; an encrypted cookie expires by itself. `SameSite=Lax` is load-bearing —
     `Strict` would drop it on the provider's redirect back and every sign-in would fail silently.

  2. **A provider that is enabled but unconfigured is treated as absent**, not as an error. It is
     the ordinary state of a fresh deployment, and a button that fails when somebody presses it is
     worse than no button.

  3. **`AdminUserResponse` gained two fields rather than a new response shape.** `MustChangePassword`
     and `PasswordSetupPending` answer "can this account sign in yet", which an administrator
     creating a user with email off needs to know. Adding fields keeps every existing client working;
     a wrapper type would not have.

  4. **The unverified-email collision is a `409`, not a silent second account.** Refusing tells the
     person something they can act on — sign in with the password you already have — where creating
     a second account under a different provider identity would leave two accounts and one confused
     customer.

  #### What is still owed

  `LoggingOtpDispatcher` is unchanged and still writes codes to the log. With the flags off it is
  unreachable, which is what makes a deployment safe today; Step 8 removes the need for it. The
  `08-integrations.md` §7 row for the identity provider is `⛔ BLOCKED` until the client owns a
  Google OAuth client and has published the privacy policy its consent screen links to.
