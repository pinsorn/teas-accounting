# 05 — Security Test

**Threat model:** internal misuse > external attack (for financial systems). Most
real-world incidents are insider — an accountant deletes evidence, a sales rep sees
competitor pricing through tenant leak, an admin disables audit log. External
attack (SQLi, XSS, CSRF) is secondary but covered.

---

## 1. Authentication

| Test | Tool | Status |
|---|---|---|
| Login with wrong password → 401, no user info leaked | integration | ✅ |
| Login rate-limit (after 5 fails, 15-min lockout) | integration | ⏳ Phase 1 ปลาย |
| JWT signature tampering → 401 | integration | ✅ |
| JWT expiry enforced | integration | ✅ |
| Refresh token rotation (old refresh token invalid after use) | integration | ⏳ Phase 1 ปลาย |
| MFA TOTP — wrong code 3 times → lockout | integration | ⏳ Phase 1 ปลาย |
| MFA TOTP — replay (same code twice within 30s window) → 2nd rejected | integration | ✅ |
| Password hash uses bcrypt or pgcrypto crypt() (not MD5/SHA) | code review + assertion | ✅ |
| Session timeout (idle > 30 min) | e2e | ⏳ |

---

## 2. Authorization (RBAC)

### Permission matrix coverage

Target: 12 roles × 30+ permissions = 360 grant cells. Test that every CELL is correct.

```csharp
[Theory]
[MemberData(nameof(RbacMatrix))]
public async Task RoleCannot_DoAction_Unless_Granted(
    string role, string perm, string endpoint, HttpMethod method, int expectedStatus)
{
    var token = await LoginAsRoleAsync(role);
    var response = await Client.SendAsync(/* ... endpoint with token */);
    response.StatusCode.Should().Be(expectedStatus);
}

public static IEnumerable<object[]> RbacMatrix()
{
    // Auto-generate from Permissions.cs + role_permissions seed
    // Yield ~360 test cases — covers every grant/deny combination
}
```

### Key SoD tests

| Scenario | Expected | Status |
|---|---|---|
| PV creator approves their own PV → 403 | ck_pv_sod CHECK rejects | ✅ |
| PV approver = creator at DB level → CHECK violation | direct INSERT bypass attempt | ✅ |
| Bypass app, raw SQL insert violating SoD | RLS + CHECK enforced regardless | ✅ |
| Approver MAY also be poster (2-person SME case) | allowed | ✅ |

---

## 3. Multi-tenant isolation (RLS)

The single most important security boundary.

