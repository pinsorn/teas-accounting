import { FlatCompat } from '@eslint/eslintrc';
import { dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const compat = new FlatCompat({ baseDirectory: dirname(fileURLToPath(import.meta.url)) });

export default [
  { ignores: ['.next/**', 'node_modules/**', 'playwright-report/**', 'test-results/**', 'next-env.d.ts', 'coverage/**'] },
  ...compat.extends('next/core-web-vitals', 'next/typescript'),
  {
    rules: {
      // Baseline 2026-09-04 (specs/fix-fe-ci-lint-gates.md): 0 errors / 17 warnings on the tree,
      // so nothing is downgraded. Add `'<rule>': 'warn'` here ONLY as a documented, temporary
      // baseline exception; WP-G burns the warnings down.
    },
  },
];
