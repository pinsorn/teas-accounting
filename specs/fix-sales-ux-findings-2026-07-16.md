# Fix spec — sales-side UX findings (2026-07-16, from PROGRESS-sales-uxtest.md)

Status: FIX ROUND APPROVED by Ham 2026-07-16 ("แก้ทุก finding เลย"). S6 excluded
(by-design, manual done). Work packages:
- WP-A (backend, sonnet): S4 (3 DTOs+3 projections), S9 (server-side BU enforcement
  on QT create/send when company requires BU + MCP tool parity), S14 (investigate
  due-date default vs customer credit term; fix if clearly wrong), S12-BE (activity
  entries for draft-edit + ensure send/accept/post transitions logged once, no
  "X → X" duplicates from backend).
- WP-B (frontend flow, sonnet): S11 (confirm dialogs QT send/accept + SO post + INV
  issue, reuse WP3.6 pattern), S12-FE (refs/activity panels refetch after actions +
  i18n wording), S15 (edit for SO/INV drafts per WP3.3 PO-edit pattern), S16 (RC
  prefill BU from upstream invoice), S10 (show BU on QT/SO/INV/RC detail headers).
- WP-C (frontend polish, sonnet, AFTER WP-B — same files): S1 (hydration skeleton +
  vatMode-gated cards/columns never flash), S2 (breadcrumb i18n), S3 (status filter
  Thai labels sweep), S5+S7 (BE-hint + locale on QT form dates + ALL list date
  filters, reuse WP4.1), S8 (customer picker modal → inline "สร้างลูกค้าใหม่" link,
  minimal: new-tab + refetch-on-focus).
- S13 (infra, investigation FIRST — no fix without evidence): prod log pull around
  2026-07-16 05:30–06:10 ICT for the 503-but-applied writes; nginx error/access +
  Kestrel/systemd logs; report root cause hypothesis + minimal fix proposal to Fable.
  Any prod config change needs Ham sign-off.

## S13 — intermittent 503 on prod writes that still APPLY [~] INVESTIGATED 2026-07-16
Investigation (read-only, full report in session transcript): actual event window =
~13:02–13:12 ICT (not 05:3x — earlier estimate was turn-clock skew). Topology:
Cloudflare (orange-cloud) → NPM docker OpenResty (proxy_host 13 → 172.17.0.1:3100)
→ teas-web Next.js BFF → Kestrel :5180. Origin is CLEAN: NPM access log has ZERO 503
ever for this vhost; teas-web/teas-api logs silent/no errors; the exact write sequence
appears at origin ONCE each with 200/204. Conclusion: first attempts died at the
**Cloudflare edge** (origin never saw them), browser showed 503, FE retry reached
origin and applied → "503-but-applied".
- [ ] HAM ACTION: pull Cloudflare dashboard Analytics/Logs 5xx for teas.kazaki-rio.com
      2026-07-16 13:02–13:12 ICT to confirm edge-side cause (health check/PoP/rule).
- [x] S13a (FE hardening, ship regardless): app/api/proxy/[...path]/route.ts fetch has
      NO timeout → add AbortSignal.timeout(~30s) + distinct "not confirmed, retry"
      error surface on abort. DONE 2026-07-16 (WP-B) — added `signal:
      AbortSignal.timeout(30_000)` to the upstream fetch; catch distinguishes
      `e.name === 'TimeoutError'` → 504 `{title:'gateway.timeout', detail:'ยังไม่ยืนยันผล
      — ลองใหม่'}` from any other failure → existing 502 `gateway.error`. Classification
      extracted to `lib/proxy-error.ts` (pure, testable without mocking next/headers) +
      `lib/proxy-error.test.ts` (3 tests, green). No other route.ts behavior changed.
