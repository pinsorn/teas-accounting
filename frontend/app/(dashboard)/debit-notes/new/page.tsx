'use client';
import { Suspense } from 'react';
import { AdjustmentNoteForm } from '@/components/forms/AdjustmentNoteForm';
export default function Page() {
  return (
    <Suspense fallback={null}>
      <AdjustmentNoteForm noteType="Debit" />
    </Suspense>
  );
}
