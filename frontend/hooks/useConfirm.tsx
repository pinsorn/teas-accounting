'use client';

import {
  createContext,
  useCallback,
  useContext,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { useTranslations } from 'next-intl';
import { AlertDialog } from '@/components/ui/AlertDialog';

// Sprint 13d P1 — promise-based confirm. Replaces window.confirm():
//   const confirm = useConfirm();
//   if (!(await confirm({ description: t('deactivateConfirm'),
//                          variant: 'destructive' }))) return;
export interface ConfirmOptions {
  title?: string;
  description: ReactNode;
  confirmText?: string;
  cancelText?: string;
  variant?: 'default' | 'destructive';
}

type ConfirmFn = (opts: ConfirmOptions) => Promise<boolean>;

const ConfirmContext = createContext<ConfirmFn | null>(null);

interface State {
  open: boolean;
  opts: ConfirmOptions | null;
}

export function ConfirmProvider({ children }: { children: ReactNode }) {
  const t = useTranslations('common');
  const [state, setState] = useState<State>({ open: false, opts: null });
  const resolverRef = useRef<((v: boolean) => void) | null>(null);

  const confirm = useCallback<ConfirmFn>((opts) => {
    return new Promise<boolean>((resolve) => {
      resolverRef.current = resolve;
      setState({ open: true, opts });
    });
  }, []);

  const settle = useCallback((value: boolean) => {
    resolverRef.current?.(value);
    resolverRef.current = null;
    setState({ open: false, opts: null });
  }, []);

  const opts = state.opts;

  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      {opts && (
        <AlertDialog
          open={state.open}
          title={opts.title ?? t('confirmTitle')}
          description={opts.description}
          confirmText={opts.confirmText ?? t('confirm')}
          cancelText={opts.cancelText ?? t('cancel')}
          variant={opts.variant ?? 'default'}
          onConfirm={() => settle(true)}
          onCancel={() => settle(false)}
        />
      )}
    </ConfirmContext.Provider>
  );
}

export function useConfirm(): ConfirmFn {
  const ctx = useContext(ConfirmContext);
  if (!ctx) {
    throw new Error('useConfirm must be used within <ConfirmProvider>');
  }
  return ctx;
}
