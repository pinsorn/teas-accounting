'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Plus, Pencil, AlertTriangle, ShieldAlert } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { QueryState } from '@/components/states/QueryState';
import { problemToast } from '@/lib/api';
import {
  useCompanies, useCompany, useCreateCompany, useUpdateCompany, useMePermissions,
} from '@/lib/queries';
import type {
  CompanyListItem, CreateCompanyRequest, UpdateCompanyRequest,
  LegalEntityType, Pnd30SubmissionMode,
} from '@/lib/types';

// Mirrors BE Accounting.Domain.Enums.LegalEntityType (PascalCase on the wire).
const ENTITY_TYPES: LegalEntityType[] = [
  'LimitedCompany', 'PublicLimitedCompany', 'LimitedPartnership',
  'OrdinaryPartnership', 'JointVenture', 'SoleProprietor', 'Other',
];

// vatRate travels as a FRACTION (0.07); the UI edits a PERCENT (7).
// Round through 1e4 to strip float fuzz (0.07 * 100 = 7.000000000000001).
const toPercent = (rate: number) => Math.round(rate * 10_000) / 100;
const toFraction = (pct: number) => Math.round(pct * 100) / 10_000;

/** Empty string → null (the API treats both as "unset"). */
const nn = (s: string): string | null => (s.trim() ? s.trim() : null);

