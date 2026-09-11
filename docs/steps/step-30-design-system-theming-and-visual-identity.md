# Step 30 — Design system, theming & visual identity

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** H · **Depends on:** Step 29
- **Objective:** **This is the step where aesthetics are finally decided and applied.** Until
  this step, everything uses neutral placeholders.
- **Deliverables:**
  - Brand discovery with the client: positioning, audience, references, moodboard direction.
  - Design tokens finalised: colour system (with dark-mode decision), typography scale and
    font pairing, spacing/radius/elevation scales, iconography, imagery and photography rules,
    motion principles.
  - Component library restyled against the tokens; storefront and admin themed consistently.
  - **White-label theming mechanism verified**: tokens delivered as runtime CSS custom
    properties per tenant, so a second client can be rebranded via configuration and assets
    only — no code changes, no rebuild.
  - Responsive visual QA across the device matrix; contrast/accessibility re-verified after
    theming; email/PDF template styling; favicon, app icons, PWA manifest, OG images.
  - Design documentation handed over.
- **Acceptance criteria:** Client signs off the visual design on the real application; a
  second demo theme can be applied end-to-end purely by swapping configuration.
- **Status:** ✅ DONE — closed by the User's explicit instruction (2026-09-11), after review of
  every deliverable in this note. Both acceptance criteria are met: the client has signed off the
  visual design on the real application (§ below), and the white-label proof shows a second demo
  theme ("Nimbus Living") applied end-to-end purely by swapping configuration, for both storefront
  and admin. Every other deliverable this step names is also built and verified against the
  running dev stack — the token layer, brand assets, email/PDF template styling, the stylelint
  guardrail, visual-regression baselines, and responsive QA across the device matrix. **Recorded
  gaps that remain true after closure, not resolved by it:** dark mode was declined by the client
  (token structure supports adding it later); no scripted authenticated e2e session exists for
  either app, so the visual-regression baselines and responsive QA both stop at the screens an
  anonymous visitor can reach — account pages, authenticated checkout and the admin
  dashboard/tables are unverified by either mechanism. Neither gap blocked closure; both are
  named so a future reader does not mistake "done" for "exhaustive".
