'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Lock, AlertTriangle, Pencil } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import {
  useCompanyProfile, useUpdateCompanyProfileSoft, useUploadCompanyLogo, useUpdateCompanyInfo,
} from '@/lib/queries';
import { apiGet, apiPut } from '@/lib/api';
import type {
  CompanyDto, CompanyProfile, LegalEntityType, UpdateCompanyProfileSoftRequest,
  UpdateCompanyRequest, UpdateCompanyInfoRequest,
} from '@/lib/types';

type SoftForm = UpdateCompanyProfileSoftRequest;

const EMPTY_SOFT: SoftForm = {
  tradeName: '', logoUrl: '', phone: '', email: '', website: '',
  contactName: '', bankName: '', bankAccountNo: '', bankAccountName: '',
  ssoEmployerAccountNo: '',
};

const LEGAL_TYPES: LegalEntityType[] = [
  'LimitedCompany', 'PublicLimitedCompany', 'LimitedPartnership',
  'OrdinaryPartnership', 'JointVenture', 'SoleProprietor', 'Other',
];

export default function CompanyProfilePage() {
  const t = useTranslations('companyProfile');
  const tc = useTranslations('common');
  const q = useCompanyProfile();
  const save = useUpdateCompanyProfileSoft();
  const upload = useUploadCompanyLogo();
  const [form, setForm] = useState<SoftForm>(EMPTY_SOFT);

  const p = q.data;

  useEffect(() => {
    if (!p) return;
    setForm({
      tradeName: p.tradeName ?? '', logoUrl: p.logoUrl ?? '',
      phone: p.phone ?? '', email: p.email ?? '', website: p.website ?? '',
      contactName: p.contactName ?? '', bankName: p.bankName ?? '',
      bankAccountNo: p.bankAccountNo ?? '', bankAccountName: p.bankAccountName ?? '',
      ssoEmployerAccountNo: p.ssoEmployerAccountNo ?? '',
    });
  }, [p]);

  async function onUploadLogo(file: File) {
    try {
      const res = await upload.mutateAsync(file);
      setForm((f) => ({ ...f, logoUrl: res.logoUrl }));
      toast.success(t('saved'));
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  async function onSave() {
    try {
      // normalise empty strings → null (the API treats both as "unset"). logoUrl is kept as-is
      // (managed by the upload button) so saving the soft section never wipes an uploaded logo.
      const payload = Object.fromEntries(
        Object.entries(form).map(([k, v]) => [k, v?.trim() ? v.trim() : null]),
      ) as unknown as SoftForm;
      await save.mutateAsync(payload);
      toast.success(t('saved'));
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  function HardField({ label, value }: { label: string; value: string | null }) {
    return (
      <label className="form-control">
        <span className="label-text flex items-center gap-1">
          <Lock className="h-3 w-3 text-base-content/50" aria-hidden />
          {label}
        </span>
        <input
          className="input input-bordered input-disabled"
          value={value ?? '—'}
          disabled
          readOnly
          title={t('hardLockedTooltip')}
          data-testid={`cp-hard-${label}`}
        />
      </label>
    );
  }

  function SoftField({
    k, label, type = 'text',
  }: { k: keyof SoftForm; label: string; type?: string }) {
    return (
      <label className="form-control">
        <span className="label-text">{label}</span>
        <input
          className="input input-bordered"
          type={type}
          value={form[k] ?? ''}
          onChange={(e) => setForm({ ...form, [k]: e.target.value })}
          data-testid={`cp-soft-${k}`}
        />
      </label>
    );
  }

  return (
    <>
      <PageHeader title={t('title')} />

      <div role="alert" className="alert alert-warning mb-4 text-sm">
        <AlertTriangle className="h-4 w-4" aria-hidden />
        {t('banner')}
      </div>

      {q.isLoading && (
        <div className="py-8 text-center text-base-content/50">{tc('loading')}</div>
      )}
      {q.isError && (
        <div className="py-8 text-center text-error">{t('loadError')}</div>
      )}

      {p && (
        <div className="space-y-6">
          {/* ── Legal identity (super-admin may edit the whole company) ── */}
          <section className="card bg-base-100 shadow-sm">
            <div className="card-body">
              <div className="flex items-center justify-between">
                <h2 className="card-title flex items-center gap-2 text-base">
                  <Lock className="h-4 w-4" aria-hidden /> {t('legalSection')}
                </h2>
                <PermissionGate scope="master.company.manage">
                  <EditCompanyInfo profile={p} />
                </PermissionGate>
              </div>
              <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                <HardField label={t('legalName')} value={p.legalName} />
                <HardField label={t('taxId')} value={p.taxId} />
                <HardField label={t('registrationNumber')} value={p.registrationNumber} />
                <HardField label={t('branchCode')} value={p.branchCode} />
                <HardField label={t('registeredAddress')} value={p.registeredAddressLine1} />
                <HardField label={t('addressLine2')} value={p.registeredAddressLine2} />
                {/* Structured registered address (RD forms — ภ.ง.ด.1 ฯลฯ). */}
                <HardField label={t('building')} value={p.regBuilding} />
                <HardField label={t('roomNo')} value={p.regRoomNo} />
                <HardField label={t('floor')} value={p.regFloor} />
                <HardField label={t('village')} value={p.regVillage} />
                <HardField label={t('houseNo')} value={p.regHouseNo} />
                <HardField label={t('moo')} value={p.regMoo} />
                <HardField label={t('soi')} value={p.regSoi} />
                <HardField label={t('street')} value={p.regStreet} />
                <HardField label={t('subdistrict')} value={p.registeredSubdistrict} />
                <HardField label={t('district')} value={p.registeredDistrict} />
                <HardField label={t('province')} value={p.registeredProvince} />
                <HardField label={t('postalCode')} value={p.registeredPostalCode} />
                <HardField label={t('vatRegistrationDate')} value={p.vatRegistrationDate} />
              </div>
            </div>
          </section>

          {/* ── SOFT (admin-editable) ── */}
          <section className="card bg-base-100 shadow-sm">
            <div className="card-body">
              <h2 className="card-title text-base">{t('softSection')}</h2>
              <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
                <SoftField k="tradeName" label={t('tradeName')} />
                <SoftField k="contactName" label={t('contactName')} />
                <SoftField k="phone" label={t('phone')} />
                <SoftField k="email" label={t('email')} type="email" />
                <SoftField k="website" label={t('website')} />
                <SoftField k="bankName" label={t('bankName')} />
                <SoftField k="bankAccountNo" label={t('bankAccountNo')} />
                <SoftField k="bankAccountName" label={t('bankAccountName')} />
                <SoftField k="ssoEmployerAccountNo" label={t('ssoEmployerAccountNo')} />
              </div>

              {/* Sprint 13h P10 — logo upload (multipart). 1 MB max, png/jpeg/svg/webp.
                  Writes attachment + LogoUrl back. (The free-text Logo URL field was removed —
                  upload is the single source so a pasted URL can't break document rendering.) */}
              <PermissionGate scope="master.company_profile.manage">
                <div className="mt-2 flex items-end gap-3">
                  <label className="form-control">
                    <span className="label-text">{t('logoUpload')}</span>
                    <input
                      type="file"
                      className="file-input file-input-bordered file-input-sm"
                      accept="image/png,image/jpeg,image/svg+xml,image/webp"
                      data-testid="cp-logo-upload"
                      disabled={upload.isPending}
                      onChange={(e) => {
                        const f = e.target.files?.[0];
                        if (f) void onUploadLogo(f);
                        e.target.value = '';
                      }}
                    />
                  </label>
                  {upload.isPending && (
                    <span className="loading loading-spinner loading-sm" />
                  )}
                </div>
              </PermissionGate>

              {form.logoUrl?.trim() && (
                <div className="mt-2">
                  <span className="label-text">{t('logoPreview')}</span>
                  {/* external/admin-provided URL — plain img is intentional */}
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img
                    src={form.logoUrl.startsWith('/attachments/')
                      ? `/api/proxy${form.logoUrl}`
                      : form.logoUrl}
                    alt={t('logoPreview')}
                    className="mt-1 h-16 rounded border border-base-300 bg-base-200 object-contain p-1"
                  />
                </div>
              )}

              <PermissionGate scope="master.company_profile.manage">
                <div className="card-actions mt-4 justify-end">
                  <button
                    className="btn btn-primary"
                    onClick={onSave}
                    disabled={save.isPending}
                    data-testid="cp-soft-save"
                  >
                    {save.isPending && (
                      <span className="loading loading-spinner loading-sm" />
                    )}
                    {tc('save')}
                  </button>
                </div>
              </PermissionGate>
            </div>
          </section>

          {/* ── Phase C-C: paid-up capital (CIT SME classification, super-admin) ── */}
          <PermissionGate scope="master.company.manage">
            <PaidUpCapitalCard profile={p} />
          </PermissionGate>
        </div>
      )}
    </>
  );
}

type InfoForm = Omit<UpdateCompanyInfoRequest, 'vatRegistered' | 'vatRate'> & {
  vatRegistered: boolean;
  vatRate: string; // kept as a string in the input, parsed on submit
};

/**
 * Super-admin full company-info editor. Replaces the deferred "edit registered address" path: a
 * fresh install commonly has a data-entry mistake in the founding legal identity. Edits BOTH the
 * master companies row (VAT/tax config — §4.6) and the company profile (what documents render) via
 * PUT /company-profile/company-info. §4.2-SAFE — posted tax invoices snapshot the supplier identity
 * at post-time, so this only changes FUTURE documents. A big confirm warning gates the write.
 */
function EditCompanyInfo({ profile }: { profile: CompanyProfile }) {
  const t = useTranslations('companyProfile');
  const tc = useTranslations('common');
  const mutate = useUpdateCompanyInfo();
  const [open, setOpen] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [f, setF] = useState<InfoForm | null>(null);
  const set = (patch: Partial<InfoForm>) => setF((s) => (s ? { ...s, ...patch } : s));

  async function openEditor() {
    // The legal-form + VAT config live on the master `companies` row, not the profile.
    let company: CompanyDto | null = null;
    try {
      const list = await apiGet<CompanyDto[]>('companies');
      company = list.find((c) => c.companyId === profile.companyId) ?? null;
    } catch {
      toast.error(tc('error'));
      return;
    }
    setF({
      legalName: profile.legalName ?? '',
      nameEn: company?.nameEn ?? null,
      taxId: profile.taxId ?? '',
      registrationNumber: profile.registrationNumber ?? null,
      legalEntityType: company?.legalEntityType ?? 'LimitedCompany',
      branchCode: profile.branchCode ?? '00000',
      vatRegistered: company?.vatRegistered ?? false,
      vatRate: company?.vatRate != null ? String(company.vatRate) : '0.07',
      pnd30SubmissionMode: company?.pnd30SubmissionMode ?? 'manual',
      vatRegisterDate: profile.vatRegistrationDate ?? null,
      building: profile.regBuilding ?? null, roomNo: profile.regRoomNo ?? null,
      floor: profile.regFloor ?? null, village: profile.regVillage ?? null,
      houseNo: profile.regHouseNo ?? null, moo: profile.regMoo ?? null,
      soi: profile.regSoi ?? null, street: profile.regStreet ?? null,
      subdistrict: profile.registeredSubdistrict ?? null, district: profile.registeredDistrict ?? null,
      province: profile.registeredProvince ?? '', postalCode: profile.registeredPostalCode ?? '',
    });
    setOpen(true);
  }

  const valid = !!f
    && f.legalName.trim().length > 0
    && /^\d{13}$/.test(f.taxId.trim())
    && /^\d{5}$/.test(f.branchCode.trim())
    && f.province.trim().length > 0
    && /^\d{5}$/.test(f.postalCode.trim());

  async function commit() {
    if (!f) return;
    const norm = (v: string | null) => (typeof v === 'string' && v.trim() === '' ? null : v);
    const payload: UpdateCompanyInfoRequest = {
      legalName: f.legalName.trim(),
      nameEn: norm(f.nameEn),
      taxId: f.taxId.trim(),
      registrationNumber: norm(f.registrationNumber),
      legalEntityType: f.legalEntityType,
      branchCode: f.branchCode.trim(),
      vatRegistered: f.vatRegistered,
      vatRate: f.vatRegistered ? Number(f.vatRate) || 0 : 0,
      pnd30SubmissionMode: f.pnd30SubmissionMode,
      vatRegisterDate: f.vatRegistered ? norm(f.vatRegisterDate) : null,
      building: norm(f.building), roomNo: norm(f.roomNo), floor: norm(f.floor), village: norm(f.village),
      houseNo: norm(f.houseNo), moo: norm(f.moo), soi: norm(f.soi), street: norm(f.street),
      subdistrict: norm(f.subdistrict), district: norm(f.district),
      province: f.province.trim(), postalCode: f.postalCode.trim(),
    };
    try {
      await mutate.mutateAsync(payload);
      toast.success(t('saved'));
      setConfirming(false); setOpen(false); setF(null);
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

  const Text = ({ k, label, required }: { k: keyof InfoForm; label: string; required?: boolean }) => (
    <label className="form-control">
      <span className="label-text">{label}{required && ' *'}</span>
      <input className="input input-bordered" value={(f?.[k] as string) ?? ''}
        onChange={(e) => set({ [k]: e.target.value } as Partial<InfoForm>)} />
    </label>
  );

  return (
    <>
      <button data-testid="cp-edit-company-info" className="btn btn-ghost btn-xs gap-1" onClick={openEditor}>
        <Pencil className="h-3 w-3" aria-hidden /> {t('editCompanyInfo')}
      </button>

      {/* ── Full edit form ── */}
      {open && f && !confirming && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-box max-w-3xl">
            <h3 className="text-lg font-bold">{t('editCompanyInfo')}</h3>

            <h4 className="mt-4 text-sm font-semibold text-base-content/70">{t('sectionIdentity')}</h4>
            <div className="mt-1 grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Text k="legalName" label={t('legalName')} required />
              <Text k="nameEn" label={t('nameEn')} />
              <Text k="taxId" label={t('taxId')} required />
              <Text k="registrationNumber" label={t('registrationNumber')} />
              <label className="form-control">
                <span className="label-text">{t('legalEntityType')}</span>
                <select className="select select-bordered" value={f.legalEntityType}
                  onChange={(e) => set({ legalEntityType: e.target.value as LegalEntityType })}>
                  {LEGAL_TYPES.map((lt) => <option key={lt} value={lt}>{t(`legalType.${lt}`)}</option>)}
                </select>
              </label>
              <Text k="branchCode" label={t('branchCode')} required />
            </div>

            <h4 className="mt-4 text-sm font-semibold text-base-content/70">{t('sectionVat')}</h4>
            <div className="mt-1 grid grid-cols-1 gap-3 sm:grid-cols-2">
              <label className="form-control">
                <span className="label-text">{t('vatRegistered')}</span>
                <input type="checkbox" className="toggle toggle-primary mt-2" checked={f.vatRegistered}
                  onChange={(e) => set({ vatRegistered: e.target.checked })} />
              </label>
              {f.vatRegistered && (
                <>
                  <label className="form-control">
                    <span className="label-text">{t('vatRate')}</span>
                    <input type="number" step="0.01" min="0" max="1" className="input input-bordered"
                      value={f.vatRate} onChange={(e) => set({ vatRate: e.target.value })} />
                  </label>
                  <label className="form-control">
                    <span className="label-text">{t('vatRegisterDate')}</span>
                    <input type="date" className="input input-bordered" value={f.vatRegisterDate ?? ''}
                      onChange={(e) => set({ vatRegisterDate: e.target.value })} />
                  </label>
                </>
              )}
              <label className="form-control">
                <span className="label-text">{t('pnd30SubmissionMode')}</span>
                <select className="select select-bordered" value={f.pnd30SubmissionMode}
                  onChange={(e) => set({ pnd30SubmissionMode: e.target.value as 'manual' | 'auto' })}>
                  <option value="manual">manual</option>
                  <option value="auto">auto</option>
                </select>
              </label>
            </div>

            <h4 className="mt-4 text-sm font-semibold text-base-content/70">{t('sectionAddress')}</h4>
            <div className="mt-1 grid grid-cols-1 gap-3 sm:grid-cols-3">
              <Text k="building" label={t('building')} />
              <Text k="roomNo" label={t('roomNo')} />
              <Text k="floor" label={t('floor')} />
              <Text k="village" label={t('village')} />
              <Text k="houseNo" label={t('houseNo')} />
              <Text k="moo" label={t('moo')} />
              <Text k="soi" label={t('soi')} />
              <Text k="street" label={t('street')} />
              <Text k="subdistrict" label={t('subdistrict')} />
              <Text k="district" label={t('district')} />
              <Text k="province" label={t('province')} required />
              <Text k="postalCode" label={t('postalCode')} required />
            </div>

            <div className="modal-action">
              <button className="btn btn-ghost" onClick={() => { setOpen(false); setF(null); }}>{tc('cancel')}</button>
              <button className="btn btn-primary" disabled={!valid} onClick={() => setConfirming(true)}>{tc('save')}</button>
            </div>
          </div>
          <button className="modal-backdrop" aria-label={tc('close')} onClick={() => { setOpen(false); setF(null); }} />
        </div>
      )}

      {/* ── BIG warning before committing a founding-identity change ── */}
      {open && f && confirming && (
        <div className="modal modal-open" role="dialog" aria-modal="true">
          <div className="modal-box border-4 border-error">
            <div className="flex flex-col items-center text-center">
              <AlertTriangle className="h-16 w-16 text-error" aria-hidden />
              <h3 className="mt-2 text-2xl font-extrabold uppercase text-error">{t('ciWarnTitle')}</h3>
            </div>
            <p className="mt-3 whitespace-pre-line text-sm font-medium leading-relaxed">{t('ciWarnBody')}</p>
            <div className="modal-action">
              <button className="btn btn-ghost" onClick={() => setConfirming(false)}>{tc('cancel')}</button>
              <button className="btn btn-error" disabled={mutate.isPending} onClick={commit}>
                {mutate.isPending && <span className="loading loading-spinner loading-sm" />}
                {t('ciWarnConfirm')}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

/**
 * Phase C-C — Company.PaidUpCapital lives on the master `companies` table (NOT
 * company_profiles), so it is read via GET /companies and saved via the
 * super-admin full-overwrite PUT /companies/{id}.
 */
function PaidUpCapitalCard({ profile }: { profile: CompanyProfile }) {
  const t = useTranslations('companyProfile');
  const tc = useTranslations('common');
  const [row, setRow] = useState<CompanyDto | null>(null);
  const [value, setValue] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let alive = true;
    apiGet<CompanyDto[]>('companies')
      .then((list) => {
        if (!alive) return;
        const c = list.find((x) => x.companyId === profile.companyId) ?? null;
        setRow(c);
        setValue(c?.paidUpCapital != null ? String(c.paidUpCapital) : '');
      })
      .catch(() => { /* no master.company.manage on BE or load failure → keep hidden */ });
    return () => { alive = false; };
  }, [profile.companyId]);

  if (!row) return null;

  const parsed = value.trim() === '' ? null : Number(value);
  const invalid = parsed !== null && (!Number.isFinite(parsed) || parsed < 0);

  async function onSave() {
    if (!row || invalid) return;
    setSaving(true);
    try {
      const payload: UpdateCompanyRequest = {
        nameTh: row.nameTh,
        nameEn: row.nameEn,
        vatRegistered: row.vatRegistered,
        vatRegisterDate: profile.vatRegistrationDate,
        addressTh: profile.registeredAddressLine1,
        subDistrict: profile.registeredSubdistrict,
        district: profile.registeredDistrict,
        province: profile.registeredProvince,
        postalCode: profile.registeredPostalCode,
        phone: profile.phone,
        email: profile.email,
        isActive: row.isActive,
        paidUpCapital: parsed,
        vatRate: row.vatRate,
        pnd30SubmissionMode: row.pnd30SubmissionMode,
      };
      await apiPut<unknown>(`companies/${row.companyId}`, payload);
      setRow({ ...row, paidUpCapital: parsed });
      toast.success(t('paidUpCapitalSaved'));
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className="card bg-base-100 shadow-sm">
      <div className="card-body">
        <h2 className="card-title text-base">{t('paidUpCapital')}</h2>
        <div className="flex flex-wrap items-end gap-3">
          <label className="form-control">
            <span className="label-text">{t('paidUpCapital')}</span>
            <input
              type="number"
              min={0}
              className="input input-bordered w-56"
              value={value}
              onChange={(e) => setValue(e.target.value)}
              data-testid="cp-paid-up-capital"
            />
          </label>
          <button
            className="btn btn-primary"
            onClick={onSave}
            disabled={saving || invalid}
            data-testid="cp-paid-up-capital-save"
          >
            {saving && <span className="loading loading-spinner loading-sm" />}
            {tc('save')}
          </button>
        </div>
        <p className="text-xs text-base-content/60">{t('paidUpCapitalHelp')}</p>
      </div>
    </section>
  );
}
