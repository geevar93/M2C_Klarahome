# Step 31 — Observability, backups & operational runbook

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

- **Phase:** H · **Depends on:** Step 30
- **Objective:** Make the system operable and recoverable.
- **Deliverables:**
  - Centralised logs (Loki), metrics (Prometheus), dashboards (Grafana), traces (OTel/Tempo),
    uptime checks and alert routing.
  - Business/technical alerts: payment webhook failure rate, orders stuck in a state, error
    rate, latency, queue depth, disk/CPU/memory, certificate expiry, failed payouts.
  - Automated encrypted Postgres backups with off-VPS copies, PITR/WAL archiving, MinIO backup,
    and a **documented, tested restore drill**.
  - Runbooks: deploy, rollback, restore, rotate secrets, scale, incident response, on-call.
- **Acceptance criteria:** A restore drill succeeds from backup into a clean environment; a
  simulated failure triggers the correct alert.
- **Outcome / Notes:** _(to be filled on completion)_
