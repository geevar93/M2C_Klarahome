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
- **Outcome / Notes:** _(to be filled on completion)_
