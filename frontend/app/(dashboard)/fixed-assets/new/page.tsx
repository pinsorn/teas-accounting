'use client';

import { FixedAssetForm } from '@/components/forms/FixedAssetForm';

// Cycle D (specs/fixed-assets.md §9.13) acquire form, extracted to
// FixedAssetForm.tsx for L3-12 (specs/fix-r2-u8-fe.md) so the Draft-only
// edit door ([id]/edit/page.tsx) can reuse it.
export default function NewFixedAssetPage() {
  return <FixedAssetForm />;
}
