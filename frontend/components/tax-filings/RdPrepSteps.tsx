'use client';

import { useTranslations } from 'next-intl';

/** RD Prep download link (identical TH/EN — kept here so it can't drift across JSON files). */
const RD_PREP_URL = 'https://efiling.rd.go.th/rd-cms/';

/**
 * Compact, collapsible "what to do AFTER you download the .txt" panel for RD bulk e-filing.
 * The downloaded .txt is the "Format กลาง" data file for the RD Prep program — NOT the final
 * upload file. The always-visible <summary> carries that correction; the steps are the detail.
 * Reuses DaisyUI `collapse` (native <details>, no JS state). Strings live in messages/{th,en}.json
 * under the shared `rdPrep` namespace.
 */
export function RdPrepSteps({
  formLabel,
  showPnd3Note = false,
}: {
  /** e.g. "ภ.ง.ด.3", "ภ.ง.ด.53", "ภ.พ.30" — read naturally into the intro line. */
  formLabel: string;
  /** ภ.ง.ด.3 only: remind users to fill blank payee addresses in RD Prep before validating. */
  showPnd3Note?: boolean;
}) {
  const t = useTranslations('rdPrep');
  const steps = t.raw('steps') as string[];

  return (
    <details
      data-testid="rdprep-steps"
      className="collapse collapse-arrow mb-4 border border-warning/40 bg-warning/5"
    >
      <summary className="collapse-title min-h-0 py-3 text-sm font-medium text-warning">
        {t('summary')}
      </summary>
      <div className="collapse-content text-sm text-base-content/80">
        <p className="mb-2">{t('intro', { form: formLabel })}</p>
        <ol className="list-decimal space-y-1 pl-5">
          {steps.map((s, i) => (
            <li key={i}>{s}</li>
          ))}
        </ol>
        <p className="mt-2">
          {t('downloadLinkLabel')}{' '}
          <a
            href={RD_PREP_URL}
            target="_blank"
            rel="noopener noreferrer"
            className="link link-primary break-all"
          >
            {RD_PREP_URL}
          </a>
        </p>
        {showPnd3Note && (
          <p className="mt-2 text-xs text-base-content/70">{t('pnd3Note')}</p>
        )}
      </div>
    </details>
  );
}
