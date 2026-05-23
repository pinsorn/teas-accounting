'use client';

import { useEffect, useRef, type ReactNode } from 'react';
import { AlertTriangle } from 'lucide-react';

// Sprint 13d P1 — DOM-based confirm dialog replacing native window.confirm().
// DaisyUI modal (matches PostConfirmDialog) so Playwright / Chrome MCP can
// drive it (native dialog is not a DOM element → blocks automation), and the
// copy is i18n/brand-controlled (no "localhost:3000 says" prefix).
export interface AlertDialogProps {
  open: boolean;
  title: string;
  description: ReactNode;
  confirmText: string;
  cancelText: string;
  variant?: 'default' | 'destructive';
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

export function AlertDialog({
  open,
  title,
  description,
  confirmText,
  cancelText,
  variant = 'default',
  busy = false,
  onConfirm,
  onCancel,
}: AlertDialogProps) {
  const confirmRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    confirmRef.current?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && !busy) onCancel();
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [open, busy, onCancel]);

  if (!open) return null;

  const confirmClass =
    variant === 'destructive' ? 'btn btn-error' : 'btn btn-primary';

  return (
    <div
      className="modal modal-open"
      role="alertdialog"
      aria-modal="true"
      aria-labelledby="alert-dialog-title"
      data-testid="alert-dialog"
    >
      <div className="modal-box">
        <h3
          id="alert-dialog-title"
          className={`flex items-center gap-2 text-lg font-bold ${
            variant === 'destructive' ? 'text-error' : ''
          }`}
        >
          {variant === 'destructive' && (
            <AlertTriangle className="h-5 w-5" aria-hidden />
          )}
          {title}
        </h3>

        <div className="mt-3 text-sm text-base-content/80">{description}</div>

        <div className="modal-action">
          <button
            type="button"
            className="btn btn-ghost"
            onClick={onCancel}
            disabled={busy}
            data-testid="alert-dialog-cancel"
          >
            {cancelText}
          </button>
          <button
            ref={confirmRef}
            type="button"
            className={confirmClass}
            onClick={onConfirm}
            disabled={busy}
            data-testid="alert-dialog-confirm"
          >
            {busy && <span className="loading loading-spinner loading-sm" />}
            {confirmText}
          </button>
        </div>
      </div>
      <button
        type="button"
        className="modal-backdrop"
        onClick={() => !busy && onCancel()}
        aria-label={cancelText}
      />
    </div>
  );
}
