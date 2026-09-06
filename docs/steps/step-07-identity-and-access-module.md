# Step 7 — Identity & Access module

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** B · **Depends on:** Step 6
- **Objective:** Authentication and authorisation for three actor classes: Customer, Vendor
  staff, Platform staff.
- **Deliverables:**
  - Registration/login: mobile number + OTP (primary for customers), email + password
    (secondary), password reset, email/phone verification.
  - JWT access tokens (short-lived) + rotating refresh tokens (HttpOnly, secure cookie),
    device/session listing and revocation.
  - RBAC: roles, granular permissions, permission-based authorisation policies; vendor-scoped
    authorisation (a vendor user may only ever see their own data).
  - Mandatory TOTP 2FA for platform-admin and vendor-owner roles.
  - Brute-force protection, OTP throttling, account lockout, security event logging.
  - Customer profile, saved addresses (Indian address model), GSTIN for B2B invoices.
- **Acceptance criteria:** All three actor types can authenticate; a vendor user is provably
  denied access to another vendor's data; refresh-token rotation and revocation verified.
- **Outcome / Notes:** ✅ **DONE 2026-09-05.** All three criteria met, each proved by a test that
  goes over HTTP against the real database rather than by inspection.

  **What was built.** A new `KlaraHome.Modules.Identity`, owning the `identity` schema: eleven
  tables, one migration, three seeders. The module registers the JWT bearer scheme, which is what
  makes `UnsecuredEndpointGuard` go quiet — the seven admin endpoints Step 6 left declaring
  permissions nothing enforced are now behind policies that check them.

  **Authentication.** Three actor classes, one set of endpoints mapped under both `/store` and
  `/admin`, because the flows are identical and writing them twice would mean fixing the next
  authentication bug twice. Customers present a mobile number and a six-digit code; verifying a
  code for an unknown number *registers* the customer, which is what makes mobile-OTP the primary
  credential rather than a convenience layered over an account somebody had to create first. Staff
  and vendor users present an email and a password, hashed with Argon2id at OWASP's first
  recommended cost. The stored value is a PHC string carrying its own parameters, so raising the
  cost next year verifies every existing password and re-hashes it on the owner's next sign-in
  instead of invalidating the lot.

  **Second factor.** TOTP, RFC 6238, implemented rather than taken from a package: it is forty
  lines of HMAC and truncation, both RFCs publish test vectors, and it is now asserted against all
  six of them. Mandatory for `platform-admin` and `vendor-owner`. A correct password for an account
  that owes a second factor answers with a challenge rather than a session — either `two-factor` or
  `two-factor-enrolment` — and the enrolment path completes the sign-in in the same round trip,
  because "enable it, now sign in again" is where a mandatory 2FA rollout gets abandoned. Secrets
  are AES-256-GCM encrypted at rest under a key id that travels in the envelope, so the key can be
  rotated with an overlapping window.

  **Sessions.** An access token is a 15-minute RS256 JWT carrying `sub`, `tenant_id`, `user_type`,
  `vendor_id?`, `session_id`, `jti` and one claim per permission. The refresh token is 256 bits of
  opacity in an `HttpOnly; Secure; SameSite=Lax` cookie, rotated on every use. Presenting a rotated
  token again revokes the whole session rather than that token: two parties hold the same secret and
  there is no way to tell which is which, so the only outcome that does not leave the thief with a
  working session is to end it for both. `GET /me/sessions` lists devices with masked addresses; one
  or all can be revoked, and a password reset revokes every one.

  **Authorisation.** Permission-based and deny-by-default.
  `RequirePermission("platform.settings.manage")` now both records the permission as metadata and
  attaches the policy that enforces it — one call, so a declaration without a check is not
  expressible. A dynamic policy provider manufactures every `perm:` policy from its name, so adding
  an endpoint never means remembering to register a policy somewhere else; a fallback policy closes
  any endpoint that declares nothing. Roles are data in `identity.roles`, enforcement is never on a
  role, and nine system roles are seeded from the actor list in `02-domain-model.md` §1 — several
  deliberately empty until the steps that build the endpoints they would grant.

  **Vendor scope.** `IVendorScoped` joins `ITenantScoped` in the SharedKernel, and `ModelConventions`
  gives it a global query filter comparing the row's `vendor_id` to the caller's claim: open for a
  caller with no vendor scope, closed for one with it. `user_roles` is the first table to carry it,
  which is what makes `GET /admin/users` a real proof rather than a promise — a vendor owner listing
  "their" users cannot discover that another seller's staff exist at all. A `{id}` route answers 404
  rather than 403, and a vendor owner holding `identity.role.assign` is refused when they try to
  grant themselves a platform role: the classic escalation path through a delegated user-management
  screen.

  **Abuse resistance.** Progressive lockout that doubles and is capped rather than permanent, so an
  attacker cannot lock any account for ever by guessing at it. Per-destination OTP throttling in the
  module *and* per-IP limiting at the edge, because each is useless against what the other catches.
  Login answers identically for a wrong password, an unknown address and a disabled account, and
  performs a decoy Argon2id verification for an unknown one so the timing does not answer either.
  Every sign-in, failure, role change, status change and two-factor event is audited.

  **Endpoints** (`04-api-specification.md` §3.1, §4):

  | Method | Route | Notes |
  |---|---|---|
  | `POST` | `/{store,admin}/auth/login` | Email + password. May answer with a challenge |
  | `POST` | `/store/auth/otp/request`, `/otp/verify` | Mobile + code; verifying registers a new number |
  | `POST` | `/store/auth/register` | Email + password, with unbundled marketing consent |
  | `POST` | `/{store,admin}/auth/refresh`, `/logout` | Cookie in, rotated cookie out |
  | `POST` | `/{store,admin}/auth/password/forgot`, `/reset` | Uniform response; a reset revokes every session |
  | `POST` | `/{store,admin}/auth/2fa/enrol`, `/2fa/verify` | Completes a challenged sign-in |
  | `GET`, `PATCH` | `/{store,admin}/me` | Identity, roles, permissions, shopper profile |
  | `GET`, `DELETE` | `/{store,admin}/me/sessions[/{id}]` | Device list and revocation |
  | `POST` | `/{store,admin}/me/2fa/setup`, `/enable`, `/disable` | Disable is refused for mandatory roles |
  | `POST` | `/{store,admin}/me/verify/request`, `/confirm` | Proves an email address or mobile number |
  | `GET`, `POST`, `PUT`, `DELETE` | `/store/me/addresses[/{id}]` | Indian address model, GSTIN per address |
  | `GET`, `POST` | `/admin/users` | Keyset-paginated; vendor-scoped by the caller's token |
  | `GET`, `PUT` | `/admin/users/{id}`, `/roles`, `/status` | 404 outside scope; a role change ends the sessions |
  | `GET`, `POST`, `PUT` | `/admin/roles[/{id}]` | System roles are read-only, because they are reseeded |
  | `GET` | `/admin/permissions` | The catalogue, served from code rather than from the table |

  **Deviations from the specification, and why**

  1. **ASP.NET Core Identity is not used.** The framework's implementation is built around a
     cookie-first, `UserManager`-shaped model that does not fit mobile-OTP as the primary
     credential, vendor-scoped data filtering, or permission-based rather than role-based
     enforcement. Adapting it would have been more code than the parts we actually need.

  2. **An unrouted path stays a 404.** ASP.NET Core applies the fallback authorisation policy to
     requests that matched no endpoint as well as to endpoints, which turned every unknown URL into
     a 401 — contradicting `04-api-specification.md` §1.2, telling a client nothing it can act on,
     and hiding nothing, since the route table *is* the published API. One middleware answers 404
     before authorisation runs.

  3. **Registration says why it refused.** `07-security-compliance.md` §3 requires uniform responses
     on login, OTP and forgot-password, and all three comply. A registration form cannot: somebody
     whose address is already taken has to be told, or they cannot proceed. The same fact is
     reachable by simply trying to register, which is what an attacker would do anyway — unlike a
     login form, where the leak costs nothing to close.

  4. **The breached-password check is a compiled-in list, not the HIBP range API.** That call is
     outbound HTTP on the registration path and needs the §3 SSRF allow-list, a timeout policy, a
     decision about what to do when it is down, and tests that do not depend on the internet. The
     offline list covers the top of every credential dump plus the entries this deployment invites —
     `klarahome123` is in no public breach list at all. Parked for the step that builds the outbound
     HTTP policy.

  5. **`StateResponse` now carries the state's id.** An address stores `state_id`, and the storefront
     form could not supply one from a document that published only the GST code. A one-field addition
     to a Step 6 endpoint, plus a new `IReferenceData` contract so a module that stores a `state_id`
     can validate it without reading the `platform` schema.

  6. **Two shared-layer additions.** `IVendorScoped` with its query filter, and `ICallerContext`.
     Both are cross-cutting by nature — the filter has to run inside the data layer, and every module
     from Step 9 onward needs the caller's scope — so neither could live in this module.

  7. **`AdminSurfaceTests` was rewritten rather than deleted.** Before Step 7 it held a gap open:
     every admin endpoint declares a permission nothing enforces. It now asserts the enforcement,
     plus two rules that keep the surface honest — an endpoint under `/admin` outside `/auth` and
     `/me` must declare a permission, and every declared permission must exist in the catalogue,
     because one that does not is an endpoint no role can ever reach.

  8. **The test fixture migrates every module, not one.** `PlatformSchemaFixture` became
     `KlaraHomeSchemaFixture`: the API host these tests point at composes every module, and a
     database carrying only some of their schemas is not a database the product ever runs against.

  #### The Step 8 gap, stated rather than hidden

  There is no SMS or email transport until the Notifications module, so `LoggingOtpDispatcher` writes
  one-time codes and reset links to the log — which is exactly what §3's logging hygiene forbids. It
  is registered unconditionally, because a host with no dispatcher at all would fail at the moment
  somebody tried to sign in rather than at startup, and the class itself says loudly on every code
  that it is not a production arrangement. Step 8 replaces the registration, not the seam.
