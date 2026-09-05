## Step

<!-- Which step of docs/IMPLEMENTATION_PLAN.md does this belong to? -->
Step: <!-- e.g. Step 3 — Backend solution skeleton -->

## What changed and why

<!-- The WHY. Link the spec section this implements. -->

## Definition of Done

<!-- docs/09-nfr-testing-observability.md §2.4 — tick or strike through with a reason -->

- [ ] Acceptance criteria in the step card are demonstrably met
- [ ] Unit + integration tests written and passing; coverage threshold held
- [ ] API changes reflected in the OpenAPI document and the generated Angular client
- [ ] Migrations reviewed as SQL, applied and rolled back cleanly on a scratch database
- [ ] Authorisation applied and tested for every new endpoint
- [ ] Logging, metrics and audit entries added for new significant actions
- [ ] No new analyzer/lint warnings; no new critical/high vulnerabilities
- [ ] Documentation updated (`docs/`, plus an ADR if a decision changed)
- [ ] Module boundaries respected (no cross-schema or cross-module internal access)
- [ ] No hard-coded branding, colour, font or spacing values
- [ ] No secrets added to the repo, images or logs

## Scope check

- [ ] This PR contains **only** work from the authorised step
- [ ] Anything else discovered has been added to the plan's Parking Lot, not fixed here

## Risk

<!-- Money, stock, auth or personal data touched? Rollback plan? -->

## Verification

<!-- How a reviewer reproduces this: commands, URLs, test names, screenshots -->
