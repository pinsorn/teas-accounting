# System

ระบบและเครื่องมือทั่วไป: health check, ข้อมูลระบบ, สถานะเกณฑ์ VAT, การปิดงวดบัญชี, สมุดรายวัน และไฟล์แนบ.

Infrastructure, ledger, and cross-cutting utility endpoints.

## Health & info
- `GET /health` — liveness/readiness health check. **Auth:** Anonymous. Returns the health-check report.
- `GET /system/info` — instance + tax-config info for the caller's company (served by `ICompanyTaxConfigService`). **Auth:** Authenticated. Returns `200`.
- `GET /system/vat-threshold-status` — whether the company is approaching/over the VAT-registration revenue threshold. **Auth:** Authenticated. Returns `200`.

## Periods (งวดบัญชี)
- `POST /periods/{year}/{month}/close` — close an accounting period. **Auth:** `gl.period.close`. Path: `year`, `month` (int). Body (optional): `{ notes?: string }` (`ClosePeriodRequest`). Returns `200`.
- `GET /periods/{year}/{month}/status` — period open/closed status. **Auth:** Authenticated. Path: `year`, `month` (int). Returns `200` `{ open: bool }`.

## Journals (สมุดรายวัน / GL)
- `POST /journals` — create a manual journal entry. **Auth:** `gl.journal.create`. Body: journal header + balanced debit/credit lines (see `openapi.yaml`). Returns `201`.
- `POST /journals/{id}/post` — post the journal to the ledger. **Auth:** `gl.journal.post`. Path: `id` (long). Returns `200`/`204`.

## Attachments
Polymorphic attachment store (`/attachments`). Gated by `sys.attachment.upload` (write) / `sys.attachment.read` (read).
- `POST /attachments` — upload a file (`multipart/form-data`). **Auth:** `sys.attachment.upload`. Returns `201`.
- `GET /attachments` — list attachments for an owner entity. **Auth:** `sys.attachment.read`. Returns `200`.
- `GET /attachments/categories` — list attachment categories. **Auth:** `sys.attachment.read`. Returns `200`.
- `GET /attachments/{id}/download` — download the file. **Auth:** `sys.attachment.read`. Path: `id` (long). Returns the file stream.
- `DELETE /attachments/{id}` — delete an attachment. **Auth:** `sys.attachment.read`. Path: `id` (long). Returns `204`.
