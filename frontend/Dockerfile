# syntax=docker/dockerfile:1.7
# Multi-stage Next.js build producing a `next build` standalone runtime.
# NODE_OPTIONS heap cap on the build stage: on a memory-constrained deploy
# target, the default V8 heap can let `next build` balloon into swap and
# stall — cap it explicitly rather than rely on host headroom.

FROM node:22-alpine AS deps
WORKDIR /app
RUN corepack enable
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

FROM node:22-alpine AS build
WORKDIR /app
RUN corepack enable
ENV NEXT_TELEMETRY_DISABLED=1 \
    NODE_OPTIONS=--max-old-space-size=1536
COPY --from=deps /app/node_modules ./node_modules
COPY . .
RUN pnpm build

FROM node:22-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    PORT=3000 \
    # Docker always pre-sets HOSTNAME to the container id, so Next's
    # standalone server.js (which binds `process.env.HOSTNAME || '0.0.0.0'`)
    # never falls through to its 0.0.0.0 default and instead binds to a
    # single interface — unreachable from Traefik/any proxy. Force it.
    HOSTNAME=0.0.0.0
RUN addgroup --system --gid 1001 nodejs \
    && adduser --system --uid 1001 nextjs
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
COPY --from=build /app/public ./public
USER nextjs
EXPOSE 3000
CMD ["node", "server.js"]