- **Outcome / Notes:**

  **Done.** `10-design-system-placeholder.md` is superseded by
  [`../10-design-system.md`](../10-design-system.md). The client's six-colour warm earth ramp is
  applied as semantic tokens across storefront and admin; two candidate assignments were rejected
  by the contrast check rather than by taste, and the reasoning is recorded (§1.1). Typography is
  Fraunces for headings and the wordmark — self-hosted, latin-subset, one weight, 35 KB, with a
  metric-adjusted fallback so `font-display: swap` does not shift the layout — over the system sans
  for the interface, which keeps the LCP budget intact. Radius, elevation and motion scales were
  re-cut; shadows are cast in the ramp's own ink. Contrast was verified for every semantic pairing
  before it was written (§1.2). The claim that this step would be a token swap held: the structural
  additions the design genuinely needed are six, and they are listed in §5.

  **Client sign-off on the working application is now in hand** — the theme was shown to the
  client on the real application and confirmed. It was reached without the brand discovery
  workshop, moodboard or alternative directions the deliverable list names: the client approved
  the single direction produced from their supplied palette rather than choosing among several.
  Recorded because a future reader should not infer a discovery process happened when it did not.

  **The white-label proof is now done, and it found the mechanism was not actually in place.**
  §6 of `10-design-system.md` claimed tokens were "emitted from the tenant's theme configuration at
  runtime... not compiled into the CSS bundle." That was true of `_tokens.scss` as a *file* — every
  value is a CSS custom property, not a SCSS variable — but nothing ever wrote a tenant's values
  onto `:root`. `BrandingSettings` carried `PrimaryColor`/`AccentColor` and the admin could edit
  them, but `StoreConfigService` (storefront) only ever read `storeName`/`tagline` out of the
  branding section; the two colour fields were validated on save and then never consumed anywhere.
  A second tenant changing its branding today would have changed a value nobody read. That is the
  real finding, not a caveat on it.

  What was built to close the gap, kept deliberately small:
  - `BrandingSettings.ThemeTokens` (`IReadOnlyDictionary<string,string>`) — a tenant's raw CSS
    custom-property overrides, keyed by the same names `_tokens.scss` already declares
    (`--color-primary`, `--color-bg`, …). Empty by default, so a tenant that has never set it keeps
    Klara Home's compiled defaults exactly as before — this is additive, not a schema break.
    Validated server-side (`SettingsValidators.cs`): every key must match a CSS custom-property
    shape, values are length-capped, the map is capped at 100 entries.
  - `ThemeService` (`libs/util/src/lib/theme.service.ts`) — the one place that calls
    `style.setProperty` on `document.documentElement`. It re-validates key shape client-side too,
    because a cached or hand-edited settings document is data this app did not just validate.
  - `ShellStore.initialise()` calls `theme.apply(branding().themeTokens)` in the same
    `config.load()` callback that already sets the SEO store name — so the tokens land during the
    same SSR pass, before the response is serialised, exactly the way the header's store name
    already did.
  - Two rebuilt images (`storefront`, `api`) were deployed to the dev stack once, to carry this
    code. That is the only rebuild involved anywhere in this deliverable.

  **The proof, run against the live dev stack, not simulated:**
  `infra/scripts/verify-white-label-theming.sh` writes a second tenant's branding
  (`storeName: "Nimbus Living"`, a cool indigo/teal token set — `--color-primary: #3949ab`,
  `--color-accent: #14b8a6`, `--color-bg: #eef2fb` — a different hue family from Klara Home's warm
  earth ramp on purpose, so the difference reads at a glance) directly into
  `platform.store_settings`, restarts only the API container (to drop its 5-minute settings cache —
  not a rebuild, the image is untouched), and asserts against the *running* stack that:
  - `GET /store/config` echoes the new store name and token map, and
  - the storefront's server-rendered home page carries them: `<html style="--color-primary: #3949ab;
    --color-accent: #14b8a6; …">` and `<title>Home | Nimbus Living</title>`.

  Both assertions passed. A real screenshot was taken of the rendered "Nimbus Living" home page next
  to Klara Home's own — indigo/teal surfaces, primary buttons and focus rings against the sand/coffee
  original, with the same product cards, layout and typography untouched. The script then restores
  the original branding document and restarts the API again; the stack was left exactly as found.
  No frontend or backend source file changed between the "before" and "after" checks — only the
  `branding` settings document did.

  **The admin gap above is now closed.** The admin app had no `StoreConfigService` consumer at
  all — every admin panel stayed on the compiled Klara Home tokens regardless of tenant, which is
  the finding the first version of this note recorded. What was built to close it, reusing the same
  pieces rather than forking them:
  - `apps/admin/src/app/app.config.ts` gained a second `provideAppInitializer`, alongside the
    existing one that sets `AuthService.surface`. It injects `StoreConfigService` (already
    `scope:shared`, so importing it from the admin app crosses no lint boundary) and `ThemeService`
    — the exact same `libs/util` service the storefront uses, not a second implementation — and
    calls `theme.apply(config.branding().themeTokens)` once `config.load()` answers.
  - **Why an app initializer and not `ShellStore`-style wiring:** the admin has no SSR pass to
    piggy-back tokens onto the way `ShellStore.initialise()` does inside `config.load()`'s callback
    during server rendering. `provideAppInitializer` is the admin's equivalent hook — it already
    gates the router on `AuthService.surface` being set before the first route resolves, so gating
    it on the tenant's tokens being applied too, before any screen paints, follows the same pattern.
  - This needed the admin Docker image rebuilt once (`docker compose build admin`) to carry the
    change — the same kind of one-time rebuild `ThemeService` itself required for the storefront and
    API images, not a new category of cost.

  **The proof for admin is necessarily different in kind from the storefront's.** The admin is
  CSR-only — no `server` build target (`docs/05-frontend-architecture.md` §4.1) — so there is no
  server-rendered HTML response to grep the way the storefront's is grepped above. "Proven" here
  means: `infra/scripts/verify-white-label-theming.sh` now also drives a real headless Chromium
  (via `src/frontend/scripts/check-admin-theme-tokens.mjs`, using the `playwright` devDependency
  already in the workspace for `admin-e2e`) against the admin's sign-in screen with the same
  "Nimbus Living" branding document live, waits for `provideAppInitializer` to run, and asserts the
  tenant's `--color-primary`/`--color-accent` land on `document.documentElement`'s `style`
  attribute — which they did. This is a real assertion against real rendered output, the same as
  the storefront's, but it is a post-bootstrap DOM check rather than an initial-response check: an
  admin user with JavaScript disabled or a crawler (there is none — the admin is never indexed)
  would see the compiled default for a moment the storefront's crawler-facing response never has.
  That is an inherent property of the admin being CSR-only, not a gap this change left open.

  **Caveats, honestly:**
  - **The token set covered is the commonly-read semantic layer** (surfaces, borders, text, primary,
    accent, focus ring) — enough to make a re-theme unmistakable and to prove the mechanism, not
    every token `_tokens.scss` declares (status colours, spacing, motion, radius are untouched by
    this override path, by design: nothing about the *shape* of a re-theme requires them, and a
    tenant that wants to override one can — the dictionary accepts any declared custom property).
  - **This is a runtime override on the compiled cascade, not a themed build.** `_tokens.scss`'s
    values are still the deployment's factory defaults; `ThemeTokens` widens the existing "single
    Docker image, per-tenant settings" model (docs/01-architecture.md §8) rather than replacing it.

  **Brand assets are now built.** Hand-authored SVG is the first-class deliverable here; PNG/ICO
  are real rasterisations, not fakes — there is no `sharp` and no ImageMagick (`magick`/`convert`)
  on this machine, but the `playwright` devDependency already in the workspace (used for
  `admin-e2e`) launches a real headless Chromium, which is a real rendering engine, so every raster
  file below was produced by actually screenshotting the SVG rather than hand-rolling bytes.

  - **Source SVGs** — `src/frontend/apps/storefront/public/brand/`: `mark.svg` (the abstract
    arch/doorway glyph, built from one arc of radius 20 — the design system's own `--radius-xl`
    step, not a bespoke curve), `wordmark.svg` (mark + "Klara Home" set in Fraunces 600 on ink),
    `favicon.svg` (a bolder redraw of the mark on a sand tile, so it reads at 16px),
    `icon.svg` / `icon-maskable.svg` (the app-icon tile, with and without the OS-mask safe zone),
    `og-default.svg` (1200×630 share image) and `error-404.svg` / `error-500.svg` (geometric,
    typographic error art — two broken arches, no photography or illustrated icon, honouring the
    imagery rule products alone get photography). All fills come from the six-colour ramp in
    `10-design-system.md` §1 verbatim — no new colours, no new typeface.
  - **The wordmark stays text in the actual header** (`libs/ui/patterns/src/lib/site-header.ts`):
    a second white-labelled tenant's store name is not "Klara Home", and baking one tenant's name
    into an SVG would fight the white-label mechanism §6 just finished proving. `wordmark.svg` is
    the brand asset for contexts where the *brand*, not the *tenant*, is being identified.
  - **Rasterised via `src/frontend/scripts/build-brand-assets.mjs`**, a documented, re-runnable
    Playwright script (re-run it after editing any source SVG): `favicon-{16,32,48}.png`,
    `favicon.ico` (a real multi-image ICO container — verified with `file`, which reports three
    embedded PNG images, 16/32/48), `icon-{192,512}.png`, `icon-maskable-{192,512}.png`, and
    `og-default.png`, all under the same `brand/` folder, copied into both apps'
    `public/favicon.ico`. Nothing here is a placeholder — every PNG is a genuine screenshot render
    of its source SVG at the target size.
  - **`manifest.webmanifest`** (`apps/storefront/public/`) — name, short name, `theme_color`
    (`#714c35`, `--brand-coffee-600`) and `background_color` (`#faf6f1`, `--brand-sand-050`) pulled
    from the actual tokens, the four generated icons referenced, `display: "browser"` — honestly,
    not `"standalone"`, because there is no service worker anywhere in this workspace and claiming
    installability without one would be asserting a capability that is not there. Wired into
    `apps/storefront/src/index.html` alongside `favicon.svg`, `favicon.ico`, `apple-touch-icon`
    and a `theme-color` meta tag.
  - **Admin got a favicon and theme-color only**, not a manifest or PWA icon set: its `project.json`
    and app config carry no PWA/service-worker configuration, so shipping installable-icon
    metadata for a surface that cannot be installed would be inventing a capability, which the
    brief for this deliverable explicitly said not to do. `apps/admin/src/index.html` now links
    `brand/favicon.svg` and `favicon.ico` (copied from the same source) and sets `theme-color`.
  - **OG image wired in**: `SeoService.apply()` (`libs/util/src/lib/seo.ts`) now falls back to
    `/brand/og-default.png`, resolved against the configured canonical origin, whenever a page's
    metadata supplies no `imageUrl` of its own — most pages that are not a product or a vendor.
    Product/vendor pages that already pass their own image are unaffected.
  - **404/500 art**: `error-404.svg` / `error-500.svg` render above `kh-empty-state` on
    `apps/storefront/src/app/pages/errors/not-found.page.ts` and `server-error.page.ts`. The art
    sits *outside* `kh-empty-state`, not inside it — the component's own documented rule is plain
    text and a single action, no illustration (`10-design-system-placeholder.md` §3), and this
    deliverable did not touch that rule or the component. Both pages already existed with working
    routing and `SeoService` wiring (Step 20/23 work); only the visual layer changed here.

  **Email and PDF template styling is now built.** Two different documents, two different
  constraints, handled differently rather than forcing one styling mechanism onto both.

  - **Email.** The seeded templates (`DefaultTemplates.cs`) stay deliberately bare — a handful of
    `<p>` fragments an operator can rewrite without touching markup, exactly as their own doc
    comment says. Rewriting all fourteen of them was not what "onto the new tokens" needed to mean:
    a new `EmailLayout.Wrap()` (`KlaraHome.Modules.Notifications/Infrastructure/Channels/
    EmailLayout.cs`) wraps whatever a template renders in the shared HTML shell once, in
    `SmtpEmailSender`, so every template gets the same header and colours without every template
    author reproducing them. Every style is inline (`style="..."` attributes, no `<style>` block, no
    external stylesheet) — most mail clients strip a `<style>` block and none fetch a remote
    stylesheet, so this is not a preference, it is the only mechanism that reaches an inbox.
    Headings and the header wordmark fall back to the design token's own documented email-safe tail
    (`'Iowan Old Style', 'Palatino Linotype', Georgia, serif` — the two self-hosted Fraunces links
    dropped, since a mail client cannot fetch them); the interface stack needed no adjustment
    because `--font-sans` was already OS-font-first. Colours are the exact hex values from
    `10-design-system.md` §1 (`--color-bg #faf6f1`, `--color-text #340c00`, `--color-primary
    #714c35`, `--color-border #ddc2a6`), pinned by `EmailLayoutTests.cs` rather than left free to
    drift from the token doc.
  - **The header logo, honestly.** The storefront's `wordmark.svg` won't render in a mail client and
    there is no public URL to host a PNG at from this dev environment — the brief's own caveat.
    What was actually achievable, and is now shipped: the brand mark (`mark.svg`'s doorway glyph,
    not the tenant-name-bearing wordmark — the same "mark is brand, wordmark-text is tenant" split
    the site header already draws, docs/10-design-system.md §7) is rasterised to a 96×96 PNG by the
    existing `build-brand-assets.mjs` script, embedded into the Notifications assembly as a resource,
    and attached to every outgoing message as a `Content-Id`-referenced inline image (`cid:
    kh-email-mark`), never a remote `<img src="https://…">`. This sidesteps the public-URL problem
    entirely rather than working around it: a CID image travels inside the message itself, so it
    renders identically in Mailpit today and in production later, with no deployment required to
    prove it — and it also does not trip the "block remote images until clicked" behaviour most
    inboxes apply to a hosted logo. The store name stays live text next to it (`{{storeName}}`,
    read from `BrandingSettings` the same way the subject line already was), for the same
    white-label reason the wordmark itself stays text in the real header: a second tenant's name is
    not "Klara Home".
  - **Verified against the real dev stack, not simulated.** A real order-shipped-style message was
    rendered with `EmailLayout.Wrap()` and sent over SMTP to the dev stack's Mailpit container
    (`klarahome-dev-mailpit`, `127.0.0.1:1025`); Mailpit's own API (`GET /api/v1/message/{id}`) was
    read back and confirmed the token colours, the inline styles, and the `cid:kh-email-mark`
    reference resolving to a genuine inline attachment (`ContentID: kh-email-mark`, `image/png`,
    1211 bytes — the actual rasterised mark, not a placeholder).
  - **PDF.** `MigraDocRenderer` (`KlaraHome.Infrastructure/Documents/`) is the one renderer behind
    both `InvoiceDocumentBuilder` (Orders) and `CreditNoteDocumentBuilder` (Returns) — also
    `CommissionInvoiceDocumentBuilder` (Settlements), which inherited the change for free — so one
    edit reaches every generated document. The hard-coded neutrals (`Colors.Gray`,
    `Colors.LightGray`, `Colors.WhiteSmoke`) on rules, captions, the footer and the table-header
    shading are now the ramp's own tokens (`--color-border #ddc2a6`, `--color-surface-sunken
    #ecddce`, `--color-text-muted #714c35`) instead of generic PDF-library gray — the same
    reasoning §3 of the design doc gives for casting on-screen shadows in the ramp's own ink rather
    than neutral black.
  - **What the PDF deliberately did not get, and why, recorded on `MigraDocRenderer` itself so the
    decision does not have to be rediscovered:** no Fraunces, and no brand accent colour on the item
    table or totals. Fraunces ships one weight (600) with no bold face; a statutory document's
    "Normal" style is also its heading style in MigraDoc's model, so a face with only one weight
    cannot serve both, and `InvoiceDocumentBuilder`'s own doc comment already recorded the intent
    this respects: "aesthetics are Step 30's, and a GST invoice is not where they would start
    anyway." Colouring monetary figures or table headers in the action colour was rejected for the
    same reason a coloured total would be a bad idea on any tax document: it must never read as
    emphasis or a status.
  - **Verified against a real render, not a unit-test assertion alone.** `InvoiceDocumentBuilder`'s
    existing fixture was rendered through `MigraDocRenderer` end to end — real fonts resolved via
    `FileFontResolver` from `C:\Windows\Fonts` in this dev environment, a real 86&nbsp;KB
    `%PDF-1.7` file written to disk and opened. The header captions, footer, table-header band and
    rules render in the token colours; the item table and totals stay black, as intended.
  - **All 988 backend unit tests pass** (`dotnet test tests/KlaraHome.UnitTests`), including
    `129` covering Notifications, Orders, Returns and Settlements together and the `6` new
    `EmailLayoutTests`; `dotnet build` across the full solution is clean, `0` warnings.

  **The stylelint guardrail is now built — and it is two rules, not one, because of where the
  colour actually lives in this codebase.** The placeholder's promise was "the hard-coded-value
  lint rule was never implemented — there is no stylelint config in the workspace." Investigating
  that gap found something the placeholder note did not anticipate: the workspace has only ten
  real `.scss` files (`libs/ui/primitives/src/styles/*.scss` and each app's `app.scss`/
  `styles.scss`). Almost every component's styling instead lives inline, in the `styles:` template
  literal of its Angular `@Component` decorator (163 files) — `inlineStyleLanguage: "scss"` in
  both apps' `project.json`, but never written to a `.scss` file stylelint could ever see. A
  guardrail that only pointed stylelint at `**/*.scss` would have covered ten files and missed
  the 163 where a hard-coded colour would actually be introduced.
  - **`.scss` files**: `stylelint@17.15.0` (`src/frontend/.stylelintrc.json`), with
    `color-no-hex` and `function-disallowed-list: [rgb, rgba, hsl, hsla]`, both carrying a message
    pointing at `docs/10-design-system.md` §1. `libs/ui/primitives/src/styles/_tokens.scss` (and
    any future `_tokens.scss`) is exempted by an override on that path glob — it is the one place a
    literal colour value is supposed to be written. `stylelint-config-standard-scss` was tried
    first and rejected: extending it surfaced 54 violations, none about colour — comment style,
    keyword case, `:not()` notation — which would have meant either a large unrelated reformat or
    disabling half the extended config; the two rules this deliverable actually needs are written
    directly instead, so a `stylelint` failure always means a real colour violation.
  - **Inline `styles:` template literals**: a local ESLint rule,
    `local/no-hardcoded-color-in-styles` (`src/frontend/eslint.config.mjs`), walks every `styles`
    property's `TemplateLiteral` quasis for a hex or `rgb()`/`hsl()`/`rgba()`/`hsla()` literal.
    stylelint itself cannot reach this: `postcss-styled-syntax`, the usual CSS-in-JS bridge for
    linting template-literal styles with a real CSS parser, was installed and tried against this
    codebase first (`libs/ui/patterns/src/lib/address-card.ts`, with a hex colour deliberately
    injected to check) and it did not detect it — that package targets *tagged* templates
    (`` styled.div`...` ``, `` css`...` ``); Angular's plain `styles: \`...\`` property is not one.
    The ESLint AST rule was written instead, checked directly against the same file with the same
    injected hex first to confirm it fires, then reverted before the real run.
  - **Wired into the existing lint pipeline**, not bolted on beside it: `npx nx run-many -t lint`
    already runs the ESLint rule, because it is one more rule in the same `eslint.config.mjs`
    every project's `lint` target already points at — no new Nx target needed. `stylelint` has no
    per-file-type Nx executor of its own in this workspace, so it is invoked once, workspace-wide,
    as `npm run stylelint` (`package.json`), and `tools/ci.ps1`'s `Invoke-LintStage` now runs it
    as a second step after `nx run-many -t lint`, so `pwsh tools/ci.ps1 -Stage lint` — and therefore
    CI — fails on either kind of violation.
  - **Violations found: zero, across both mechanisms.** Every `.scss` file lint-clean immediately;
    every one of the 163 files with inline `styles:` lint-clean immediately too. The one hex-colour
    hit in a component `.ts` file workspace-wide,
    `apps/storefront/src/app/pages/auth/social-sign-in.ts`, is in the page's *template* (`fill="#…"`
    on inline Google/Facebook brand-mark SVGs, the providers' own brand colours), not in `styles:`,
    so the rule correctly does not flag it and it was left untouched. "Never hard-code a colour" was
    apparently followed as a convention even before there was a guardrail enforcing it — the
    guardrail's job from here is to keep that true as the codebase grows, not to have found a pile
    of debt to clean up.

  **Visual-regression baselines are now captured, against the real themed dev stack, not the
  `nx serve` dev-build server.** That distinction mattered enough to build two Playwright configs
  instead of pointing the existing skeleton specs' config at new tests: `playwright.config.mts`
  (Step 29) boots its own server via `nx run storefront:serve` — the Angular dev build, with
  `optimization: false` and no tenant branding document applied the way `ThemeService`/
  `ShellStore.initialise()` apply it against real settings data (§6 above). A baseline taken
  against that server would have been a baseline of the placeholder, not of the theme the client
  signed off on. `apps/storefront-e2e/playwright.visual.config.mts` and
  `apps/admin-e2e/playwright.visual.config.mts` instead point at the already-running docker dev
  stack (`https://klarahome.localhost`, `https://admin.klarahome.localhost`, behind Traefik,
  `ignoreHTTPSErrors: true` for its self-signed dev cert) — the same stack §6's white-label proof
  and the email/PDF verification above ran against.
  - **Specs**: `apps/storefront-e2e/src/theming.visual.spec.ts` (home, `/c/cushions` category
    listing, the `linen-cushion-cover` PDP, `/checkout`) and `apps/admin-e2e/src/theming.visual.spec.ts`
    (sign-in, forgot-password), each run at a `mobile` (Pixel 5 emulation, matching the
    Step-29 e2e config's own mobile-first choice) and `desktop` (1280×900) breakpoint —
    **12 baselines total**: 8 storefront, 4 admin.
  - **Admin dashboard and data-table screens are not baselined, and that is a scope decision, not
    an oversight.** Every screen behind them requires an `authenticatedGuard` session, and the
    `platform-admin` role that session needs is mandatory-two-factor (`docs/dev-setup.md`,
    "Signing in locally") — scripting a TOTP enrolment into a Playwright fixture was out of scope
    for this turn. Sign-in and forgot-password are the two admin screens actually reachable
    without one, so they are what is baselined; a scripted-2FA fixture that would unlock
    dashboard/table baselines is deferred, named here rather than silently dropped.
  - **Baselines are real screenshots of the real running product**, not placeholders: `home.png`
    shows the actual seeded catalogue (Linen Cushion Cover, ₹899, "30% off") rendered in the
    warm-earth theme — Fraunces headings, the sand ground, the coffee-brown primary button —
    exactly as `10-design-system.md` §1 specifies, because it is the literal page a browser gets
    from the docker dev stack.
  - **`--update-snapshots` was run once** to accept these as the baseline, then every spec was run
    again *without* `--update-snapshots` and confirmed green against what was just written — a real
    check that the comparison mechanism works, not just that the write succeeded.
  - **Wired as Nx targets**: `npx nx run storefront-e2e:visual-regression` and
    `npx nx run admin-e2e:visual-regression` (`nx:run-commands` executors in each app's
    `project.json`, consistent with how every other Nx target in this workspace is declared).
    Update a baseline after an intentional visual change: rerun the same target with
    `-- --update-snapshots`, review the PNGs that changed match the intended change, then commit
    them — the comment at the top of each `playwright.visual.config.mts` says so inline.
  - **`__screenshots__/` was blanket-ignored in `src/frontend/.gitignore`** (line added at an
    earlier step, presumably against ad-hoc local snapshot diffs). That line is removed: these
    baselines are meant to be committed, and a gitignored "baseline" that can never be checked in
    is not a baseline.
  - **A per-pixel tolerance** (`maxDiffPixelRatio: 0.02`, `animations: "disabled"`) absorbs the
    live catalogue imagery and the clock-driven parts of the page without hiding a real
    layout/colour regression, which moves far more than a couple of percent of the frame.

  **Responsive visual QA across the device matrix is now done — this was the last open item in
  this step's deliverable list, and every deliverable it names is now built and verified against
  the running dev stack.** Full write-up:
  [`30-reports/responsive-qa-report.md`](30-reports/responsive-qa-report.md).

  - **Breakpoint matrix is not invented for this pass** — 360×640, 768×1024, 1024×900, 1280×900 are
    the `xs`/`md`/`lg`/`xl` tokens `docs/05-frontend-architecture.md` §3.3 already names, and 360
    is also the exact viewport `docs/09-nfr-testing-observability.md`'s journey #16 names.
  - **Two mechanisms, not one:** a programmatic no-horizontal-overflow assertion
    (`document.documentElement.scrollWidth <= clientWidth`) run across 44 route×breakpoint
    combinations (36 storefront, 8 admin) via two new Playwright configs
    (`apps/storefront-e2e/playwright.responsive.config.mts`,
    `apps/admin-e2e/playwright.responsive.config.mts`) against the same live themed docker dev
    stack the visual-regression baselines use — plus full-page screenshots of the three named
    risk areas (filter panel, mobile nav, cart) at all four breakpoints, actually looked at. All
    44 overflow assertions pass; the filter panel was confirmed to switch from a bottom-sheet
    toggle to a persistent sidebar exactly at `lg`, as §3.3 specifies.
  - **One thing looked like a defect and was not, and it is recorded rather than silently
    dismissed:** a full-page PDP screenshot showed the mobile sticky "Add to cart" bar appearing to
    overlap the price section. That is a known artifact of Playwright's `fullPage` screenshots
    compositing a `position: fixed` element once at its first-viewport offset, not a real bug — a
    real-scroll check (viewport screenshots before and after scrolling) confirmed the bar stays
    correctly pinned to the bottom of the screen with no overlap. No CSS changed.
  - **Coverage is nine storefront routes and the same two admin routes the visual-regression
    baselines justify** (sign-in, forgot-password) — account pages, authenticated checkout, and
    the admin dashboard/tables remain out of reach because no Playwright fixture in this workspace
    scripts a login session past `authenticatedGuard` or the admin's mandatory-2FA
    `platform-admin` role yet. That gap is named in the report rather than worked around with a
    fake session.
  - **Wired as Nx targets**: `npx nx run storefront-e2e:responsive-qa`,
    `npx nx run admin-e2e:responsive-qa`.

  **Still not done, and it is a real gap against the acceptance criteria:**
  - **Dark mode** was decided against for now (client decision: light only). The token structure
    supports adding it.
  - **No scripted authenticated e2e session** exists yet for either app, which is why responsive QA
    (and the visual-regression baselines before it) stop at the screens an anonymous visitor can
    reach. See the responsive QA report §4 for what that leaves uncovered.
