# Finding — co7's employee names are corrupted Thai, not placeholders (Fable, 2026-07-29)

Raised while reviewing Leg B. That leg saw `???????` where employee names should be and recorded it as
"literally stored as placeholder text (confirmed via the employee master list, **not a bug**)".
That conclusion is wrong. It is real data corruption, and it was worth checking because the same field
is printed on two legal forms.

## Evidence — byte lengths on prod, not appearance

```sql
select company_id, employee_code, first_name_th, last_name_th, octet_length(first_name_th)
from master.employees where company_id in (6,7);
```

| company | code | th name | octet_length |
|---|---|---|---|
| co6 | NVEMP-B2NV | `ทดสอบ พนักงานเอ็นวี` | **15** |
| co6 | PRA01 | `เอสอง ปกติ` | **15** |
| co6 | PRB01 | `บีสอง เข้ากลางเดือน` | **15** |
| co6 | PRC01 | `ซีสอง ออกกลางเดือน` | **15** |
| co7 | O8FULL | `???? ?????` | **4** |
| co7 | O8MID | `???? ?????` | **4** |
| co7 | O8OUT | `??? ?????????` | **3** |

Thai codepoints are 3 bytes each in UTF-8, so co6's 5-character names measuring 15 bytes are intact.
co7's names measure one byte per character — they are literal ASCII `?`. The Thai never arrived; it was
replaced with `?` before the database ever saw it.

## Root cause — the client, NOT the application

co6's employees were created **through the UI in Chrome** (see `PROGRESS-army-untested.md`) and their
Thai is perfect. co7's three were created **through the API from PowerShell** during the O8 proration
work on 2026-07-26. Same application, same endpoint, same database — different client, and only the
PowerShell-originated rows are damaged. PowerShell's default output encoding degrades non-ASCII to `?`
on the way out, so the API received `?` and stored exactly what it was sent.

**So this is not a product defect and needs no code change.** The app round-trips Thai correctly, which
co6 demonstrates.

## Why it still matters

`first_name_th` / `last_name_th` are printed on **ภ.ง.ด.1** and on **สปส.1-10 ส่วนที่ 2** — both legal
filings where the employee's name is the identifying field. On co7 those forms currently render `????`.

Consequences:
- **co7 cannot be used to verify name rendering on any RD/SSO output.** Leg B's checks that the SSO
  schedule "renders names correctly" prove nothing on this company; its numeric assertions are
  unaffected and still stand.
- Use **co6** (now unfrozen by O14, and holding correct Thai names) for any future form-rendering check.

## Recommended fix — NOT applied, needs Ham

Repair the three names through the UI (or an API call made from a UTF-8-clean client). Deliberately not
done here: it is a write to production data and Ham was asleep; the authorisation given was to test, not
to mutate master data unattended. It is a two-minute edit whenever he wants it.

## Process lesson

When creating Thai (or any non-ASCII) data on this system from a script, do not drive the API from
PowerShell without forcing UTF-8 — the corruption is silent, the API returns 200, and it only surfaces
later on a printed form. Prefer the UI, or verify with `octet_length` immediately after writing.
