# Expense Claims

ใบเบิกค่าใช้จ่ายของพนักงาน: สร้าง → ส่งอนุมัติ → อนุมัติ → จ่าย.

Employee expense-reimbursement claims, with a submit → approve → pay lifecycle (SoD split across three permissions).

## Expense Claims
- `POST /expense-claims` — create draft. **Auth:** `expense.claim.create`. → `201` `{ expense_claim_id }`.
- `PUT /expense-claims/{id}` — edit (draft only). **Auth:** `expense.claim.create`. → `204`.
- `POST /expense-claims/{id}/submit` — send for approval. **Auth:** `expense.claim.create`. → `204`.
- `POST /expense-claims/{id}/approve` — approve. **Auth:** `expense.claim.approve`. → `200`.
- `POST /expense-claims/{id}/reject` — reject with a reason. **Auth:** `expense.claim.approve`. Body: `{ reason }`. → `204`.
- `POST /expense-claims/{id}/pay` — record payment + post GL. **Auth:** `expense.claim.pay`. → `200`.
- `POST /expense-claims/{id}/cancel` — cancel. **Auth:** `expense.claim.create`. → `204`.
- `GET /expense-claims` — list. **Auth:** `expense.claim.read`. Query: `status?`, `employeeId?`, `from?`, `to?`. → `200`.
- `GET /expense-claims/{id}` — detail. **Auth:** `expense.claim.read`. → `200` / `404`.

The Employee selector on the claim form uses `GET /employees/lookup` (see [payroll.md](payroll.md)) — the narrow, name-only permission, not full employee management.
