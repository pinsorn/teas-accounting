// Sprint 13g — capture driver. Imports every walkthrough (each calls
// walkthrough() at module load → registry), then runs them id-ordered as
// serial Playwright tests: fresh context, persona login (unless self-
// bootstrap), execute body with a recording capture(), emit step JSON for
// the P3 markdown generator.

import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { test } from '@playwright/test';
import { getWalkthroughs } from './lib/walkthrough';
import { makeCapture, type StepRecord } from './lib/capture';
import { personas, personaFor, SELF_BOOTSTRAP_IDS } from './lib/personas';

// Register all walkthroughs (module side-effect).
import './walkthroughs/00.01-onboarding-wizard';
import './walkthroughs/01.01-login';
import './walkthroughs/01.02-dashboard-tour';
import './walkthroughs/01.03-language-toggle';
import './walkthroughs/01.04-logout';
import './walkthroughs/02.01-business-units';
import './walkthroughs/02.02-products';
import './walkthroughs/02.03-wht-types';
import './walkthroughs/02.04-api-keys';
import './walkthroughs/02.05-company-profile';
import './walkthroughs/03.01-customers';
import './walkthroughs/03.02-vendors';
import './walkthroughs/03.03-person-customer';
import './walkthroughs/03.04-expense-categories';
import './walkthroughs/03.05-employees';
import './walkthroughs/04.01-quotation';
import './walkthroughs/04.02-sales-order-delivery';
import './walkthroughs/04.03-billing-note';
import './walkthroughs/04.04-tax-invoice-post';
import './walkthroughs/04.05-receipt';
import './walkthroughs/04.06-credit-note';
import './walkthroughs/04.07-debit-note';
import './walkthroughs/04.08-product-selection';
import './walkthroughs/04.09-customer-vat-types';
import './walkthroughs/04.10-nonvat-company';
import './walkthroughs/04.11-nonvat-company-e2e';
import './walkthroughs/05.01-purchase-order';
import './walkthroughs/05.02-vendor-invoice';
import './walkthroughs/05.03-payment-voucher-wht';
import './walkthroughs/05.04-wht-explained';
import './walkthroughs/05.05-vendor-vat-types';
import './walkthroughs/05.06-person-vendor';
import './walkthroughs/06.01-payroll-run';
import './walkthroughs/07.01-pnd30-vat-return';
import './walkthroughs/07.02-wht-filing';
import './walkthroughs/07.03-corporate-income-tax';
import './walkthroughs/07.04-foreign-payment-tax';
import './walkthroughs/07.05-vat-registration-forms';
import './walkthroughs/07.06-pnd1-payroll-wht';
import './walkthroughs/07.07-pnd30-pdf';
import './walkthroughs/07.08-pnd3-pdf';
import './walkthroughs/07.09-pnd53-pdf';
import './walkthroughs/07.10-pnd54-pdf';
import './walkthroughs/08.01-financial-statements';
import './walkthroughs/08.02-tax-summary';
import './walkthroughs/08.03-ap-aging';

const CAPTURES_ROOT = resolve(__dirname, '../../docs/manual/captures');

// NOT .serial — a failure in one walkthrough must NOT skip the rest (the
// spec wants the pilot to complete + report which failed). workers:1 +
// fullyParallel:false (config) keeps strict id order + 01.04-logout
// isolation. Each walkthrough: own context; partial JSON written even on
// failure so P3 still renders what was captured; failure recorded then
// re-thrown so Playwright marks that one red while others continue.
test.describe('manual capture', () => {
  for (const { meta, body } of getWalkthroughs()) {
    test(`${meta.id} — ${meta.title}`, async ({ browser }) => {
      const ctx = await browser.newContext({
        viewport: { width: 1440, height: 900 },
        locale: 'th-TH',
      });
      const page = await ctx.newPage();
      page.setDefaultTimeout(15_000);
      const records: StepRecord[] = [];
      const chap = meta.id.slice(0, 2);
      let failure: string | null = null;

      try {
        if (!SELF_BOOTSTRAP_IDS.has(meta.id)) {
          const p = personas[personaFor(meta.id, meta.persona)];
          await page.goto('/login');
          await page.getByLabel('ชื่อผู้ใช้').fill(p.username);
          await page.getByLabel('รหัสผ่าน').fill(p.password);
          await page.getByRole('button', { name: 'เข้าสู่ระบบ' }).click();
          await page.waitForURL('http://localhost:3000/', { timeout: 30_000 });
        }
        const capture = makeCapture(page, meta.id, records);
        await body({ page, capture });
      } catch (e) {
        failure = e instanceof Error ? e.message : String(e);
      } finally {
        const jsonPath = resolve(CAPTURES_ROOT, chap, `${meta.id}.json`);
        mkdirSync(dirname(jsonPath), { recursive: true });
        writeFileSync(
          jsonPath,
          JSON.stringify(
            { meta, steps: records, failure, capturedSteps: records.length },
            null, 2,
          ),
          'utf8',
        );
        await ctx.close();
      }

      if (failure) {
        throw new Error(
          `${meta.id} failed after ${records.length} step(s): ${failure}`,
        );
      }
    });
  }
});
