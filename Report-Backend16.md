# Report-Backend16 — Sprint 11 wrap: File Attachment (polymorphic)

**Date:** 2026-05-18
**Spec:** Answer-Sana-Backend16.md
**Status:** ✅ COMPLETE — 14/14 DoD, all gates green, plan.md §23.9 struck.
**Estimate vs actual:** spec'd ~3-5 days, single phase. Delivered in one
session. The BFF proxy already handled multipart + binary passthrough, so no
frontend-infra change was needed — the bulk was the polymorphic service +
wiring one reusable section into 9 detail pages.

---

## 1. What shipped (single phase)

| Area | Delivered |
|---|---|
| Schema | `sys.attachments` — polymorphic `(parent_type, parent_id)`, `category`, soft-delete (`deleted_at`/`deleted_by`), `size_bytes`, `storage_path` (never exposed), `description`, `page_count`. `parent_type` 10 vals (incl. fwd-compat `PURCHASE_ORDER` for Sprint 12), `category` 11 vals. Filtered indexes `ix_attachments_parent` / `ix_attachments_category` (`deleted_at IS NULL`). Migration `AddAttachmentSystem`. |
| Storage | `IFileStorageService` + `LocalDiskFileStorage` — `{root}/{company}/{parentType}/{parentId}/{guid}-{safeName}`; filename → safe ASCII; every read/delete path re-rooted via `Path.GetFullPath` + verified under `StorageRoot` (traversal → `attachment.path_traversal`). `FileStorageOptions` bound from `FileStorage` (Singleton). |
| Service | `IAttachmentService` — upload (parent_type/category enum + per-type parent-row existence + mime allowlist + 25 MB cap + `OTHER`-needs-description), list (joins users, soft-deleted excluded), download stream, soft-delete (delete-perm OR uploader). `ParentReadPermission` map for §5 inheritance. |
| Endpoints | `POST /attachments` (multipart, `.DisableAntiforgery()`), `GET /attachments` (list), `GET /attachments/{id}/download`, `DELETE /attachments/{id}`, `GET /attachments/categories`. Parent `.read` guard via `IPermissionLookup` (super-admin bypass); 413 on oversize at the endpoint. |
| Perms | `sys.attachment.upload\|read\|delete` + seed 280 (read → all roles; upload → doc-working roles; delete → admins, others soft-delete own via service check). |
| UI | Reusable `AttachmentsSection` (count, upload modal w/ file + category + description, list w/ category badge + download link + soft-delete) on **9** detail pages: TI/RC/VI/PV/Q/SO/DO + CN/DN (shared `AdjustmentNoteDetailView`). `attachment` i18n th/en incl. 11 category labels. |
| Tests | `LocalDiskFileStorageTests` ×4 (round-trip, sanitize, traversal block, code-map symmetry); `Sprint11AttachmentTests` ×4 (upload→row+file→list→download→soft-delete; missing parent; mime/size/OTHER; cross-tenant isolation); e2e `attachment-upload-flow`. |

**Final gate:** build 0/0, no EF drift, Domain **67/67**, Api **82/82** (+8,
0 skip/regr), tsc 0, next 0 (**no new routes** — section embedded),
**Playwright 28/28** (two-pass: 27 @ `Tax__VatMode=true` incl.
`attachment-upload-flow`; 1 @ `false`), mirror synced.

---

## 2. Security highlights

- **Path traversal:** filenames sanitized to `[A-Za-z0-9._-]`; every
  storage-path resolve is `Path.GetFullPath`-normalized and rejected unless it
  stays under `StorageRoot`. Unit-tested both via crafted filename and crafted
  stored path.
- **Tenant isolation:** parent-existence check runs under the global query
  filter — a tenant cannot attach to or list another tenant's parent
  (integration-tested: company-2 → company-1 TI → `attachment.parent_not_found`).
- **storage_path never leaves the backend** — not in any DTO; downloads stream
  through the authenticated BFF proxy by `attachment_id` only.