- [x] S13b (backend, Fable-descoped from key-infra): VERIFY + TEST that the
      number-issuing/posting transition endpoints (quotations send/accept,
      sales-orders post, billing-notes issue, receipts post) are idempotent under a
      duplicate call — second identical request must NOT issue a second number /
      second JE (safe no-op or 409, either acceptable; assert current behavior with
      tests). Full Idempotency-Key infra deferred until Cloudflare logs justify it
      (would need a schema migration — YAGNI on current evidence).
      DONE 2026-07-16 (WP-A) — VERIFIED SAFE, no code fix needed. All 5 transitions
      guard Draft-only status BEFORE allocating a doc number (Quotation.SendAsync/
      AcceptAsync, SalesOrder.PostAsync, BillingNote.IssueAsync) except Receipt.PostAsync,
      whose guard lives in the entity method `Receipt.MarkPosted` and runs AFTER
      `_numbers.NextAsync`; traced this is still safe because `NumberSequenceService`
      runs its UPSERT on the caller's AMBIENT transaction (`cmd.Transaction =
      _db.Database.CurrentTransaction?.GetDbTransaction()`), and `ReceiptService.PostAsync`
      wraps the whole method in one `tx` that only commits at the very end — so a
      `MarkPosted` throw on a duplicate call rolls back the number allocation too (no
      stranded/leaked number). 5 new tests drive the REAL transition twice (not a seeded
      target status) and assert: 2nd call throws, DocNo/status unchanged, exactly 1
      activity-log entry (Quotation send/accept, SO post, BN issue) or exactly 1 JE by
      `Reference` (Receipt post) — `SalesUxFixesWpATests.cs`.
- [ ] Separate note for Ham (unrelated but real): NPM certbot hourly renewals for
      npm-1/9/11/23 are failing on Let's Encrypt rate limits (not teas's cert).
- Do NOT touch nginx timeouts/buffering — zero evidence pointing there.

## S11 — no confirm dialog on QT ส่ง (issues doc number!), QT ตอบรับ, SO post, INV ออก [x]
Only RC post has the WP3.6-style dialog. Add parity dialogs (totals + consequence
text) at minimum on number-issuing/immutable hops: QT send, SO post, INV issue.
DONE 2026-07-16 (WP-B) — added `ConfirmActionDialog` (WP3.6 pattern, reused as-is, no
component changes) to: QT send/accept/reject (quotations/[id]/page.tsx — reject
upgraded from the plain `useConfirm()` text dialog to the totals-bearing dialog for
parity), SO post (sales-orders/[id]/page.tsx), INV issue + mark-settled
(invoices/[id]/page.tsx, mark-settled was dialog-less per scope note). Each dialog
shows customer + VAT/total rows and a Thai consequence line (new `confirmAction.*`
i18n keys in th.json/en.json: qtSend, qtAccept, qtReject, soPost, bnIssue,
bnMarkSettled). RC detail-page post button left as-is (out of the named S11 scope;
RC create-flow already has PostConfirmDialog). tsc clean.
RUNTIME VERIFIED 2026-07-16 (local :3000 + :5080, demo-admin/co2, fresh next dev):
QT send dialog renders correctly (title/warning/party/VAT+total rows), confirmed →
doc number issued (01-0001-QT-ECOM-0001), status flipped to Sent live. QT accept
dialog also verified + confirmed → Accepted. SO post dialog verified (title/warning/
party/VAT+total rows) on a fresh test SO → confirmed → doc number issued
(07-2026-SO-ECOM-0002), status Posted. INV issue dialog verified + confirmed → doc
number issued (07-2026-IV-ECOM-0002), status Issued. INV mark-settled dialog
verified (title/warning) then cancelled (no need to complete). Tooling note: the
`computer` tool's coordinate/ref clicks were flaky in this session (silently missed
the target, no error) — confirmed via JS-dispatched `.click()` instead; not a
product bug.

## S12 — side panels stale after actions on sales detail pages (F10-parity) [x]
Refs/activity panels don't refetch after send/accept/post; edit writes no activity
entry; "ส่งแล้ว → ส่งแล้ว" wording redundant (R6-parity).
DONE 2026-07-16 (WP-B, FE half) — mirrored the purchase-side WP4.5 fix: added
`qc.invalidateQueries({queryKey:['doc-chain']})` + `['activity']` (broad, no id) to
`useQuotationAction`, `usePostSalesOrder`, `useBillingNoteAction`, `usePostReceipt`
in lib/queries.ts, so the "เอกสารอ้างอิง"/"ประวัติกิจกรรม" side rails refetch after
send/accept/reject/cancel/post/issue/mark-settled. Redundant "ส่งแล้ว → ส่งแล้ว"
wording: extracted `activityHeadline()` pure helper in ActivityLog.tsx (collapses to
one label when action/toStatus localize the same) + ActivityLog.test.ts (3 tests,
green). "edit writes no activity entry" is S12-BE (WP-A, backend) — not this half.
tsc clean, vitest green.
RUNTIME VERIFIED 2026-07-16 — on every QT/SO/INV action exercised during S11's
verify (send/accept/post/issue), the "ประวัติกิจกรรม" (activity) rail updated LIVE
with no manual reload, e.g. QT send showed a clean "ส่งแล้ว" entry (not "ส่งแล้ว →
ส่งแล้ว"), and QT accept showed a fallback raw "Accepted" (backend action code has
no `common.activityAction.Accepted` Thai key — a separate, pre-existing i18n gap,
NOT the redundant-arrow bug this ticket targets; noting for a possible follow-up,
out of S12-FE's scope). SO edit (see S15) also showed a live "Updated" entry
confirming S12-BE's backend addition works end-to-end too.
S12-BE DONE 2026-07-16 (WP-A) — `QuotationChainServices.cs` `UpdateDraftAsync` wrote
NO activity entry at all before this fix; added `activity.Record("Quotation", ...,
"Updated")` (no fromStatus/toStatus — the doc stays Draft — mirrors the existing
no-status-change convention like `SalesOrder`'s `"CreatedDeliveryOrder"` note-only
entry). Same fix carried into the NEW `SalesOrder.UpdateDraftAsync` (S15 backend
half, see below) for parity. Investigated the "ส่งแล้ว → ส่งแล้ว" duplicate-wording
finding: grepped every `activity.Record` call across `backend/src/.../Sales/*.cs` —
NONE pass an equal fromStatus/toStatus pair (e.g. Quotation Send records
`fromStatus:"Draft", toStatus:"Sent"`, action `"Sent"`). Root cause is FE-side:
`ActivityLog.tsx` renders `${label(action)} → ${label(toStatus)}`, and the action
code `"Sent"` and the status code `"Sent"` happen to translate to the SAME Thai
string ("ส่งแล้ว"), producing the apparent duplicate — the backend DATA is correct,
this is WP-B's rendering fix (already addressed above via `activityHeadline()`). No
backend change made for this half (per spec: "only fix if backend clearly writes a
wrong transition pair" — it doesn't).

## S16 — receipt-from-invoice doesn't prefill BU from upstream invoice [x]
/receipts/new?bn=5 leaves BU "— ต้องระบุ —" though invoice has businessUnitId=3.
DONE 2026-07-16 (WP-B) — receipts/new/page.tsx: added an effect that reads
`bnDetailQueries[0]?.data?.businessUnitId` (the Invoice detail already fetched for
the line-item preview) and calls `setBusinessUnitId` once, only while the user
hasn't picked their own BU yet (`businessUnitId === null` guard). tsc clean.
RUNTIME VERIFIED 2026-07-16 — navigated to /receipts/new?bn=20&customer=5&amount=8560
(bn=20 has businessUnitId=1/ECOM): the หน่วยธุรกิจ selector auto-filled "ECOM —
อีคอมเมิร์ซ" instead of the prior "— ต้องระบุ —", and the invoice reference/amount
prefilled correctly too.

## S15 — converted drafts (SO-from-QT, INV-from-SO) have no แก้ไข [x]
Add edit route/button parity with QT draft (F6-parity), or explicit design ruling.
INV DONE 2026-07-16 (WP-B) — BillingNoteForm.tsx gained the same `edit` prop
QuotationForm already has (isEdit branch: PUT via `useUpdateBillingNote`, re-hydrate
effect, edit-mode header/actions); new route
`app/(dashboard)/invoices/[id]/edit/page.tsx` mirrors quotations/[id]/edit/page.tsx
1:1 (Draft-only, bounces to detail otherwise); "แก้ไข" button added to the Draft
action bar on invoices/[id]/page.tsx. Known limitation (documented, not fixed): the
detail-line snapshot (ChainLineDto) doesn't carry productType, so re-saving an
edited line without re-picking its product defaults it to 'GOOD' — same class of gap
QuotationForm already had for its own fields; flagging for a possible backend DTO
follow-up (ChainLineDto has no ProductType at all — Sales/SalesChainDtos.cs:65-68),
out of FE-only blast radius.
SO backend half DONE 2026-07-16 (WP-A, Fable scope addition after WP-B flagged the
gap above) — was BLOCKED (no backend PUT endpoint existed for sales-orders; confirmed
via the same `grep MapPut` WP-B ran). Added, mirroring Quotation's UpdateDraftAsync
exactly: `ISalesOrderService.UpdateDraftAsync(long id, CreateSalesOrderRequest req,
CancellationToken ct)` (reuses the Create DTO, no new `UpdateSalesOrderRequest` type —
same as Quotation does), implementation in `SalesOrderDeliveryServices.cs`
(Draft-status-only guard `so.cannot_edit_after_post`, DocDate is user-editable/passed
through per §10 Option B — Quotation-parity, NOT re-pinned; drop+rebuild lines;
carries the new S9 BU-required check; S12-BE `activity.Record(..., "Updated")` entry),
`so.MapPut("/{id:long}", ...)` in `SalesChainEndpoints.cs` with the exact same shape
as the Quotation PUT (same `soPol` permission the create route already uses, applied
group-wide — no distinct edit permission exists). 2 new tests: draft update persists +
DocDate reflects the request value (not re-pinned), non-Draft update rejected
(`so.cannot_edit_after_post`) — `SalesUxFixesWpATests.cs`. FE half (SalesOrderForm
edit route/button) remains WP-B's — now unblocked.
SO FE half DONE 2026-07-16 (WP-B, after unblock) — `SalesOrderForm.tsx` gained the
same `edit` prop pattern (PUT via new `useUpdateSalesOrder` in lib/queries.ts, which
reuses `CreateSalesOrderRequest` — matches the backend's own PUT shape, no separate
Update type, same convention as `useUpdatePurchaseOrder`); new route
`app/(dashboard)/sales-orders/[id]/edit/page.tsx` mirrors the QT/INV edit pages;
"แก้ไข" button added to the Draft action bar on sales-orders/[id]/page.tsx.
KNOWN GAP found + mitigated (not fixed — backend DTO, out of FE-only blast radius):
`SalesOrderDetail` (unlike QuotationDetail/BillingNoteDetail) carries NEITHER
`ExpectedDeliveryDate` NOR `Notes` at all (SalesChainDtos.cs:94-102) — the edit form
cannot know the SO's current values for those two fields, and a PUT always fully
replaces them (no partial-patch API), so blindly preloading them empty would SILENTLY
WIPE existing data on every save. Mitigated by leaving both fields empty in edit mode
with an explicit orange "จะแทนที่ค่าเดิม" (will replace, not preserve) hint on each,
so the risk is visible instead of silent. Flagging to Fable: needs
`SalesOrderDetail` extended with these 2 fields for true round-tripping.
INV docDate ADDENDUM 2026-07-16 (WP-B, per Fable's mid-task finding) —
`BillingNoteService.UpdateDraftAsync` unconditionally re-pins DocDate to today
server-side (`bn.DocDate = clock.TodayInBangkok()`, §10/D2 — deliberately NOT in the
PO/QT/SO preserve-on-edit scope), ignoring whatever the request sends. BillingNoteForm's
edit branch now shows this honestly via the PO-edit-fix `DateInput locked` pattern:
`<DateInput value={docDate} locked lockedHint="ล็อกตามกติกา — บันทึกแล้ววันที่เอกสารจะเป็น
วันนี้ (Asia/Bangkok)" .../>` where `docDate` state is seeded/held at `today` (never
the stale `edit.docDate`) — the field never shows a value that would disagree with
what's actually persisted.
tsc clean, vitest green (6 new self-checks total this session: proxy-error.test.ts ×3,
ActivityLog.test.ts ×3).
RUNTIME VERIFIED 2026-07-16 (full round trips, not just page loads):
- SO edit: created a clean test SO (#6, valid company-2 customer — the pre-existing
  seed QT#1→SO#5 chain turned out to reference a customer NOT in company 2, a stale
  dev-DB data artifact unrelated to this change, see below), opened
  /sales-orders/6/edit, changed line qty 1→5, saved → redirected to detail, GET
  confirmed persisted (qty=5, total recalculated ฿5,350.00), activity rail showed a
  live "Updated" entry, BU badge persisted.
- INV edit: created invoice #20, opened /invoices/20/edit, confirmed the docDate
  field renders LOCKED showing today's date with the exact hint text specified,
  changed line qty 2→4, saved → GET confirmed persisted (qty=4, total ฿8,560,
  docDate=today, notes preserved correctly since BillingNoteDetail DOES carry notes).
- Dev-DB note (not a product bug): the pre-existing QT#1→SO#5 seed chain has
  customerId=1 which is absent from company 2's live `/customers` list — PUT
  correctly rejected it with `customer.not_found` (the SAME validation Create already
  applies). Confirms the backend's re-validation-on-edit behavior is working AS
  INTENDED; the seed data itself is stale/cross-company, out of scope to fix here.

## S9 — API allows BU-null drafts while FE requires BU [x]
MCP-created QT #5/#6 had businessUnitId=null. Enforce server-side on create/send
(company requires BU), align MCP tool validation.
DONE 2026-07-16 (WP-A) — reused the existing `Company.RequiresBusinessUnit` +
`bu.required` `DomainException` mechanism already enforced on TaxInvoice/Receipt/
TaxAdjustmentNote (no new config flag). Added the same check to: Quotation
CreateDraftAsync + UpdateDraftAsync (an edit can re-null the BU) + SendAsync (defense
at the numbering gate — catches a draft that was legitimately created null-BU BEFORE
the company opted into BU-required, e.g. the MCP QT #5/#6 case), SalesOrder
CreateDraftAsync (no SO UpdateDraftAsync existed at the time this check went in — see
S15 SO note; it now has the same check too), BillingNote CreateDraftAsync +
UpdateDraftAsync. Receipt already had it — untouched. MCP tool parity: MCP create/send
tools are thin wrappers over these same Application services (per the file header
comment "no posting/business logic lives here" — mcp-document-chain D1), so the
enforcement is automatically shared, no separate MCP-layer change needed. 7 new
`bu.required`/rejection tests (create-without-BU on QT/SO/BillingNote, send-of-a-
pre-existing-null-BU-draft on QT) — `SalesUxFixesWpATests.cs`.

## S10 — QT detail page doesn't display หน่วยธุรกิจ [x]
DONE 2026-07-16 (WP-B) — spec's title says "QT" but scope note covers QT/SO/INV/RC;
implemented on all 4 detail pages via the existing `BusinessUnitBadge` component
(component itself untouched except widening its null-guard: `businessUnitId == null
&& !code` instead of `businessUnitId == null`, so it also renders when a caller has
only a `code`, no numeric id). QT/SO/INV pages pass `businessUnitId={d.businessUnitId}`
directly (detail DTOs carry it). RC (receipts/[id]/page.tsx) is the exception:
`ReceiptDetail` (backend) has ONLY `BusinessUnitCode`, no numeric id at all
(Sales/AdjustmentReadDtos.cs:28-33) — passed `businessUnitId={null} code={d.businessUnitCode}`,
which renders the code without a name (name isn't in that DTO either). tsc clean.
RUNTIME VERIFIED 2026-07-16 — BU badge confirmed live on QT ("หน่วยธุรกิจ: ECOM —
อีคอมเมิร์ซ"), SO, and INV detail pages during the S11/S15 verify passes above. RC
detail page badge NOT independently runtime-checked this session (the receipts/new
save via JS-dispatched form events didn't create the draft as expected — deprioritized
after the 3/4 pages already confirmed the identical code pattern working live); code
is tsc-clean and follows the exact same `<BusinessUnitBadge>` call already proven on
3 pages, so residual risk is low but flagging as the one sub-item without a direct
screenshot.

## S14 — verify invoice due-date default vs customer credit term [x]
DONE 2026-07-16 (WP-A) — confirmed the field exists (`Customer.PaymentTermDays`,
int) and was simply not applied on the two chain-create paths. Fixed
`BillingNoteService.CreateFromDeliveryOrderAsync` + `CreateFromSalesOrderAsync`:
`DueDate = PaymentTermDays > 0 ? DocDate.AddDays(PaymentTermDays) : DocDate` (prior
behavior — DueDate==DocDate — preserved when the customer has no term). The direct
manual-create path (`CreateDraftAsync`, `POST /billing-notes`) takes an explicit
client-supplied DueDate already and was NOT touched — that's a user-chosen value, not
a silently-wrong default. 2 new tests: term=30 (seeded demo customer) applied, term=0
keeps prior DueDate==DocDate behavior — `SalesUxFixesWpATests.cs`.
## S7/S5 — BE hints + date-locale on QT form dates & all list filters (merge into S5) [ ]
## S8 — customer picker modal: add inline "สร้างลูกค้าใหม่" (F4-parity) [ ]

## S4 — BU column "—" on 3 sales list pages (BUG, backend, R8-family) [x]
Root cause (audited, evidence in PROGRESS): list DTO + ListAsync projection omit
BusinessUnitId; entity + Detail DTO + FE all have/expect it.
- [x] QuotationListItem add `int? BusinessUnitId`
      (backend/src/Accounting.Application/Sales/SalesChainDtos.cs:70-75) + projection
      select x.BusinessUnitId (Accounting.Infrastructure/Sales/QuotationChainServices.cs:289-292)
- [x] SalesOrderListItem same (SalesChainDtos.cs:86-88 +
      SalesOrderDeliveryServices.cs:186-188)
- [x] DeliveryOrderListItem same (SalesChainDtos.cs:100-105 +
      SalesOrderDeliveryServices.cs:368-371)
- Pattern to copy: BillingNoteDtos.cs:39 + BillingNoteService.cs:352 (Sprint 13i C3).
- FE: NO change needed (pages already render businessUnitId; v1.21.3 cell fix live).
- Gate: integration test asserting list items carry businessUnitId when set (one per
  endpoint, follow existing BillingNote list test if present); dotnet build + test green.
  DONE — 3 tests (`Quotation_list_item_carries_business_unit_id`,
  `SalesOrder_list_item_carries_business_unit_id`,
  `DeliveryOrder_list_item_carries_business_unit_id`) in `SalesUxFixesWpATests.cs`;
  `dotnet build` 0 errors/0 warnings; full suite green (see Attempt log).
- Deploy: API deploy required (not FE-only). DB backup per deploy SOP (no schema change,
  but SOP mandates backup). NOT DEPLOYED by WP-A — deploy is Fable's/deploy-SOP's call.
- Also verify after fix: หน่วยธุรกิจ FILTER on /quotations /sales-orders /delivery-orders
  actually filters (it operates on the same missing field today). CLARIFICATION: this
  is a CLIENT-SIDE filter (none of Quotation/SalesOrder/DeliveryOrder/BillingNote's
  `ListAsync` take a `businessUnitId` query param server-side — matches the
  BillingNoteListItem comment "for client-side BU/customer filtering on the list
  page"). The FE filters the already-fetched array by `item.businessUnitId`; it was
  silently always-empty only because the field was always null. Not independently
  re-verified in the browser (backend-only worker, no FE/browser access this dispatch)
  — should self-resolve now the field is populated, but flagging for WP-B/Fable to
  confirm live.

## S1 — dashboard first-paint flash (UX minor, FE) [ ]
Pre-hydration paint shows ฿0.00 stat cards + "VAT สุทธิ" card (wrong for non-VAT co) +
empty nav section headers ~1-2s before company context loads.
- [ ] Show skeleton/shimmer (or hide cards) until company + sysInfo loaded; never render
      the vatOnly card before vatMode known; nav sections render only with their items.

## S2 — breadcrumb i18n inconsistency (i18n minor, FE) [ ]
/customers breadcrumb = "แดชบอร์ด > customers" (EN slug) while /quotations shows Thai.
- [ ] Audit breadcrumb source; map all route segments through nav i18n keys (th.json).

## S3 — list status-filter options raw EN enum (i18n minor, FE) [ ]
/quotations สถานะ dropdown shows "Accepted"/"Draft"; table badges are Thai.
- [ ] Localize status options via existing status-label map; sweep ALL sales list pages
      (and purchase pages for parity) for the same dropdown pattern.

## S5 — list date-range filters native mm/dd/yyyy, no BE hint (UX minor, FE) [ ]
WP4.1 added BE hints to form date inputs only; list filters lack them.
- [ ] Reuse the WP4.1 hint component/pattern on list filter date inputs (all list pages).

## Non-fixes (documented for manual instead)
- S6: ใบวางบิล = ใบแจ้งหนี้ (/invoices) by design; ใบกำกับภาษี/CN/DN hidden on non-VAT co
  (vatOnly flag). Manual must explain the chain + non-VAT visibility rule (ม.86/4).

## Attempt log
- 2026-07-16 ~04:0x: spec drafted from Phase 0–2 findings + Explore DTO audit. Test
  paused at prod login (session + MCP token both expired, awaiting Ham).
- 2026-07-16 ~13:3x-14:0x (WP-B worker): implemented S11, S12-FE, S15 (INV; SO
  blocked, see S15 note), S16, S10, plus the S13a addition Fable dispatched
  mid-task. 16 files under frontend/ touched (at the ~16-file cap, not exceeded):
  lib/queries.ts, components/doc/ActivityLog.tsx(+.test.ts), messages/{th,en}.json,
  app/(dashboard)/quotations/[id]/page.tsx, app/(dashboard)/sales-orders/[id]/page.tsx,
  app/(dashboard)/invoices/[id]/page.tsx, app/(dashboard)/invoices/[id]/edit/page.tsx
  (new), components/forms/BillingNoteForm.tsx, components/ui/BusinessUnitBadge.tsx,
  app/(dashboard)/receipts/[id]/page.tsx, app/(dashboard)/receipts/new/page.tsx,
  app/api/proxy/[...path]/route.ts, lib/proxy-error.ts(+.test.ts) (new).
  Gates run: `npx tsc --noEmit` clean; `npx vitest run` 13 files/61 tests green
  (added 2 new test files, 6 new tests, for the two pieces of non-trivial logic —
  activityHeadline collapse + proxy timeout classification). `next lint` skipped —
  no eslint config committed in this repo (interactive setup wizard, pre-existing,
  unrelated to this diff). Runtime verify (S11 dialog on QT send, S16 BU prefill)
  is PENDING per Fable's hold — backend worker (WP-A) was editing backend/src in
  the same tree; did not run dotnet/next dev. Awaiting go-ahead to run the local
  dev-stack browser check.
- 2026-07-16 ~13:3x-14:2x (WP-A worker): implemented S4 (3 DTOs + 3 projections), S9
  (BU-required on QT create/update/send, SO create, BillingNote create/update — reused
  existing `bu.required` mechanism), S14 (BillingNote DueDate now applies
  `Customer.PaymentTermDays` on the two chain-create paths), S12-BE (Quotation
  UpdateDraftAsync now logs an activity entry; confirmed the "ส่งแล้ว → ส่งแล้ว" wording
  is FE-only, backend data is correct — see S12 note above). Mid-task scope additions
  from Fable, both addressed: S13b (verified all 5 number-issuing/posting transitions
  are already safe under a duplicate/retry call — no code fix needed, only tests) and
  S15 backend half (SalesOrder had NO update endpoint at all — added
  `UpdateDraftAsync` + `PUT /sales-orders/{id}`, mirroring Quotation exactly, unblocking
  WP-B's SO-edit FE which was blocked on this). 6 backend files touched (under the
  ~14-file cap): `SalesChainDtos.cs`, `QuotationChainServices.cs`,
  `SalesOrderDeliveryServices.cs`, `BillingNoteService.cs`, `SalesChainEndpoints.cs`,
  + new test file `tests/Accounting.Api.Tests/Sales/SalesUxFixesWpATests.cs` (22 tests).
  Footgun hit: `dotnet build` failed MSB3027 (locked DLLs) from a STALE
  `Accounting.Api.exe` dev-server process left running from an earlier session (not
  `testhost` as the existing troubles-wiki entry describes) — killed the PID, documented
  the variant in troubles-wiki. Gates: `dotnet build` 0 warnings/0 errors; new test file
  22/22 green (0 skipped); RBAC suite (41 tests) green confirming the new SO PUT route
  is correctly permission-mapped; full suite run — see EVIDENCE in the final report.
- 2026-07-16 ~14:3x-15:0x (WP-B worker, resumed after WP-A unblock): implemented the
  SO-edit FE half (SalesOrderForm `edit` prop + `useUpdateSalesOrder` + new
  /sales-orders/[id]/edit route + "แก้ไข" button) and the BillingNoteForm docDate-lock
  addendum Fable flagged mid-task (found via a real backend read: BillingNote
  UpdateDraftAsync re-pins DocDate to today, unlike PO/QT/SO). tsc clean; vitest 13
  files/61 tests green. Then ran the deferred runtime verification: killed the stale
  :3000 (footgun), started backend fresh on :5080 (`dotnet run`,
  `ASPNETCORE_URLS=http://localhost:5080`) and `next dev` fresh on :3000, logged in as
  demo-admin/Demo@1234 (co2, manual-capture persona). Verified live in-browser: S11
  (QT send/accept, SO post, INV issue/mark-settled dialogs — all render + confirm
  correctly, doc numbers issued, statuses flip), S12-FE (activity/refs rails refetch
  live post-action, redundant-arrow wording confirmed gone), S16 (RC BU auto-fills from
  the referenced invoice), S15 (both SO-edit and INV-edit round-trip: line qty edited →
  saved → PUT persisted → GET confirms), S10 (BU badge live on QT/SO/INV; RC only
  code-reviewed, not screenshotted — see S10 note). One dev-DB-only false alarm
  (pre-existing QT#1→SO#5 seed references a customer outside company 2 — correctly
  rejected by the (working-as-designed) tenant-scoped customer lookup on PUT; not a
  code bug, worked around by testing against a freshly created SO instead). Killed both
  local dev-stack processes after verification. Full tsc + vitest re-run green
  immediately before this entry. No git commit made (per instructions) — diff is ready
  for Fable's review.
