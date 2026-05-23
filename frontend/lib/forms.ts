// Sprint 13i B4 (SR6/SR9) — shared form-validation feedback.
//
// React Hook Form's handleSubmit silently aborts when validation fails unless an
// onInvalid callback is supplied. Forms across the app omitted it, so an empty
// submit did nothing visible. This helper standardises: toast + scroll-to-first
// error. Pair it with `aria-invalid` / `data-field-error` markers on inputs so
// the scroll has a target and screen readers announce the field.

/** Scroll the first invalid field into view and focus it (if focusable). */
export function scrollToFirstError(): void {
  if (typeof document === 'undefined') return;
  const el = document.querySelector<HTMLElement>(
    '[aria-invalid="true"], [data-field-error="true"]',
  );
  if (!el) return;
  el.scrollIntoView({ behavior: 'smooth', block: 'center' });
  // Focus the field itself, or the first focusable descendant (e.g. a combobox).
  const focusTarget =
    el.matches('input, select, textarea, button')
      ? el
      : el.querySelector<HTMLElement>('input, select, textarea, button');
  focusTarget?.focus?.({ preventScroll: true });
}

/**
 * Build an onInvalid handler for RHF's handleSubmit(onValid, onInvalid).
 * `toastError` is the caller's translated toast.error fn; `message` defaults to
 * the toast.validationFailed string the caller passes in.
 */
export function onInvalidSubmit(toastError: (msg: string) => void, message: string) {
  return () => {
    toastError(message);
    // Defer so RHF has applied aria-invalid before we query the DOM.
    requestAnimationFrame(scrollToFirstError);
  };
}
