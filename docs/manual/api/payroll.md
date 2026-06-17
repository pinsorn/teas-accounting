# Payroll

เงินเดือน: ข้อมูลพนักงาน รอบจ่ายเงินเดือน สลิป ภ.ง.ด.1 / 1ก ประกันสังคม (สปส.1-10) และ 50ทวิ.

Employee master plus the payroll-run lifecycle and its statutory outputs. Payroll data is sensitive — employee CRUD is admin-only; runs use a SoD permission split (`payroll.run.manage` for draft/read, `payroll.run.post` for approve/post, `payroll.run.pay` for marking paid). Posted runs are immutable (no edit endpoint).

## Employees
All gated by `master.employee.manage`.
- `POST /employees` — create. Body (key fields): `employeeCode`, `firstNameTh`, `lastNameTh`, `nationalId` (required); `titleTh?`, name-EN parts, `taxId?`, `address?`, `hireDate`, `terminationDate?`, `baseSalary` (decimal), bank fields, `ssoApplicable` (bool), `ssoNumber?`, `maritalStatus`, `spouseHasIncome` (bool), `childrenCount` (int) (`CreateEmployeeRequest`). → `201`.
- `PUT /employees/{id}` — update (`UpdateEmployeeRequest`, adds `isActive`). Path `id` (long). → `204`.
- `DELETE /employees/{id}` — soft-deactivate. → `204`.
- `GET /employees` — list. Query: `includeInactive?` (bool). → `200`.
- `GET /employees/{id}` — detail. → `200` / `404`.

## Payroll Runs
Mounted at `/payroll/runs`. The group requires authentication; each route adds its specific permission.
- `POST /payroll/runs` — create draft (auto-creates a payslip per active employee). **Auth:** `payroll.run.manage`. Body: `periodYearMonth` (YYYYMM, CE), `payDate`, `notes?` (`CreatePayrollRunRequest`). → `201`.
- `POST /payroll/runs/{id}/approve` — approve. **Auth:** `payroll.run.post`. → `204`.
- `POST /payroll/runs/{id}/post` — post to GL. **Auth:** `payroll.run.post`. → `204`.
- `POST /payroll/runs/{id}/pay` — mark paid. **Auth:** `payroll.run.pay`. → `204`.
- `DELETE /payroll/runs/{id}` — delete draft. **Auth:** `payroll.run.manage`. → `204`.
- `GET /payroll/runs` — list. **Auth:** `payroll.run.manage`. → `200`.
- `GET /payroll/runs/{id}` — detail. **Auth:** `payroll.run.manage`. → `200` / `404`.

## Statutory documents
All gated by `payroll.run.manage`.
- `GET /payroll/runs/{id}/payslips/{employeeId}/pdf` — single payslip. → `application/pdf`.
- `GET /payroll/runs/{id}/payslips/pdf` — all payslips for the run, zipped. → `application/zip`.
- `GET /payroll/runs/{id}/pnd1/pdf` — ภ.ง.ด.1 (monthly WHT return + ใบแนบ). → `application/pdf`.
- `GET /payroll/runs/{id}/sso/file` — SSO สปส.1-10 monthly upload file (TIS-620 fixed-width). → `text/plain`.
- `GET /payroll/runs/{id}/sso/pdf` — สปส.1-10 ส่วนที่ 1 PDF. → `application/pdf`.
- `GET /payroll/pnd1a/pdf` — ภ.ง.ด.1ก (annual, ม.58(1); aggregates posted runs). Query: `year` (CE). → `application/pdf`.
- `GET /payroll/employees/{employeeId}/wht50tawi/pdf` — annual 50ทวิ for one employee (ม.50ทวิ). Query: `year` (CE). → `application/pdf`.
