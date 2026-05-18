# Answer-Sana-Backend16 — Sprint 11: File Attachment (polymorphic)

**Date:** 2026-05-18
**From:** Ham (via Sana, Cowork)
**To:** Claude Code
**Re:** Polymorphic file attachment system supporting all document entities
**Gate:** **Small focused sprint ~3-5 days. Single phase (no Part split).**
**Estimate:** ~3-5 days human-equivalent

> Sprint 11 lands the **last piece of infrastructure** missing from Phase 1 — file
> attachments. Used cases pent up across many sprints: vendor TI scans (Sprint 5.5
> "gas station scenario"), customer 50ทวิ scans (Sprint 8.6 AR-WHT cert lookup),
> foreign vendor invoice PDFs (Sprint 8.7 AWS bills), employee expense-claim form
> + receipt original (Option-A reimburse workflow), internal PO copies (Sprint 12).
> All consumers exist + waiting.

---

## 0. Pre-spec audit (Sana — emergent discipline applied)

| Check | Result | Sprint 11 impact |
|---|---|---|
| `Attachment` entity in Domain | ❌ doesn't exist (only MailKit attachment in ETaxEmailSender, unrelated) | Build from scratch |
| `sys.attachments` schema | ❌ doesn't exist | Build from scratch |
| `IFileStorageService` / similar | ❌ doesn't exist | Build minimal abstraction (LocalDisk impl Phase 1) |
| `attachment_url` references in existing DTOs | ⚠ check — VendorInvoiceCreateRequest had `attachment_url` field in early openapi.yaml (Sprint 5.5 era) but may have been dropped | Verify: if present anywhere as scaffold, decide reuse vs deprecate |
| Storage path conventions | N/A — greenfield | Define: `{StorageRoot}/{company_id}/{parent_type}/{parent_id}/{guid}-{safe_filename}` |
| BFF proxy pattern for file streams | ✅ Next.js BFF exists (cookie-based JWT) | Use it for upload/download (don't expose backend directly) |

**Outcome:** Sprint 11 is clean greenfield. Minor recommendation: spec includes "audit pass" step in P1 to check for any `attachment_url` strings in existing DTOs/UI that need wiring (likely 0 — but flag if found).

---

## 1. Concept

**Polymorphic design:** single `sys.attachments` table, every row links to a parent
entity via `(parent_type, parent_id)`. One file → one row. Multiple files per parent
allowed.

**Why polymorphic vs per-parent table:**
- One table = one storage backend interface = one upload/download/delete flow
- New entities (PO from Sprint 12, future Phase 2 entities) need NO schema change
- Trade-off: weaker referential integrity (no FK from `parent_id` to any specific table) — mitigated by application-layer validation + soft-delete pattern
- Trade-off: harder to ON DELETE CASCADE — mitigated by app-layer cleanup hook on parent void/delete (rare in this system due to immutability)

**Categories enum** lets users + reports distinguish attachment purposes (e.g., "show me all WHT-cert scans for last month" filters by category).

---

## 2. Schema

```
sys.attachments
  attachment_id        BIGINT IDENTITY PK
  company_id           INT NN                 -- RLS via app.company_id

  -- Polymorphic parent link
  parent_type          VARCHAR(30) NN         -- enum (see §2.1)
  parent_id            BIGINT NN

  -- Category
  category             VARCHAR(30) NN         -- enum (see §2.2)

  -- File metadata
  file_name            VARCHAR(255) NN        -- original upload name (sanitized)
  mime_type            VARCHAR(100) NN
  size_bytes           BIGINT NN              -- bytes (informational + quota tracking)
  storage_path         VARCHAR(500) NN        -- relative path under StorageRoot

  -- Audit
  uploaded_at          TIMESTAMPTZ NN
  uploaded_by          BIGINT NN FK sys.users
  deleted_at           TIMESTAMPTZ NULL       -- soft-delete
  deleted_by           BIGINT NULL

  -- Optional metadata
  description          VARCHAR(500) NULL      -- e.g. "Receipt for fuel + service"
  page_count           INT NULL               -- for PDFs (informational)

  INDEX ix_attachments_parent (company_id, parent_type, parent_id) WHERE deleted_at IS NULL
  INDEX ix_attachments_category (company_id, category, uploaded_at DESC) WHERE deleted_at IS NULL

  ITenantOwned (CompanyId)
```

### 2.1 `parent_type` enum (values)

```
'VENDOR_INVOICE'
'PAYMENT_VOUCHER'
'RECEIPT'
'TAX_INVOICE'
'TAX_ADJUSTMENT_NOTE'   -- CN + DN
'JOURNAL_ENTRY'
'QUOTATION'             -- Sprint 10
'SALES_ORDER'           -- Sprint 10
'DELIVERY_ORDER'        -- Sprint 10
'PURCHASE_ORDER'        -- Sprint 12 (forward-compat — add to enum now)
```

Validation: `parent_type IN (allowlist)`. Application-layer also verifies row with `parent_id` exists in the corresponding table on upload (NOT on read — read tolerates dangling rows for audit history).

### 2.2 `category` enum (values)

```
'TAX_INVOICE'           -- vendor's TI scan we received (for VI)
'RECEIPT'               -- vendor's plain receipt (no TI) (for VI/PV)
'PURCHASE_ORDER'        -- our PO copy (for VI cross-ref)
'DELIVERY_ORDER'        -- vendor's DO confirmation (for VI)
'QUOTATION'             -- vendor's quote (for VI)
'WHT_CERT_50TAWI'       -- customer's 50ทวิ to us (AR-WHT on RC) OR our 50ทวิ we issued (AP-WHT on PV) — direction inferred from parent
'BANK_SLIP'             -- payment confirmation, transfer slip
'CONTRACT'              -- service agreement, retainer
'EXPENSE_CLAIM_FORM'    -- employee reimbursement (Option A workflow)
'CUSTOMS_DECL'          -- import/export docs (foreign vendor)
'OTHER'                 -- catch-all with required description
```

UI shows category badge for each attachment in detail page. Filter by category in lists.

---

## 3. Storage abstraction

```csharp
public interface IFileStorageService
{
    Task<string> SaveAsync(int companyId, string parentType, long parentId, Stream content, string suggestedFileName, CancellationToken ct);
    // Returns: relative storage_path (e.g. "1/TAX_INVOICE/42/a1b2c3-Untitled.pdf")

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct);
    Task DeleteAsync(string storagePath, CancellationToken ct);
    Task<bool> ExistsAsync(string storagePath, CancellationToken ct);
}
```

### Phase 1 implementation: `LocalDiskFileStorage`

```csharp
public sealed class LocalDiskFileStorage(IOptions<FileStorageOptions> opts) : IFileStorageService
{
    private readonly string _root = opts.Value.StorageRoot;   // e.g. "/var/teas/attachments"

    public async Task<string> SaveAsync(...) {
        var guid = Guid.NewGuid().ToString("N")[..16];
        var safeName = SanitizeFileName(suggestedFileName);
        var relPath = $"{companyId}/{parentType}/{parentId}/{guid}-{safeName}";
        var fullPath = Path.Combine(_root, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var fs = File.Create(fullPath);
        await content.CopyToAsync(fs, ct);
        return relPath;
    }
    // ... OpenReadAsync uses File.OpenRead(Path.Combine(_root, storagePath))
    // ... DeleteAsync uses File.Delete
}
```

Config:
```json
"FileStorage": {
  "StorageRoot": "/var/teas/attachments",  // production
  "MaxFileSizeMb": 25,                      // per file
  "AllowedMimeTypes": [
    "application/pdf",
    "image/jpeg", "image/png", "image/webp",
    "application/vnd.ms-excel",
    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    "application/vnd.ms-outlook"   // for .msg files
  ]
}
```

### Phase 2 path: `AzureBlobFileStorage` / `S3FileStorage`

Same interface, different impl. Swap via DI registration:
```csharp
if (config["FileStorage:Provider"] == "AzureBlob")
    services.AddSingleton<IFileStorageService, AzureBlobFileStorage>();
else
    services.AddSingleton<IFileStorageService, LocalDiskFileStorage>();
```

`storage_path` semantics stay the same (relative path).

---

## 4. Endpoints

All `/attachments/*` endpoints accept the polymorphic parent reference.

### POST /attachments — upload

```
POST /attachments
Content-Type: multipart/form-data

form fields:
  parent_type    string  required (validated against enum)
  parent_id      long    required
  category       string  required (validated against enum)
  description    string  optional
  file           binary  required
```

**Backend logic:**
1. Validate parent_type + category against enum
2. Validate parent_id row exists in target table (resolve table from parent_type)
3. Verify caller has appropriate permission to MUTATE the parent (e.g., uploading to a TI requires `sales.tax_invoice.read` or `.create` per design choice — see §5)
4. Stream upload to `IFileStorageService.SaveAsync`
5. INSERT `sys.attachments` row with `storage_path` returned
6. Return: `{ attachment_id, file_name, mime_type, size_bytes, uploaded_at, storage_path: hidden }`

**Response:** 201 Created with attachment metadata (storage_path NOT exposed to client — only used internally).

### GET /attachments?parent_type=X&parent_id=Y — list

Returns attachments for a parent, ordered by uploaded_at DESC. Soft-deleted excluded.

```json
{
  "items": [
    {
      "attachment_id": 1,
      "category": "TAX_INVOICE",
      "file_name": "AWS-invoice-2026-05.pdf",
      "mime_type": "application/pdf",
      "size_bytes": 154832,
      "uploaded_at": "2026-05-18T10:00:00Z",
      "uploaded_by": { "id": 5, "name": "นาง ส" },
      "description": "AWS monthly cloud bill",
      "page_count": 2
    }
  ]
}
```

### GET /attachments/{id}/download — stream content

Returns binary stream with correct `Content-Type` + `Content-Disposition: attachment; filename="..."`.

**Permission check:** must have read access to the parent entity (same perm as viewing the parent detail page).

### DELETE /attachments/{id} — soft-delete

Marks `deleted_at` + `deleted_by`. File on disk NOT deleted immediately (Phase 2 GC task can clean up after retention period).

**Permission:** `sys.attachment.delete` OR uploader (own attachments).

### GET /attachments/categories — enum reference

Returns list of valid categories (for UI dropdowns + filters).

---

## 5. Permissions

```
sys.attachment.upload    — can upload to any entity caller has access to
sys.attachment.read      — can list + download (granted with parent's .read)
sys.attachment.delete    — can soft-delete any attachment in their tenant
```

Default grants:
- SUPER_ADMIN: all 3
- COMPANY_ADMIN: all 3
- ACCOUNTANT + AP_CLERK + AR_CLERK: upload + read; can delete own uploads (validator)
- AUDITOR + TAX_OFFICER: read only

Granular per-parent permission inheritance: upload to TI requires `sales.tax_invoice.read`; upload to PV requires `purchase.payment_voucher.read`; etc. Application-layer check.

---

## 6. UI

### Detail pages — add "📎 หลักฐาน (N)" section

Every detail page (TI, RC, CN, DN, VI, PV, JV, Q, SO, DO) gets a section near the bottom:

```
📎 หลักฐาน (3)

[+ อัปโหลด]

┌───────────────────────────────────────────────────────────────┐
│ ┃ [TAX_INVOICE] AWS-invoice-2026-05.pdf  154 KB  10:00 น.    │
│ ┃ AWS monthly cloud bill                                     │
│ ┃ อัปโหลดโดย: นาง ส | [ดู] [ดาวน์โหลด] [ลบ]                │
├───────────────────────────────────────────────────────────────┤
│ ┃ [BANK_SLIP] transfer-confirm.jpg  84 KB  10:05 น.         │
│ ┃ ...                                                        │
└───────────────────────────────────────────────────────────────┘
```

Click "+ อัปโหลด" → modal:
- Drag-and-drop or file picker
- Category dropdown (filter to relevant for parent type — e.g. WHT_CERT_50TAWI hidden for Quotation)
- Description (optional)
- Upload button

Inline preview for images + PDF first page (browser-native `<embed>`).

### List pages — attachment count chip

TI/RC/VI/PV list rows show a small "📎 N" chip if attachments exist. Click to filter or hover for tooltip.

### Reports/dashboard — "Recent attachments" widget

Optional widget on dashboard showing last 10 uploads across the tenant. Quick visual of activity. Defer if scope tight.

---

## 7. i18n

```
attachment.title              "หลักฐาน"
attachment.upload             "อัปโหลด"
attachment.category           "ประเภท"
attachment.fileName           "ชื่อไฟล์"
attachment.size               "ขนาด"
attachment.uploadedAt         "อัปโหลดเมื่อ"
attachment.uploadedBy         "อัปโหลดโดย"
attachment.description        "คำอธิบาย"
attachment.download           "ดาวน์โหลด"
attachment.view               "ดู"
attachment.delete             "ลบ"
attachment.deleteConfirm      "ยืนยันลบ? (ไฟล์จะถูก soft-delete สำหรับ audit trail)"
attachment.dropHere           "ลากไฟล์มาวางที่นี่"
attachment.maxSize            "ขนาดสูงสุด: 25 MB"
attachment.allowedTypes       "PDF, JPG, PNG, Excel"
attachment.category.TAX_INVOICE      "ใบกำกับภาษี"
attachment.category.RECEIPT          "ใบเสร็จรับเงิน"
attachment.category.PURCHASE_ORDER   "ใบสั่งซื้อ"
... (per category)
```

---

## 8. Tests

### Unit
- `AttachmentValidatorTests` — file size, mime type, category enum
- `LocalDiskFileStorageTests` — save/read/delete round-trip, path sanitization, dir creation

### Integration
- POST /attachments → row created + file on disk
- POST /attachments to non-existent parent → 404
- POST /attachments without parent's .read perm → 403
- GET /attachments?parent_type=X&parent_id=Y → returns correct list
- GET /attachments/{id}/download → streams correct bytes
- DELETE /attachments/{id} → row soft-deleted (deleted_at set), file still on disk
- Cross-tenant: tenant A cannot list/download tenant B's attachments
- Large file (24MB → ok, 26MB → 413 Payload Too Large)
- Bad mime type (.exe) → 400
- File name with `../` traversal attempt → sanitized
- Concurrent uploads to same parent → no race / duplicate

### e2e Playwright (×1 new)
- `attachment-upload-flow.spec.ts`:
  1. Login as accountant
  2. Navigate to a posted VI detail
  3. Click "+ อัปโหลด"
  4. Upload a test PDF (`fixtures/test-invoice.pdf`)
  5. Verify attachment appears in list with correct metadata
  6. Click download → file downloads
  7. Click delete → confirm → list updates
  8. Soft-delete: re-fetch list shows it's gone, but DB row deleted_at is set

Total: 27 prior + 1 new = **28/28**.

---

## 9. Scope cuts — explicitly OUT

- ❌ **Cloud storage (Azure Blob / S3)** — Phase 2 (interface in place, swap when needed)
- ❌ **Virus scanning** — Phase 2 (when SaaS hosting needs it)
- ❌ **OCR / text extraction** — Phase 2 (for receipt auto-parse)
- ❌ **File versioning** — soft-delete only; new upload = new row (Phase 2 if asked)
- ❌ **In-app PDF annotation** — browser-native preview only
- ❌ **Drag-attach across documents** (e.g., move attachment from one TI to another) — not in scope
- ❌ **Thumbnail generation** — browser handles it
- ❌ **Encryption at rest** — Phase 2 (filesystem-level or TDE later)
- ❌ **Audit-trail of attachment views** — uploads + deletes logged, views NOT (would be noisy)
- ❌ **Hard delete + GC job** — Phase 2 (soft-delete only this sprint)
- ❌ **Quota enforcement per company** — Phase 2 (track size_bytes, no enforcement)

If any block → escalate per §8.

---

## 10. Cross-sprint use cases (all unlocked)

| Sprint | Use case | Category |
|---|---|---|
| 5.5 | Gas station scenario — vendor TI scan attached to VI | TAX_INVOICE |
| 8.6 | Customer 50ทวิ scan attached to Receipt (AR-WHT) | WHT_CERT_50TAWI |
| 8.7 | AWS bill PDF attached to PV/VI | TAX_INVOICE or RECEIPT |
| 8.7 | Employee expense-claim form + receipt original (Option-A reimburse) | EXPENSE_CLAIM_FORM + RECEIPT |
| 12 | Internal PO copy attached to VI (cross-ref) | PURCHASE_ORDER |
| Future | Customs declaration (import) | CUSTOMS_DECL |

---

## 11. Gates

| Gate | Expectation |
|---|---|
| Backend build | 0/0 |
| Domain tests | +N (validators, storage) |
| Api tests | +N (CRUD, cross-tenant, perms, mime/size validation) |
| EF migration | `AddAttachmentSystem` clean |
| tsc / next build | 0 / 0 (no new routes — section added to existing detail pages) |
| Playwright | 27 + 1 = **28/28** |
| Local disk IO | round-trip test, path traversal blocked |

---

## 12. DoD (single phase)

1. `sys.attachments` entity + EF config + migration
2. parent_type + category enums + validators
3. `IFileStorageService` interface + `LocalDiskFileStorage` impl
4. `IAttachmentService` (upload, list, get, delete, download stream)
5. Endpoints: POST, GET (list + by-id + download), DELETE
6. Permissions: 3 new perms + grants + parent-level inheritance check
7. UI section on 10 detail pages (TI/RC/CN/DN/VI/PV/JV/Q/SO/DO) — uniform component
8. Attachment count chip on relevant list pages
9. i18n th + en
10. Tests (unit + integration + 1 e2e)
11. All gates green
12. Mirror sync `Y:\AccountApp`
13. plan.md §23.3 — strike Sprint 11
14. `Report-Backend16.md`

**Total: 14 DoD items.**

---

## 13. After this sprint

Next: **Sprint 12 — Internal Purchase Order** (Sana will write spec when 11 is in flight). With Sprint 11's attachment system shipped, Sprint 12 PO can attach the printed-out PO PDF to itself for archive (use-case alignment).

---

**Build it. ~3-5 days. Single phase. Report back via Report-Backend16.**
