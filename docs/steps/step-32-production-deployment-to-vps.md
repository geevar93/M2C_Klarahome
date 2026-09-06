# Step 32 — Production deployment to VPS

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** H · **Depends on:** Step 31
- **Objective:** Live infrastructure.
- **Deliverables:**
  - VPS hardening (SSH keys only, firewall, fail2ban, unattended security updates, non-root
    container users, resource limits).
  - Production `docker-compose.prod.yml` (or Swarm) with Traefik + Let's Encrypt, all services,
    resource limits, restart policies, log rotation.
  - Secrets management (Docker secrets / SOPS-encrypted env); separate staging and production.
  - Release pipeline: image build → registry → migration job → rolling restart → smoke tests
    → automated rollback on failure.
  - DNS, TLS, CDN/caching configuration, rate limiting and WAF rules at the edge.
- **Acceptance criteria:** Staging and production are both live on HTTPS; a full deploy and a
  rollback are each executed successfully and timed.
- **Outcome / Notes:** _(to be filled on completion)_
