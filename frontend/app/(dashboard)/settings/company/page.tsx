'use client';

import { useEffect, useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { Lock, AlertTriangle, Pencil } from 'lucide-react';
import { PageHeader } from '@/components/ui/PageHeader';
import { PermissionGate } from '@/components/PermissionGate';
import {
  useCompanyProfile, useUpdateCompanyProfileSoft, useUploadCompanyLogo, useUpdateRegisteredAddress,
} from '@/lib/queries';
import { apiGet, apiPut } from '@/lib/api';
import type {
  CompanyDto, CompanyProfile, UpdateCompanyProfileSoftRequest,
  UpdateCompanyRequest, UpdateRegisteredAddressRequest,
} from '@/lib/types';

type SoftForm = UpdateCompanyProfileSoftRequest;

const EMPTY_SOFT: SoftForm = {
  tradeName: '', logoUrl: '', phone: '', email: '', website: '',
  contactName: '', bankName: '', bankAccountNo: '', bankAccountName: '',
  ssoEmployerAccountNo: '',
};

export default function CompanyProfilePage() {
  const t = useTranslations('companyProfile');
  const tc = useTranslations('common');
  const q = useCompanyProfile();
  const save = useUpdateCompanyProfileSoft();
  const upload = useUploadCompanyLogo();
  const saveAddr = useUpdateRegisteredAddress();
  const [form, setForm] = useState<SoftForm>(EMPTY_SOFT);
  const [addr, setAddr] = useState<UpdateRegisteredAddressRequest | null>(null);
  const [confirming, setConfirming] = useState(false);

  const p = q.data;

  function openAddr() {
    if (!p) return;
    setAddr({
      building: p.regBuilding ?? '', roomNo: p.regRoomNo ?? '', floor: p.regFloor ?? '',
      village: p.regVillage ?? '', houseNo: p.regHouseNo ?? '', moo: p.regMoo ?? '',
      soi: p.regSoi ?? '', street: p.regStreet ?? '',
      subdistrict: p.registeredSubdistrict ?? '', district: p.registeredDistrict ?? '',
      province: p.registeredProvince ?? '', postalCode: p.registeredPostalCode ?? '',
    });
  }
  const setA = (patch: Partial<UpdateRegisteredAddressRequest>) =>
    setAddr((a) => (a ? { ...a, ...patch } : a));

  async function commitAddr() {
    if (!addr) return;
    try {
      const payload = Object.fromEntries(
        Object.entries(addr).map(([k, v]) => [k, typeof v === 'string' && v.trim() === '' ? null : v]),
      ) as unknown as UpdateRegisteredAddressRequest;
      await saveAddr.mutateAsync(payload);
      toast.success(t('saved'));
      setConfirming(false); setAddr(null);
    } catch (e) {
      toast.error((e as { detail?: string })?.detail ?? tc('error'));
    }
  }

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
      // normalise empty strings → null (the API treats both as "unset").
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
          {/* ── HARD (read-only, Phase 1) ── */}
          <section className="card bg-base-100 shadow-sm">
            <div className="card-body">
              <div className="flex items-center justify-between">
                <h2 className="card-title flex items-center gap-2 text-base">
                  <Lock className="h-4 w-4" aria-hidden /> {t('legalSection')}
                </h2>
                <PermissionGate scope="master.company_profile.manage">
                  <button data-testid="cp-edit-address" className="btn btn-ghost btn-xs gap-1" onClick={openAddr}>
                    <Pencil className="h-3 w-3" aria-hidden /> {t('editRegisteredAddress')}
                  </button>
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

          {/* ── Edit registered address (HARD) modal ── */}
          {addr && !confirming && (
            <div className="modal modal-open" role="dialog" aria-modal="true">
              <div className="modal-box max-w-2xl">
                <h3 className="text-lg font-bold">{t('editRegisteredAddress')}</h3>
                <div className="mt-4 grid grid-cols-1 gap-3 sm:grid-cols-3">
                  {([
                    ['building', t('building')], ['roomNo', t('roomNo')], ['floor', t('floor')],
                    ['village', t('village')], ['houseNo', t('houseNo')], ['moo', t('moo')],
                    ['soi', t('soi')], ['street', t('street')], ['subdistrict', t('subdistrict')],
                    ['district', t('district')], ['province', t('province')], ['postalCode', t('postalCode')],
                  ] as [keyof UpdateRegisteredAddressRequest, string][]).map(([k, label]) => (
                    <label className="form-control" key={k}>
                      <span className="label-text">{label}</span>
                      <input className="input input-bordered" value={addr[k] ?? ''}
                        onChange={(e) => setA({ [k]: e.target.value })} />
                    </label>
                  ))}
                </div>
                <div className="modal-action">
                  <button className="btn btn-ghost" onClick={() => setAddr(null)}>{tc('cancel')}</button>
                  <button className="btn btn-primary"
                    disabled={!addr.province.trim() || !addr.postalCode.trim()}
                    onClick={() => setConfirming(true)}>{tc('save')}</button>
                </div>
              </div>
              <button className="modal-backdrop" aria-label="close" onClick={() => setAddr(null)} />
            </div>
          )}

          {/* ── DBD/ภ.พ.09 warning before committing a HARD address change ── */}
          {addr && confirming && (
            <div className="modal modal-open" role="dialog" aria-modal="true">
              <div className="modal-box border-2 border-warning">
                <h3 className="flex items-center gap-2 text-lg font-bold text-warning">
                  <AlertTriangle className="h-6 w-6" aria-hidden /> {t('addrWarnTitle')}
                </h3>
                <p className="mt-3 whitespace-pre-line text-sm leading-relaxed">{t('addrWarnBody')}</p>
                <div className="modal-action">
                  <button className="btn btn-ghost" onClick={() => setConfirming(false)}>{tc('cancel')}</button>
                  <button className="btn btn-warning" disabled={saveAddr.isPending} onClick={commitAddr}>
                    {t('addrWarnConfirm')}
                  </button>
                </div>
              </div>
            </div>
          )}

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
                <SoftField k="logoUrl" label={t('logoUrl')} />
                <SoftField k="bankName" label={t('bankName')} />
                <SoftField k="bankAccountNo" label={t('bankAccountNo')} />
                <SoftField k="bankAccountName" label={t('bankAccountName')} />
                <SoftField k="ssoEmployerAccountNo" label={t('ssoEmployerAccountNo')} />
              </div>

              {/* Sprint 13h P10 — logo upload (multipart). 1 MB max,
                  png/jpeg/svg/webp. Writes attachment + LogoUrl back. */}
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

/**
 * Phase C-C — Company.PaidUpCapital lives on the master `companies` table (NOT
 * company_profiles), so it is read via GET /companies and saved via the
 * super-admin full-overwrite PUT /companies/{id}. CompanyDto omits the address/
 * phone/email/vatRegisterDate columns, so the PUT body reconstructs them from
 * the company-profile (registered address Line1 is kept in sync with the
 * structured parts; nothing renders from companies.address_th — documents use
 * company_profiles). Hidden entirely when GET /companies is forbidden (403).
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
        // Per-company VAT mode — PUT is a full-row replace; echo the current tax
        // config or the BE would reset it to defaults (0.07 / manual).
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