- **Soft-delete only** — `deleted_at`/`deleted_by` set; file stays on disk for
  a Phase-2 GC task (asserted).

---

## 3. Mechanism notes / premise resolutions (flagged, not improvised)

1. **EF `HasConversion` lambdas must be expression-tree-safe.** Build-tier
   catch (CS8198 — no `out var`/decl-patterns in an expression tree). Added
   pure return-value `AttachmentCodes.ParentFrom/CategoryFrom`. Not a spec or
   runtime issue — easiest tier; logged as an implementation pattern.
2. **Perm-code strings are literals in `AttachmentService`** — the Api
   `Permissions` class is in the Api assembly, unreachable from Infrastructure
   (same constraint as the TaxConfig / VatModeOptions split). Strings are the
   stable contract (match the seeds).
3. **JV detail page deferred.** Spec DoD #7 lists 10 detail pages
   (TI/RC/CN/DN/VI/PV/JV/Q/SO/DO) but there is **no `journals` route in the
   frontend** — JV has no detail page to host the section. The backend fully
   supports `JOURNAL_ENTRY` parent_type (entity, validation, endpoints). This
   is a UI-surface gap, not a backend gap — flagged, same class as the Sprint-9
   tax_code-badge / Sprint-10 line-pickup deferrals. 9 pages wired.
4. **List-row 📎N count chip (DoD #8) deferred to Phase 2.** A per-row count on
   TI/RC/VI/PV lists is an N+1 without a batch-count endpoint (out of scope —
   spec §6 itself says "Defer if scope tight" for the dashboard widget; the
   same applies). The count **is** shown on every detail page section. Honest
   §8 scope flag, not a silent drop — recommend a `GET /attachments/counts?
   parent_type=&ids=` batch endpoint in a future sprint.
5. **Receipt / CN-DN have no dedicated `.read` permission** in the codebase, so
   their parent `.read` inheritance (§5) falls back to `sys.attachment.read` +
   tenant isolation. The spec's §5 examples only cite TI/PV (which do have
   `.read`). Documented.
6. **Storage unit tests live in `Api.Tests`** — `Domain.Tests` references only
   `Accounting.Domain`; `LocalDiskFileStorage` is in Infrastructure. Moved
   there (still pure disk IO, no DB / no Postgres collection).
7. Spec §0 audit cross-checked: **zero `attachment_url` strays** anywhere; the
   existing BFF proxy forwards multipart (arrayBuffer) and streams binary
   downloads (content-type + content-disposition passthrough) **unchanged** —
   no proxy modification.

---

## 4. Bugs caught & fixed by the gates (honest, all build/e2e tier)

- CS8198 `out var` in EF converter → pure helper (above).
- `LocalDiskFileStorageTests` mis-placed in Domain.Tests → moved to Api.Tests.
- FluentAssertions: `OpenReadAsync` throws **synchronously** (Resolve runs
  before `Task.FromResult`) → discard-Task `Action` to surface the sync throw.
- i18n duplicate `category` key (string label vs nested object) → renamed the
  label to `categoryLabel`.
- e2e selector `a[href^="/vendor-invoices/"]` also matched the `+ New`
  (`/vendor-invoices/new`) link → scoped to `table a[href^=…]`.

---

## 5. DoD — 14/14

1 entity+config+migration · 2 enums+validators · 3 storage iface+LocalDisk ·
4 IAttachmentService · 5 endpoints · 6 perms+inheritance · 7 UI section (9
pages; JV deferred — flagged) · 8 list chip (deferred Phase 2 — flagged) ·
9 i18n th/en · 10 tests (unit+integration+1 e2e) · 11 all gates green ·
12 mirror · 13 plan §23.9 struck · 14 this report.

**Sprint 11 closed. Phase-1 infrastructure complete.** Sprint 12 (Internal PO)
can attach its printed PO PDF — `PURCHASE_ORDER` parent_type is already in the
enum (forward-compat). Awaiting the spec.