| Test | Method | Status |
|---|---|---|
| Company A user CANNOT GET company B TI list | integration (TenantIsolationTests) | ✅ |
| Company A user CANNOT GET company B TI by ID (404 not 403 — don't reveal existence) | integration | ✅ |
| Company A user CANNOT POST creating row with company_id=B | integration | ✅ |
| Cross-tenant FK reference (e.g., TI references Customer from another company) → reject | integration | ✅ |
| RLS active on every ITenantOwned entity | reflection test scans entity registry | ✅ |
| Super-admin bypass works for legitimate purposes (e.g., system queries) | integration | ✅ |
| Super-admin bypass logged in audit_log every time | integration | ✅ |
| Direct DB query without `app.company_id` set → returns 0 rows (fail closed) | direct npgsql test | ✅ |

**Adversarial scenarios (paranoid mode):**

| Attack | Test | Status |
|---|---|---|
| User changes JWT claim `company_id` to other tenant | JWT signature invalid → 401 | ✅ |
| User passes `?company_id=other` in query string | ignored (RLS uses session var, not query) | ✅ |
| Admin SQL injection in customer name field | parameterized queries — injection blocked | ✅ (smoke) |
| Mass enumeration of TI IDs across tenants | RLS returns 0 rows for unauthorized | ✅ |

---

## 4. Audit log integrity

| Test | Status |
|---|---|
| Every POSTED action recorded with user_id + timestamp | ✅ |
| UPDATE on activity_logs → trigger reject | ✅ |
| DELETE on activity_logs → trigger reject | ✅ |
| TRUNCATE on activity_logs → super-admin only + logged | ✅ |
| Audit log entries cannot be re-ordered (clustered chronologically) | ✅ |
| Tampered audit log detection (hash chain — Phase 2) | Phase 2 |

---

## 5. OWASP Top 10 coverage

| Risk | Mitigation | Test |
|---|---|---|
| A01 Broken access control | RLS + RBAC + SoD | sections 2, 3 above |
| A02 Cryptographic failures | bcrypt/crypt, TLS 1.2+, MFA AES key 32-byte | OWASP ZAP scan + manual |
| A03 Injection | EF parameterized + FluentValidation | manual sqlmap (controlled) + code review |
| A04 Insecure design | Threat model + spec-first migration | architectural review |
| A05 Security misconfig | env-locked configs, no debug in prod | runbook check |
| A06 Vulnerable components | Renovate/Dependabot + monthly audit | ✅ (pinned versions) |
| A07 Identification & auth failures | section 1 above | ✅ |
| A08 Software & data integrity failures | Immutability triggers + audit log | ✅ |
| A09 Logging & monitoring failures | activity_logs append-only + structured logs | ✅ |
| A10 SSRF | external API calls allowlist (RD endpoints only) | runbook check |

---

## 6. Sensitive data handling

| Data | Storage | Test |
|---|---|---|
| Password | bcrypt hash | ✅ never plaintext in DB, never logged |
| MFA seed | AES-encrypted at rest | ✅ key in env, never logged |
| Tax ID (PII) | Plain text (legally required for matching) | encrypt at rest via TDE (Phase 2) |
| Customer email | Plain text | encrypt at rest via TDE (Phase 2) |
| JWT in transit | HTTPS-only via BFF cookie pattern (httpOnly) | ✅ |
| PFX file (e-Tax sign) | encrypted with passphrase, restricted file perms | ✅ |
| HSM keys (Phase 2) | Azure Key Vault Managed HSM | Phase 2 |

---

## 7. Penetration test scope (pre-go-live)

External vendor mandatory before production. Scope:

| Area | Method |
|---|---|
| Web application | OWASP Top 10 walkthrough |
| API | Burp Suite + custom fuzzing per OpenAPI |
| Auth | Brute force, MFA bypass, session fixation |
| RLS | Cross-tenant via manual + automated probe |
| Backup/restore | DR test + tamper attempt during restore |
| Network | Port scan, TLS config (testssl.sh) |
| Infrastructure | Server hardening (CIS benchmark) |

**Expected duration:** 5-10 working days. **Deliverable:** report + remediation list +
re-test. **Cost:** ฿80k-200k typical for Thai security vendor.

**Findings classification:**
- Critical → block go-live until fixed
- High → fix before go-live
- Medium → fix within 30 days post-go-live
- Low → backlog

---

## 8. Compliance with PDPA (พรบ.คุ้มครองข้อมูลส่วนบุคคล)

See ch.04 §5 for legal mapping. Key tests reinforced here:

| Right | Implementation | Test |
|---|---|---|
| Right to access (Section 30) | User can request data export of their own records | Manual workflow Phase 1 |
| Right to be forgotten | Soft-delete + audit-preserved deletion record | Manual workflow Phase 1 |
| Data breach 72h notification | Operational runbook + monitoring alerts | Runbook |
| Lawful basis documented | Privacy policy + consent capture (sign-up) | Manual review |
| DPO designated | Org doc | Manual |

---

## 9. Periodic security checks (ongoing)

| Check | Frequency | Tool |
|---|---|---|
| Dependency audit (npm audit + dotnet list package --vulnerable) | Weekly | CI |
| OWASP ZAP scan against staging | Weekly | scheduled job |
| SSL cert expiry check | Daily | monitoring |
| Failed login monitoring | Real-time | log alert |
| Suspicious activity (e.g., super-admin elevation) | Real-time | log alert |
| Backup integrity (restore test) | Monthly | DR runbook |

---

## 10. Incident response

If a security incident is detected:
1. **Contain** — disable affected accounts/endpoints immediately
2. **Investigate** — review activity_logs + system logs
3. **Notify** — PDPA 72h rule + customer notification if breach
4. **Remediate** — patch + verify
5. **Document** — post-mortem within 14 days

Runbook lives separately (`docs/ops/incident-response.md` — Phase 1 ปลาย).
