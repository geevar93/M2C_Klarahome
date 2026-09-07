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
- **Status:** 🟡 Partially delivered — the token layer is done and applied; the sign-off,
  white-label proof and asset deliverables are not.
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

  **Not done, and each is a real gap against the acceptance criteria:**
  - **Client sign-off on the working application** has not happened. This was themed from the
    supplied palette without a brand discovery workshop, moodboard or alternative directions.
  - **The white-label proof** — a second, visually distinct demo theme applied end-to-end purely by
    swapping configuration — has not been exercised. It is the formal exit criterion; the mechanism
    is in place but the proof itself is outstanding.
  - **Dark mode** was decided against for now (client decision: light only). The token structure
    supports adding it.
  - **Brand assets**: no logo SVG, favicon set, app icons, PWA manifest, OG images or 404/500 art.
    The wordmark is still text, now set in the display face.
  - **Email and PDF template styling** has not been brought onto the new tokens.
  - **Visual-regression baselines** have not been captured, and the hard-coded-value lint rule the
    placeholder promised was never implemented — there is no stylelint config in the workspace, so
    "never hard-code a colour" is a convention rather than a guardrail.
  - **Responsive visual QA** across the full device matrix has not been run.
