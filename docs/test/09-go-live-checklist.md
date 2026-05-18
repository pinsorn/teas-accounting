# 09 — Go-Live Checklist (Pre-production gate)

Before the very first production transaction. Each item ☐ → ✓ + initials + date.
Don't go live unless **every** item checked + owner sign-off + auditor approval.

---

## 1. Functional readiness

| # | Item | Owner | ☐ |
|---|---|---|---|
| 1.1 | Phase 1 all sprints (8.5 → 12) shipped + green | Claude Code | ☐ |
| 1.2 | UAT scenarios (ch.03) all 20 walked through + passed | QA + Ham | ☐ |
| 1.3 | Compliance test (ch.04) 100% pass | Sana + compliance reviewer | ☐ |
| 1.4 | External pen-test passed (no critical/high open) | External vendor | ☐ |
| 1.5 | Performance scenarios (ch.06) within targets | Sana | ☐ |
| 1.6 | Regression full suite passes 5 consecutive nightly runs | CI | ☐ |
| 1.7 | Data migration rehearsed + reconciled (ch.08) | Sana + customer | ☐ |
| 1.8 | Manual PDF inspection ×8 (Sprint 8.5 flag) cleared | Ham | ☐ |

## 2. Infrastructure readiness

| # | Item | Owner | ☐ |
|---|---|---|---|
| 2.1 | Production Postgres cluster provisioned + tuned | Ops | ☐ |
| 2.2 | Backup schedule active (daily full + 6-hour incremental) | Ops | ☐ |
| 2.3 | Backup restore drill completed successfully | Ops | ☐ |
| 2.4 | TLS cert valid + auto-renewal configured | Ops | ☐ |
| 2.5 | Domain + DNS pointed correctly | Ops | ☐ |
| 2.6 | Monitoring + alerting active (uptime, error rate, p95 latency) | Ops | ☐ |
| 2.7 | Log aggregation + 90-day retention | Ops | ☐ |
| 2.8 | DR runbook reviewed + tested | Ops | ☐ |
| 2.9 | Connection pool sizing finalized + load-tested | Ops | ☐ |
| 2.10 | Storage growth projection within plan (1GB/year/tenant) | Ops | ☐ |

## 3. Security readiness

| # | Item | Owner | ☐ |
|---|---|---|---|
| 3.1 | All passwords rotated from dev defaults | Ops | ☐ |
| 3.2 | MFA enabled for all admin accounts (mandatory) | Ops | ☐ |
| 3.3 | MFA AES key (`Mfa.MfaAesKeyBase64`) provisioned as a real 32-byte secret (NOT placeholder) | Ops | ☐ |
| 3.4 | JWT signing key (`Jwt.SigningKey`) is unique + long enough (≥ 256-bit) | Ops | ☐ |
| 3.5 | DB connection string in secret manager (NOT in appsettings) | Ops | ☐ |
| 3.6 | All env files locked (no plaintext secrets in repo) | Code review | ☐ |
| 3.7 | e-Tax PFX certificate installed + access-restricted | Ops + compliance | ☐ |
| 3.8 | Audit log retention configured (5+ years per พรบ.บัญชี) | Ops | ☐ |
| 3.9 | RLS verified active on every ITenantOwned entity | Sana | ☐ |
| 3.10 | Incident response runbook published + on-call rota set | Ops + Ham | ☐ |

## 4. Tax & regulatory readiness

| # | Item | Owner | ☐ |
|---|---|---|---|
| 4.1 | Customer's Digital Cert Class 2 นิติบุคคล purchased + installed | Customer + ops | ☐ |
| 4.2 | Customer registered as Service Provider — Direct Filing with RD | Customer | ☐ |
| 4.3 | Customer registered for e-Tax Invoice by Email with RD | Customer | ☐ |
| 4.4 | RD API credentials provisioned + tested (if Auto mode chosen) | Customer + ops | ☐ |
| 4.5 | RD test environment certified | Customer + Sana | ☐ |
| 4.6 | Compliance reviewer signed off on ch.04 | Compliance reviewer | ☐ |
| 4.7 | VAT rate (`Tax:VatRate=0.07`) verified current (พรฎ. still in force) | Sana | ☐ |
| 4.8 | All 13 WHT types seeded + verified per ม.50ทวิ + ม.3 เตรส (post 8.6) | Sana | ☐ |
| 4.9 | Tax codes for exempt items (ม.81) seeded if needed for customer (Reptify case post Sprint 9) | Sana | ☐ |
| 4.10 | First test ภ.พ.30 generation reviewed by accountant | Customer | ☐ |

