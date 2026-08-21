import path from 'node:path';
import type { NextConfig } from 'next';
import createNextIntlPlugin from 'next-intl/plugin';

const withNextIntl = createNextIntlPlugin('./i18n/request.ts');

const nextConfig: NextConfig = {
  output: 'standalone',
  reactStrictMode: true,
  // Next 15.5: `typedRoutes` graduated from `experimental` to a stable
  // top-level option.
  typedRoutes: true,
  // Next 15.5 walks parent dirs for lockfiles to infer the workspace root.
  // A stray `C:\Users\<user>\package-lock.json` (outside this repo, not ours
  // to delete) makes it pick the home dir — wrong for standalone file
  // tracing. Pin tracing to this app dir explicitly.
  outputFileTracingRoot: path.join(__dirname),
  // No direct /api rewrite: all backend access goes through BFF route handlers
  // (app/api/auth/* and app/api/proxy/[...path]) so the JWT stays in an httpOnly
  // cookie and is never exposed to client JS (CLAUDE.md §10).
  // Type-checking is already owned by CI (`pnpm exec tsc --noEmit` in the
  // Typecheck step of .github/workflows/ci.yml, run on pull_request and on
  // push to main). Re-running tsc inside the Docker build exhausted memory
  // alongside a concurrent `dotnet publish` on the 2 vCPU / 3.7 GB deploy
  // box and caused repeated build failures there.
  typescript: { ignoreBuildErrors: true },
};

export default withNextIntl(nextConfig);
