'use client';

import { use } from 'react';
import { useRouter } from 'next/navigation';
import { useTranslations } from 'next-intl';
import { FixedAssetForm } from '@/components/forms/FixedAssetForm';
import { useFixedAsset } from '@/lib/queries';

// L3-12 (specs/fix-r2-u8-fe.md) — Draft-only edit route, following the
// expense-claims/[id]/edit sibling pattern exactly: fetch the detail,
// enforce the editable status (mirrors the API's own UpdateDraftAsync
// refusal — backend/src/Accounting.Infrastructure/FixedAsset/FixedAssetService.cs:151-157),
// reuse the shared form.
export default function FixedAssetEditPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = use(params);
  const assetId = Number(id);
  const tc = useTranslations('common');
  const router = useRouter();
  const q = useFixedAsset(assetId);
  const d = q.data;

  if (!d) return <div className="p-6 text-base-content/50">{tc('loading')}</div>;

  if (d.status !== 'Draft') {
    router.replace(`/fixed-assets/${assetId}`);
    return <div className="p-6 text-base-content/50">{tc('loading')}</div>;
  }

  return <FixedAssetForm edit={d} />;
}
