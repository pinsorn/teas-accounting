'use client';

import { useState } from 'react';
import { useTranslations } from 'next-intl';
import { toast } from 'sonner';
import { useCreateVendor } from '@/lib/queries';
import { errorToToast } from '@/lib/api/errors';
import type { VendorType } from '@/lib/types';

// WP3 3.8 — inline vendor quick-create, rendered inside EntityPickerModal so a
// PO/VI/PV vendor picker never has to leave the doc form. Minimal fields only
// (code / Thai name / type / VAT toggle) — everything else can be filled in
// later on the full vendor edit page. Posts to the existing vendor create
// endpoint (useCreateVendor) then hands the new id+label back to the caller.
export function VendorQuickCreateForm({
  onCreated,
}: {
  onCreated: (id: number, label: string) => void;
}) {
  const t = useTranslations('party.quickCreate');
  const tc = useTranslations('common');
  const create = useCreateVendor();
  const [vendorCode, setVendorCode] = useState('');
  const [nameTh, setNameTh] = useState('');
  const [vendorType, setVendorType] = useState<VendorType>('Corporate');
  const [vatRegistered, setVatRegistered] = useState(false);

  const canSave = vendorCode.trim() !== '' && nameTh.trim() !== '' && !create.isPending;

  async function save() {
    try {
      const res = await create.mutateAsync({
        vendorCode: vendorCode.trim(),
        vendorType,
        nameTh: nameTh.trim(),
        nameEn: null,
        taxId: null,
        branchCode: null,
        branchName: null,
        vatRegistered,
        address: null,
        contactPerson: null,
        phone: null,
        email: null,
        paymentTermDays: 30,
        defaultCurrency: 'THB',
        defaultWhtTypeCode: null,
      });
      toast.success(tc('save'));
      onCreated(res.vendor_id, nameTh.trim());
    } catch (e) {
      toast.error(errorToToast(e));
    }
  }

  return (
    <div className="space-y-3 border-t border-ink-100 p-4">
      <label className="form-control" htmlFor="qc-vendor-code">
        <span className="label-text">{t('code')} *</span>
        <input
          id="qc-vendor-code"
          className="input input-bordered input-sm"
          value={vendorCode}
          onChange={(e) => setVendorCode(e.target.value)}
        />
      </label>
      <label className="form-control" htmlFor="qc-vendor-name-th">
        <span className="label-text">{t('nameTh')} *</span>
        <input
          id="qc-vendor-name-th"
          className="input input-bordered input-sm"
          value={nameTh}
          onChange={(e) => setNameTh(e.target.value)}
        />
      </label>
      <label className="form-control" htmlFor="qc-vendor-type">
        <span className="label-text">{t('type')}</span>
        <select
          id="qc-vendor-type"
          className="select select-bordered select-sm"
          value={vendorType}
          onChange={(e) => setVendorType(e.target.value as VendorType)}
        >
          <option value="Corporate">{t('corporate')}</option>
          <option value="Individual">{t('individual')}</option>
        </select>
      </label>
      <label className="label cursor-pointer justify-start gap-3">
        <input
          type="checkbox"
          className="checkbox checkbox-sm"
          checked={vatRegistered}
          onChange={(e) => setVatRegistered(e.target.checked)}
        />
        <span className="label-text">{t('vatRegistered')}</span>
      </label>
      <div className="flex justify-end">
        <button type="button" className="btn btn-primary btn-sm" disabled={!canSave} onClick={save}>
          {tc('save')}
        </button>
      </div>
    </div>
  );
}