export default function CompaniesSettingsPage() {
  const t = useTranslations('companies');
  const tc = useTranslations('common');
  const perms = useMePermissions();
  const q = useCompanies();
  const rows = q.data ?? [];

  const [creating, setCreating] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);

  // Super-admin only — BE enforces master.company.manage on /companies; the FE
  // hides the page body entirely (same hidden-not-disabled rule as PermissionGate).
  // Gate on the loading window first so the super-admin UI never flashes before
  // permissions resolve.
  if (perms.isLoading) {
    return (
      <>
        <PageHeader title={t('title')} />
        <div className="py-12 text-center text-base-content/50">{tc('loading')}</div>
      </>
    );
  }
  if (!perms.data?.isSuperAdmin) {
    return (
      <>
        <PageHeader title={t('title')} />
        <div className="flex flex-col items-center gap-2 py-12 text-center" data-testid="state-no-access">
          <ShieldAlert className="h-10 w-10 text-warning" aria-hidden />
          <div className="font-semibold">{t('noAccessTitle')}</div>
          <div className="max-w-md text-sm text-base-content/60">{t('noAccessBody')}</div>
        </div>
      </>
    );
  }

  return (
    <>
      <PageHeader
        title={t('title')}
        subtitle={t('subtitle')}
        actions={
          <button className="btn btn-primary btn-sm gap-1" onClick={() => setCreating(true)}
            data-testid="co-create-btn">
            <Plus className="h-4 w-4" aria-hidden /> {t('create')}
          </button>
        }
      />

      <QueryState query={q} isEmpty={!q.isLoading && rows.length === 0}>
        <div className="overflow-x-auto rounded-lg border border-base-300">
          <table className="table table-zebra">
            <thead>
              <tr>
                <th>ID</th>
                <th>{t('taxId')}</th>
                <th>{t('nameTh')}</th>
                <th>{t('vat')}</th>
                <th className="text-right">{t('vatRate')}</th>
                <th>{t('pnd30Mode')}</th>
                <th>{t('status')}</th>
                <th className="w-16" />
              </tr>
            </thead>
            <tbody>
              {rows.map((c: CompanyListItem) => (
                <tr key={c.companyId} data-testid={`co-row-${c.companyId}`}>
                  <td>{c.companyId}</td>
                  <td className="font-mono">{c.taxId}</td>
                  <td>{c.nameTh}</td>
                  <td>
                    {c.vatRegistered
                      ? <span className="badge badge-success badge-sm">{t('vatRegistered')}</span>
                      : <span className="badge badge-ghost badge-sm">{t('vatNotRegistered')}</span>}
                  </td>
                  <td className="text-right tabular-nums">{toPercent(c.vatRate)}%</td>
                  <td>{c.pnd30SubmissionMode === 'auto' ? t('modeAuto') : t('modeManual')}</td>
                  <td>
                    {c.isActive
                      ? <span className="badge badge-outline badge-sm">{t('active')}</span>
                      : <span className="badge badge-error badge-sm">{t('inactive')}</span>}
                  </td>
                  <td>
                    <button className="btn btn-ghost btn-xs gap-1"
                      onClick={() => setEditingId(c.companyId)}
                      data-testid={`co-edit-${c.companyId}`}>
                      <Pencil className="h-3 w-3" aria-hidden /> {tc('edit')}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </QueryState>

      {creating && <CreateCompanyDialog onClose={() => setCreating(false)} />}
      {editingId != null && (
        <EditCompanyDialog id={editingId} onClose={() => setEditingId(null)} />
      )}
    </>
  );
}

/** §4.6 callout shared by both dialogs' tax sections. */
function TaxWarning() {
  const t = useTranslations('companies');
  return (
    <div role="alert" className="alert alert-warning py-2 text-xs">
      <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden />
      {t('taxWarning')}
    </div>
  );
}

function Field({
  label, value, onChange, type = 'text', required = false, testId,
}: {
  label: string; value: string; onChange: (v: string) => void;
  type?: string; required?: boolean; testId?: string;
}) {
  return (
    <label className="form-control">
      <span className="label-text">{label}{required && <span className="text-error"> *</span>}</span>
      <input className="input input-bordered" type={type} value={value}
        onChange={(e) => onChange(e.target.value)} data-testid={testId} />
    </label>
  );
}

// ───────────────────────────── Create ─────────────────────────────

interface CreateForm {
  taxId: string; nameTh: string; nameEn: string;
  legalEntityType: LegalEntityType; registrationDate: string;
  fiscalYearStartMonth: number;
  vatRegistered: boolean; vatRegisterDate: string; vatRatePct: string;
  pnd30SubmissionMode: Pnd30SubmissionMode;
  addressTh: string; subDistrict: string; district: string; province: string;
  postalCode: string; phone: string; email: string;
}

const EMPTY_CREATE: CreateForm = {
  taxId: '', nameTh: '', nameEn: '',
  legalEntityType: 'LimitedCompany', registrationDate: '',
  fiscalYearStartMonth: 1,
  vatRegistered: true, vatRegisterDate: '', vatRatePct: '7',
  pnd30SubmissionMode: 'manual',
  addressTh: '', subDistrict: '', district: '', province: '',
  postalCode: '', phone: '', email: '',
};

function CreateCompanyDialog({ onClose }: { onClose: () => void }) {
  const t = useTranslations('companies');
  const tc = useTranslations('common');
  const create = useCreateCompany();
  const [f, setF] = useState<CreateForm>(EMPTY_CREATE);
  const set = (patch: Partial<CreateForm>) => setF((cur) => ({ ...cur, ...patch }));

  const pct = Number(f.vatRatePct);
  const pctInvalid = f.vatRatePct.trim() === '' || !Number.isFinite(pct) || pct < 0 || pct > 100;
  // ม.86/4 — province + 5-digit postal are the founding registered address (required server-side
  // so the new company_profile / RD forms have a real address).
  const canSave =
    /^\d{13}$/.test(f.taxId.trim()) &&
    f.nameTh.trim() !== '' &&
    !pctInvalid &&
    f.province.trim() !== '' &&
    /^\d{5}$/.test(f.postalCode.trim());

  async function submit() {
    try {
      const req: CreateCompanyRequest = {
        taxId: f.taxId.trim(),
        nameTh: f.nameTh.trim(),
        nameEn: nn(f.nameEn),
        legalEntityType: f.legalEntityType,
        registrationDate: nn(f.registrationDate),
        vatRegistered: f.vatRegistered,
        vatRegisterDate: nn(f.vatRegisterDate),
        fiscalYearStartMonth: f.fiscalYearStartMonth,
        addressTh: nn(f.addressTh),
        subDistrict: nn(f.subDistrict),
        district: nn(f.district),
        province: nn(f.province),
        postalCode: nn(f.postalCode),
        phone: nn(f.phone),
        email: nn(f.email),
        paidUpCapital: null,
        vatRate: toFraction(pct),
        pnd30SubmissionMode: f.pnd30SubmissionMode,
      };
      await create.mutateAsync(req);
      toast.success(t('created'));
      onClose();
    } catch (e) {
      problemToast(e, tc('error'));
    }
  }

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true">
      <div className="modal-box max-w-2xl">
        <h3 className="text-lg font-bold">{t('createTitle')}</h3>

        <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Field label={t('taxId')} value={f.taxId} required testId="co-new-taxid"
            onChange={(v) => set({ taxId: v.replace(/\D/g, '').slice(0, 13) })} />
          <label className="form-control">
            <span className="label-text">{t('legalEntityType')}</span>
            <select className="select select-bordered" value={f.legalEntityType}
              onChange={(e) => set({ legalEntityType: e.target.value as LegalEntityType })}>
              {ENTITY_TYPES.map((et) => (
                <option key={et} value={et}>{t(`entity.${et}`)}</option>
              ))}
            </select>
          </label>
          <Field label={t('nameTh')} value={f.nameTh} required testId="co-new-nameth"
            onChange={(v) => set({ nameTh: v })} />
          <Field label={t('nameEn')} value={f.nameEn} onChange={(v) => set({ nameEn: v })} />
          <Field label={t('registrationDate')} value={f.registrationDate} type="date"
            onChange={(v) => set({ registrationDate: v })} />
          <label className="form-control">
            <span className="label-text">{t('fiscalYearStartMonth')}</span>
            <select className="select select-bordered" value={f.fiscalYearStartMonth}
              onChange={(e) => set({ fiscalYearStartMonth: Number(e.target.value) })}>
              {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                <option key={m} value={m}>{m}</option>
              ))}
            </select>
          </label>
          <Field label={t('addressTh')} value={f.addressTh} onChange={(v) => set({ addressTh: v })} />
          <Field label={t('subDistrict')} value={f.subDistrict} onChange={(v) => set({ subDistrict: v })} />
          <Field label={t('district')} value={f.district} onChange={(v) => set({ district: v })} />
          <Field label={t('province')} value={f.province} required onChange={(v) => set({ province: v })} />
          <Field label={t('postalCode')} value={f.postalCode} required
            onChange={(v) => set({ postalCode: v.replace(/\D/g, '').slice(0, 5) })} />
          <Field label={t('phone')} value={f.phone} onChange={(v) => set({ phone: v })} />
          <Field label={t('email')} value={f.email} type="email" onChange={(v) => set({ email: v })} />
        </div>

        {/* ── การตั้งค่าภาษี (Tax) ── */}
        <div className="mt-5 rounded-lg border border-warning/40 bg-warning/5 p-4">
          <h4 className="mb-3 font-semibold">{t('taxSection')}</h4>
          <TaxWarning />
          <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
            <label className="label cursor-pointer justify-start gap-3">
              <input type="checkbox" className="toggle toggle-success" checked={f.vatRegistered}
                onChange={(e) => set({ vatRegistered: e.target.checked })}
                data-testid="co-new-vatregistered" />
              <span className="label-text">{t('vatRegistered')}</span>
            </label>
            <Field label={t('vatRatePercent')} value={f.vatRatePct} type="number"
              testId="co-new-vatrate" onChange={(v) => set({ vatRatePct: v })} />
            <Field label={t('vatRegisterDate')} value={f.vatRegisterDate} type="date"
              onChange={(v) => set({ vatRegisterDate: v })} />
            <label className="form-control">
              <span className="label-text">{t('pnd30Mode')}</span>
              <select className="select select-bordered" value={f.pnd30SubmissionMode}
                onChange={(e) => set({ pnd30SubmissionMode: e.target.value as Pnd30SubmissionMode })}>
                <option value="manual">{t('modeManual')}</option>
                <option value="auto">{t('modeAuto')}</option>
              </select>
            </label>
          </div>
          {pctInvalid && <p className="mt-1 text-xs text-error">{t('vatRateInvalid')}</p>}
        </div>

        <div className="modal-action">
          <button className="btn btn-ghost" onClick={onClose}>{tc('cancel')}</button>
          <button className="btn btn-primary" disabled={!canSave || create.isPending}
            onClick={submit} data-testid="co-new-save">
            {create.isPending && <span className="loading loading-spinner loading-sm" />}
            {tc('save')}
          </button>
        </div>
      </div>
      <button className="modal-backdrop" aria-label={tc('close')} onClick={onClose} />
    </div>
  );
}

