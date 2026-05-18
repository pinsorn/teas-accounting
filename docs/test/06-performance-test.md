# 06 — Performance Test

**Goal:** Verify TEAS meets NFR targets in `accounting-system-plan.md` §3.3 + §21.1 under realistic load.

## Performance targets (Phase 1)

| Operation | Target p50 | Target p95 | Target p99 |
|---|---|---|---|
| Login (incl MFA) | < 200ms | < 500ms | < 1s |
| Dashboard load | < 300ms | < 800ms | < 1.5s |
| TI list (50 items, page 1) | < 150ms | < 400ms | < 800ms |
| TI POST (single, simple) | < 200ms | < 500ms | < 1s |
| TI POST (10-line, with WHT + BU) | < 300ms | < 700ms | < 1.5s |
| Receipt POST (apply 5 TIs) | < 400ms | < 1s | < 2s |
| PV POST (with WHT + 50ทวิ PDF gen) | < 500ms | < 1.5s | < 3s |
| VI POST (10-line + JV + Input VAT register) | < 400ms | < 1s | < 2s |
| Number-gap audit (1 month, 10k docs) | < 1s | < 3s | < 5s |
| Trial Balance report (1 month) | < 2s | < 5s | < 8s |
| ภ.พ.30 generator (1 month) | < 3s | < 7s | < 12s |
| PDF generation (single TI) | < 500ms | < 1s | < 2s |
| Bulk import (1000 customers) | < 30s | < 60s | < 90s |

## Load scenarios

### Scenario 1 — Daily SME usage

**Profile:** 5-15 concurrent users, mix of read (70%) + write (30%) operations.

```js
// k6 script outline
import http from 'k6/http';
import { check } from 'k6';

export const options = {
  vus: 15,
  duration: '10m',
  thresholds: {
    http_req_duration: ['p(95)<800'],
    http_req_failed: ['rate<0.01'],
  },
};

export default function () {
  // Mix realistic SME daily operations
  const flows = [
    () => listTaxInvoices(),         // 30%
    () => viewTaxInvoiceDetail(),    // 20%
    () => createDraftTi(),           // 15%
    () => postTaxInvoice(),          // 15%
    () => createReceipt(),           // 10%
    () => viewDashboard(),           // 10%
  ];
  pickWeighted(flows)();
}
```

**Expected:** All p95 within targets. Error rate < 1%.

### Scenario 2 — Month-end close

**Profile:** 3-5 accountants running close activities simultaneously — heavy report queries + journal posting.

| Action | Concurrency | Duration | Target |
|---|---|---|---|
| Trial Balance generation | 3 simultaneous | 5 min | All complete < 8s p99 |
| ภ.พ.30 generation | 1 (single tenant) | — | < 12s p99 |
| Multiple JV posts | 5/sec for 2 min | — | All POST < 2s p99 |
| Read concurrent with writes | 10 readers | 5 min | No deadlocks, < 5s p99 |

### Scenario 3 — Peak transaction burst

**Profile:** Year-end last week, all customers rush to invoice — sustained write load.

| Action | Rate | Duration |
|---|---|---|
| TI POST | 20/sec sustained | 30 min |
| Each TI: 5 lines, BU set, immediate PDF generation request |

**Expected:** No 5xx errors. Number sequence has zero gaps (atomic INSERT ON CONFLICT). DB connections stable.

### Scenario 4 — Concurrent gapless numbering stress

**Critical test:** verify number sequences are gapless under concurrent POST.

```js
// 100 simultaneous TI POSTs with same company_id + doc_type + month
export default function () {
  postTaxInvoiceAsync();
}

export const options = {
  scenarios: {
    burst: {
      executor: 'shared-iterations',
      vus: 100,
      iterations: 100,
    },
  },
};
```

**Assertion:** After all 100 complete, query `SELECT MAX(doc_no), COUNT(*)`. Doc numbers must be `0001..0100` with no gap.

### Scenario 5 — Soak test

**Profile:** Sustained 10 VU for 24 hours. Detect memory leaks, connection leaks, slow degradation.

**Pass criteria:**
- No OOM
- DB connection count stable
- p95 latency stays within target (no creep)
- No accumulating background task lag

---

## Database performance

### Index validation

For every query in code, EXPLAIN ANALYZE must show:
- Index scan (not seq scan) on > 10k row tables
- No nested loops on > 1k row joins (hash join required)

**Tooling:** pgbadger nightly review against slow query log.

### Connection pool tuning

- Production target: PgBouncer in transaction-pooling mode
- Pool size: matched to (CPU cores × 2) + spare
- Test: at peak load, no connection wait > 100ms

### Hot path queries

| Query | Frequency | Optimization |
|---|---|---|
| TI list cursor pagination | Every list view | Covering index on (company_id, doc_date DESC, tax_invoice_id) |
| Number-gap audit | Daily + on-demand | Materialized view or efficient window query |
| ภ.พ.30 register | Monthly | Partitioned by period_id, indexed by (company_id, period) |
| AR aging | Daily | Computed column or scheduled refresh |
| Audit log search | On-demand | BRIN index on timestamp (append-only friendly) |

---

## Frontend performance

| Metric | Target | Tool |
|---|---|---|
| First Contentful Paint | < 1.5s | Lighthouse |
| Largest Contentful Paint | < 2.5s | Lighthouse |
| Time to Interactive | < 3s | Lighthouse |
| Bundle size (per route) | < 250KB | Next bundle analyzer |
| TanStack Query stale-while-revalidate hit rate | > 80% | Custom telemetry |

---

## Test infrastructure

### k6 setup

```
backend/perf/
  ├── scenarios/
  │   ├── 01-daily-usage.js
  │   ├── 02-month-end-close.js
  │   ├── 03-peak-burst.js
  │   ├── 04-gapless-stress.js
  │   └── 05-soak.js
  ├── lib/
  │   ├── auth.js
  │   ├── helpers.js
  │   └── data-gen.js
  └── k6.config.js
```

### Run cadence

- **Per PR:** quick smoke (Scenario 1, 1 min) — fail if p95 > 2× target
- **Weekly:** full Scenario 1 (10 min) + Scenario 2 (10 min)
- **Pre-release:** All scenarios + soak (Scenario 5) overnight

### Reporting

- k6 → Grafana k6 cloud OR self-hosted InfluxDB + Grafana
- Trend charts: p50/p95/p99 over time per scenario
- Alert if regression > 20% vs baseline

---

## Capacity planning (Phase 1)

Initial deployment target:
- Single tenant, 5-15 users
- Up to 10k TIs/year
- Up to 50k JV lines/year
- Storage growth: ~1GB/year per tenant

Scaling triggers:
- > 50 concurrent users → consider HA Postgres + read replicas
- > 100k TIs/year → table partitioning by year
- > 5GB DB → schedule archive policy for closed periods

Phase 2 multi-tenant SaaS hosting → revisit completely.

---

## Performance regression policy

If any p95 > 110% of baseline for 3 consecutive runs → investigate before next release.

If regression confirmed → block release until root cause + fix + verified back to baseline.