## 5. Business readiness

| # | Item | Owner | ☐ |
|---|---|---|---|
| 5.1 | Customer's master data (CoA, Customer, Vendor, Product) fully loaded | Customer | ☐ |
| 5.2 | Opening balances loaded + Trial Balance ties out (ch.08) | Sana + customer accountant | ☐ |
| 5.3 | All in-flight AR/AP carried over correctly | Sana + customer | ☐ |
| 5.4 | Business Units defined + flag set if multi-BU (post Sprint 8) | Customer | ☐ |
| 5.5 | User accounts provisioned + roles assigned | Customer admin | ☐ |
| 5.6 | All users have completed training | Trainer + users | ☐ |
| 5.7 | User manual (Sprint 13b) reviewed + accessible | Sana + customer | ☐ |
| 5.8 | Document number format + prefix registry configured per company | Sana + customer | ☐ |
| 5.9 | Sub-prefixes for PV expense categories + BUs configured | Customer | ☐ |
| 5.10 | Support channel established (email, phone, response SLA) | Ham | ☐ |

## 6. Cutover plan

| # | Item | Owner | ☐ |
|---|---|---|---|
| 6.1 | Cutover date agreed in writing | Ham + customer | ☐ |
| 6.2 | Old system frozen as of cutover date (read-only) | Customer | ☐ |
| 6.3 | Migration data import completed + reconciled | Sana + customer | ☐ |
| 6.4 | First test transaction in production (TI) — successful POST + PDF | Customer accountant | ☐ |
| 6.5 | First test e-Tax email sent + RD ack received (if applicable) | Customer accountant | ☐ |
| 6.6 | Number sequences continue from last old-system numbers (if customer wants continuity) | Sana | ☐ |
| 6.7 | Communication to internal team + suppliers (if relevant) sent | Customer | ☐ |
| 6.8 | First-day support presence scheduled (someone available for issues) | Ham + ops | ☐ |

## 7. Post-go-live monitoring (first 7 days)

| # | Item | Owner | ☐ |
|---|---|---|---|
| 7.1 | Daily check-in with customer (any issues?) | Ham | ☐ |
| 7.2 | Daily TB tie-out vs expected | Customer accountant | ☐ |
| 7.3 | Audit log review (anything suspicious?) | Sana | ☐ |
| 7.4 | Error rate < 0.5% sustained | Ops | ☐ |
| 7.5 | p95 latency within target | Ops | ☐ |
| 7.6 | No data loss / corruption | Sana + customer | ☐ |
| 7.7 | All gates still green | Ops | ☐ |
| 7.8 | After day 7: post-go-live review + lessons learned | Everyone | ☐ |

## 8. Rollback plan (if catastrophic failure in first 7 days)

| # | Item | Owner | ☐ |
|---|---|---|---|
| 8.1 | Decision authority defined (who calls rollback) | Ham + customer | ☐ |
| 8.2 | Rollback procedure documented + tested | Ops + Sana | ☐ |
| 8.3 | Old system reactivation path documented | Customer | ☐ |
| 8.4 | Communication plan for customers/suppliers if rollback | Ham | ☐ |

---

## Sign-off

```
Phase 1 Go-Live Approval

Date:                    ___________

Functional readiness:    [✓] (Claude Code lead)        Date: _____  Initial: _____
Infrastructure:          [✓] (Ops lead)               Date: _____  Initial: _____
Security:                [✓] (Sana + pen-test vendor) Date: _____  Initial: _____
Tax & regulatory:        [✓] (Compliance reviewer)     Date: _____  Initial: _____
Business:                [✓] (Customer admin)          Date: _____  Initial: _____
Cutover:                 [✓] (Ham + customer)          Date: _____  Initial: _____

CTO/Owner final approval: [✓]                          Date: _____  Initial: _____

Production go-live time:  ___________
```

**This document is binding.** No item may be skipped. If unable to complete, document
why + risk acceptance + escalate to Ham for go/no-go.