// ───────────────────────────── Edit ─────────────────────────────

interface EditForm {
  nameTh: string; nameEn: string;
  addressTh: string; subDistrict: string; district: string; province: string;
  postalCode: string; phone: string; email: string;
  isActive: boolean; paidUpCapital: string;
  vatRegistered: boolean; vatRegisterDate: string; vatRatePct: string;
  pnd30SubmissionMode: Pnd30SubmissionMode;
}

function EditCompanyDialog({ id, onClose }: { id: number; onClose: () => void }) {
  const t = useTranslations('companies');
  const tc = useTranslations('common');
  const q = useCompany(id);
  const save = useUpdateCompany(id);
  const [f, setF] = useState<EditForm | null>(null);
  const set = (patch: Partial<EditForm>) => setF((cur) => (cur ? { ...cur, ...patch } : cur));

  // PUT is a whole-row replace — prefill EVERY field from GET /companies/{id},
  // otherwise saving would blank the address (see CompanyDetail doc comment).
  const d = q.data;
  useEffect(() => {
    if (!d) return;
    setF({
      nameTh: d.nameTh, nameEn: d.nameEn ?? '',
      addressTh: d.addressTh ?? '', subDistrict: d.subDistrict ?? '',
      district: d.district ?? '', province: d.province ?? '',
      postalCode: d.postalCode ?? '', phone: d.phone ?? '', email: d.email ?? '',
      isActive: d.isActive,
      paidUpCapital: d.paidUpCapital != null ? String(d.paidUpCapital) : '',
      vatRegistered: d.vatRegistered, vatRegisterDate: d.vatRegisterDate ?? '',
      vatRatePct: String(toPercent(d.vatRate)),
      pnd30SubmissionMode: d.pnd30SubmissionMode,
    });
  }, [d]);

  const pct = f ? Number(f.vatRatePct) : NaN;
  const pctInvalid = !f || f.vatRatePct.trim() === ''
    || !Number.isFinite(pct) || pct < 0 || pct > 100;
  const capital = f && f.paidUpCapital.trim() !== '' ? Number(f.paidUpCapital) : null;
  const capitalInvalid = capital !== null && (!Number.isFinite(capital) || capital < 0);
  const canSave = !!f && f.nameTh.trim() !== '' && !pctInvalid && !capitalInvalid;

  async function submit() {
    if (!f) return;
    try {
      const req: UpdateCompanyRequest = {
        nameTh: f.nameTh.trim(),
        nameEn: nn(f.nameEn),
        vatRegistered: f.vatRegistered,
        vatRegisterDate: nn(f.vatRegisterDate),
        addressTh: nn(f.addressTh),
        subDistrict: nn(f.subDistrict),
        district: nn(f.district),
        province: nn(f.province),
        postalCode: nn(f.postalCode),
        phone: nn(f.phone),
        email: nn(f.email),
        isActive: f.isActive,
        paidUpCapital: capital,
        vatRate: toFraction(pct),
        pnd30SubmissionMode: f.pnd30SubmissionMode,
      };
      await save.mutateAsync(req);
      toast.success(t('saved'));
      onClose();
    } catch (e) {
      problemToast(e, tc('error'));
    }
  }

  return (
    <div className="modal modal-open" role="dialog" aria-modal="true">
      <div className="modal-box max-w-2xl">
        <h3 className="text-lg font-bold">
          {t('editTitle')}{d ? ` — ${d.nameTh}` : ''}
        </h3>

        {(q.isLoading || !f) && !q.isError && (
          <div className="py-8 text-center text-base-content/50">{tc('loading')}</div>
        )}
        {q.isError && <div className="py-8 text-center text-error">{tc('error')}</div>}

        {f && d && (
          <>
            <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Field label={t('nameTh')} value={f.nameTh} required testId="co-edit-nameth"
                onChange={(v) => set({ nameTh: v })} />
              <Field label={t('nameEn')} value={f.nameEn} onChange={(v) => set({ nameEn: v })} />
              <Field label={t('addressTh')} value={f.addressTh} onChange={(v) => set({ addressTh: v })} />
              <Field label={t('subDistrict')} value={f.subDistrict} onChange={(v) => set({ subDistrict: v })} />
              <Field label={t('district')} value={f.district} onChange={(v) => set({ district: v })} />
              <Field label={t('province')} value={f.province} onChange={(v) => set({ province: v })} />
              <Field label={t('postalCode')} value={f.postalCode} onChange={(v) => set({ postalCode: v })} />
              <Field label={t('phone')} value={f.phone} onChange={(v) => set({ phone: v })} />
              <Field label={t('email')} value={f.email} type="email" onChange={(v) => set({ email: v })} />
              <Field label={t('paidUpCapital')} value={f.paidUpCapital} type="number"
                onChange={(v) => set({ paidUpCapital: v })} />
              <label className="label cursor-pointer justify-start gap-3">
                <input type="checkbox" className="toggle" checked={f.isActive}
                  onChange={(e) => set({ isActive: e.target.checked })} />
                <span className="label-text">{t('active')}</span>
              </label>
            </div>

            {/* ── การตั้งค่าภาษี (Tax) — §4.6: audit-logged, effective immediately ── */}
            <div className="mt-5 rounded-lg border border-warning/40 bg-warning/5 p-4">
              <h4 className="mb-3 font-semibold">{t('taxSection')}</h4>
              <TaxWarning />
              <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
                <label className="label cursor-pointer justify-start gap-3">
                  <input type="checkbox" className="toggle toggle-success" checked={f.vatRegistered}
                    onChange={(e) => set({ vatRegistered: e.target.checked })}
                    data-testid="co-edit-vatregistered" />
                  <span className="label-text">{t('vatRegistered')}</span>
                </label>
                <Field label={t('vatRatePercent')} value={f.vatRatePct} type="number"
                  testId="co-edit-vatrate" onChange={(v) => set({ vatRatePct: v })} />
                <Field label={t('vatRegisterDate')} value={f.vatRegisterDate} type="date"
                  onChange={(v) => set({ vatRegisterDate: v })} />
                <label className="form-control">
                  <span className="label-text">{t('pnd30Mode')}</span>
                  <select className="select select-bordered" value={f.pnd30SubmissionMode}
                    onChange={(e) => set({ pnd30SubmissionMode: e.target.value as Pnd30SubmissionMode })}>
                    <option value="manual">{t('modeManual')}</option>
                    <option value="auto">{t('modeAuto')}</option>
                  </select>
                </label>
              </div>
              {pctInvalid && <p className="mt-1 text-xs text-error">{t('vatRateInvalid')}</p>}
            </div>
          </>
        )}

        <div className="modal-action">
          <button className="btn btn-ghost" onClick={onClose}>{tc('cancel')}</button>
          <button className="btn btn-primary" disabled={!canSave || save.isPending}
            onClick={submit} data-testid="co-edit-save">
            {save.isPending && <span className="loading loading-spinner loading-sm" />}
            {tc('save')}
          </button>
        </div>
      </div>
      <button className="modal-backdrop" aria-label={tc('close')} onClick={onClose} />
    </div>
  );
}
