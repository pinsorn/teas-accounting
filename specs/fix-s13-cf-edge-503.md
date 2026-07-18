# S13 — Investigation: prod browser 503s generated at Cloudflare edge, never reaching origin

Status: **INVESTIGATION ONLY — no fixes applied.** Read-only prod access (SSH key
`repttown_deploy`, VPS `ubuntu@158.69.197.154`), log reads + config `cat` only. No
edits, no restarts, no service changes were made anywhere on the VPS.

## Checklist

- [x] Read troubles-wiki.md S13 entry (lines 579–583) — proven diagnosis method:
  compare browser-observed 5xx against `proxy-host-13` origin access log; if origin
  shows 200/204 or nothing, the 5xx was fabricated at the CF edge.
- [x] Confirm SSH access (read-only) to prod VPS.
- [x] Pull NPM (nginx-proxy-manager) access + error logs for proxy-host-13
  (teas.kazaki-rio.com) across both incident dates (2026-07-16, 2026-07-18).
- [x] Read proxy host 13 config (`/opt/npm/data/nginx/proxy_host/13.conf`) and NPM's
  global `nginx.conf` / `proxy.conf` include.
- [x] Check system-level resource pressure (CPU/mem via `sar`, OOM/conntrack via
  `journalctl -k`) at the incident windows.
- [x] Check for CF-side-only causes not verifiable from the VPS; produced a concrete
  CF-dashboard checklist for Ham.
- [x] Check whether origin nginx or the origin app ever hit its own limits silently.
- [x] Write this spec with evidence, ranked hypotheses, proposed (unapplied) fixes,
  and the CF-dashboard checklist.

## Topology (confirmed from the VPS)

Browser → **Cloudflare edge** (DNS-proxied / "orange cloud", **not** a Cloudflare
Tunnel — confirmed no `cloudflared` process/container running; NPM listens directly
on host `0.0.0.0:80`/`0.0.0.0:443` via `docker-proxy`) → **nginx-proxy-manager**
(`docker` container `npm`, image `jc21/nginx-proxy-manager:latest`) → proxied to
`172.17.0.1:3100` (a bare `next-server` process on the host, **not** its own docker
container) → Next.js BFF route (`/api/proxy/[...path]`, has its own unrelated
"S13a" 30s upstream timeout to the .NET backend on `:8080`, already implemented,
out of scope here).

This VPS is **shared** with unrelated workloads (n8n, a WordPress demo, MSSQL
Express) — 4 vCPU / 7.1GB RAM total. Relevant because it means "the box is busy"
was a real candidate; ruled out below.

## Evidence collected

### 1. Origin access log: zero 503s, ever, for this host

```
$ sudo grep -oE ' [0-9]{3} [0-9]{3} ' /opt/npm/data/logs/proxy-host-13_access.log | sort | uniq -c | sort -rn
   7408  200 200
    586  307 307
    117  304 304
     67  202 202
     57  404 404
     52  401 401
     51  201 201
     40  204 204
     18  308 308
     13  422 422
      5  500 500
      4  302 302
      2  400 400
```

Zero `503` in the entire retained log (covers well past both incident dates — the
current file alone spans back before 2026-07-16, older content is gz-rotated).
The 5× `500` are real origin app errors, unrelated to S13 (2× `/api/proxy/admin/
rbac/users` on 2026-07-12, and one `/api/proxy/companies` on 2026-07-18 11:58 — the
latter is the already-fixed RLS bug from `specs/fix-company-create-rls-atomic.md`,
committed 4b92edd same day). Both sides show matching `500 500` — a real app bug
that origin logged correctly, the opposite signature from S13's edge-fabricated
503s. Noted here only so it isn't conflated with the S13 family.

### 2. Exact timestamp correlation — origin succeeded every time the browser saw 503

2026-07-16, 22:19–22:37 ICT window (payroll/employees UX test), from
`proxy-host-13_access.log`:

```
[16/Jul/2026:22:19:43 +0700] - 200 200 - GET  /api/proxy/employees/2  ... "https://teas.kazaki-rio.com/settings/employees"
[16/Jul/2026:22:26:49 +0700] - 401 401 - GET  /api/proxy/employees/2  ... "curl/8.18.0" "-"
[16/Jul/2026:22:26:51 +0700] - 401 401 - GET  /api/proxy/employees/2  ... "curl/8.18.0" "-"
[16/Jul/2026:22:26:52 +0700] - 401 401 - GET  /api/proxy/employees/2  ... "curl/8.18.0" "-"
[16/Jul/2026:22:33:27 +0700] - 200 200 - GET  /api/proxy/employees/2  ... "https://teas.kazaki-rio.com/settings/employees"
[16/Jul/2026:22:35:08 +0700] - 204 204 - PUT  /api/proxy/employees/2  ... "https://teas.kazaki-rio.com/settings/employees"
[16/Jul/2026:22:37:06 +0700] - 200 200 - GET  /api/proxy/employees/2  ... "https://teas.kazaki-rio.com/settings/employees"
```

The browser (per `REPORT-payroll-reports-uxtest.md` §Infra) saw the **GET 503 four
times in a row** around 22:19–22:28 with only **one** matching origin entry (the
22:19:43 `200`), and saw the **PUT 503** at 22:35:08 while origin logged a clean
**204 (applied)** at that exact second. Note the three `curl/8.18.0` lines at
22:26:49–52 (unauthenticated, no cookie, correctly `401`) sitting *between* the
browser's 503 cluster and succeeding every time — this is the "curl passes
throughout" evidence from the wiki, reproduced directly from the log.

2026-07-18, 13:1x ICT window (payroll run approve/post, per `STATUS.md` F-5):

```
[18/Jul/2026:13:24:18 +0700] - 204 204 - POST /api/proxy/payroll/runs/5/approve ... "https://teas.kazaki-rio.com/payroll/5"
[18/Jul/2026:13:24:39 +0700] - 204 204 - POST /api/proxy/payroll/runs/5/post    ... "https://teas.kazaki-rio.com/payroll/5"
```

Both actions were reported as browser-side 503s that "succeeded server-side, JE
created" — and indeed each shows **exactly one** origin entry, each a clean `204`.
Not a duplicate-submission/retry artifact (only one origin hit per action) — the
edge told the client 503 for a request origin had already finished successfully.

2026-07-16, 22:00–22:40 ICT — RSC prefetch traffic (`?_rsc=`) for the exact paths
the wiki flagged as "503'd systematically" (bank-accounts, sales-orders, customers,
payroll/1) all show **repeated successful origin hits across the same window**
(e.g. `bank-accounts?_rsc=` succeeded at 22:13:35, 22:15:19, 22:21:38, 22:32:41,
22:38:06) — i.e. these paths were not uniformly broken, they intermittently failed
and intermittently passed within the *same* half hour, from the *same* browser
session. That rules out a static/deterministic block on those URLs and points at a
**scoring/heuristic** mechanism (see Hypothesis 1). Also note: the same session's
requests were served via at least three different CF edge/PoP source IPs over time
(`172.68.241.118`, `172.68.241.119`, and once `108.162.241.160` at 22:21:31) —
consistent with Cloudflare anycast + per-PoP bot-score variance, not a single
misbehaving link.

### 3. NPM error log: no upstream errors, only benign buffering warnings

`proxy-host-13_error.log` has zero `[error]` lines in either incident window. The
only `[warn]` lines across both dates are all the same shape — a **large/slow
response buffered to a temp file**, and always for the *same* recurring background
poll target, not for any of the paths users saw 503 on:

```
2026/07/16 13:03:03 [warn] ... an upstream response is buffered to a temporary file ...
  request: "GET /api/proxy/attachments/3/download HTTP/2.0" ...
2026/07/16 22:19:18 [warn] ... same request, same file, different referrer ...
```

This attachment (`attachments/3/download`) is fetched repeatedly across many hours
regardless of what page the referrer says — looks like a recurring background
poll unrelated to S13, flagged here as a minor separate oddity worth a look
(possibly an unintentional interval-poll of a logo/asset), but it is not a 503 and
not evidence of an origin-side problem.

### 4. Host resource pressure: ruled out

`sar` CPU + memory at both 2026-07-16 windows (10-minute samples, the granularity
available):

```
=== CPU 13:00-13:12 ===
13:00:01   all   2.92  0.00  0.95  0.02  0.01  96.10   (idle)
=== CPU 22:00-22:40 ===
22:10:02   all   2.54 ... 96.45 idle
22:20:06   all   1.66 ... 97.63 idle
22:30:02   all   2.15 ... 96.46 idle
22:40:02   all   1.69 ... 97.57 idle
=== MEM 22:00-22:40 ===
22:10:02   kbavail 2960332  %memused 50.96  %commit 96.91
22:40:02   kbavail 2904592  %memused 51.65  %commit 97.23
```

CPU is >96% idle throughout both windows; memory available stays ~2.9GB the whole
time (no downward trend, no swap thrash). `journalctl -k` for both boots covering
these windows was grepped for `oom|kill|memory|conntrack|nf_conntrack` — **zero
matches** in either window. No OOM kills, no conntrack exhaustion, no kernel-level
network pressure. `docker inspect npm` shows `RestartCount=0`, `OOMKilled=false` —
the NPM container itself never crashed or restarted around either incident.
**Host/VPS resource exhaustion is ruled out** as the cause.

### 5. Origin nginx config: standard, generous, nothing misconfigured

`proxy_host/13.conf`: `http2 on`, `proxy_http_version 1.1`, standard
`Upgrade`/`Connection` headers (websocket-compatible pattern), access/error logs
correctly scoped. NPM's global `nginx.conf`: `proxy_connect_timeout 90s`,
`proxy_send_timeout 90s`, `proxy_read_timeout 90s`, `keepalive_timeout 90s`,
`worker_processes auto`, no custom `worker_connections` override (default). No
`proxy_buffering off`, no unusual body-size limits. Nothing here would cause a
premature 503 for a fast (<1s) API call, and no evidence in the error log that any
of these limits were ever hit.

### 6. Hourly Let's Encrypt renewal cron — checked, ruled out as primary cause

`docker logs npm` shows a `"Renewing SSL certs expiring within 30 days"` job firing
**every hour, every day, all day** (`:02:2x`/`:02:4x` past the hour, 24×/day: 12:02
AM, 1:02 AM, 2:02 AM, ... 11:02 PM). On 2026-07-16 it ran at `1:02:29 PM` (i.e.
13:02:29, inside the first incident window) and renewed 4 certs — `#1
intern.kazaki-rio.online`, `#9 kazaki-rio.online`, `#11 wp-demo1.kazaki-rio.online`,
`#23 playground.repttown.com` — **teas.kazaki-rio.com's own cert (`npm-29`) was not
in that batch**, and the whole job completed in 6 seconds (13:02:29→13:02:35),
while the incident's 503 activity continued through 13:10+. This job runs 24
times/day irrespective of whether an incident is happening, so its timing overlap
with 2 of the 3 known windows is very likely coincidental rather than causal. Not
pursued further, documented for completeness.

### 7. `curl` (no cookie, no auth) succeeded throughout every incident window

Confirmed directly in the origin log (the three `curl/8.18.0` 401s above, landing
inside the browser's 503 cluster and succeeding every time) and consistent with
prior manual testing noted in the wiki. This is the single most important
differentiator: whatever is intercepting traffic at the edge is **not**
capacity/network-wide — it discriminates on something specific to the
authenticated browser session (cookie, TLS/JA3 fingerprint, request cadence, or
automation signals), not on the domain or IP as a whole.

## Ranked hypotheses

**H1 — Cloudflare Bot Management / Bot Fight Mode (or WAF managed rule) scoring
the automated browser session as suspicious and blocking/challenging it at the
edge before origin is contacted. Confidence: HIGH.**

Fits every observed fact:
- Zero origin log entries for most of the browser's 503s (blocked before reaching
  origin) — but *not all*, some show a completed origin 2xx anyway (see H2 note
  below for that residual case).
- `curl` (different UA, different TLS fingerprint, no cookie/session) sailed
  through unaffected during the exact same minutes.
- The *same* URL (`bank-accounts?_rsc=...`, `employees/2`, etc.) succeeded
  repeatedly and failed intermittently within the same 20–30 minute session — a
  scoring/heuristic signature, not a static path-based WAF rule (which would block
  100% of matching requests).
- These test sessions are driven by an automated Chrome instance (Claude-in-Chrome
  CDP / browser automation), which exhibits well-known automation fingerprints
  (`navigator.webdriver`, CDP artifacts, inhuman click/request cadence and prefetch
  bursts — e.g. 15 RSC prefetches fired within 14 seconds at 22:00:50–22:01:04,
  itself a Next.js router prefetch burst that a bot heuristic could plausibly
  flag) — exactly the profile Cloudflare's "Definitely automated" bucket targets.
- Recurrence specifically during test/UX-drive sessions (2026-07-16, 2026-07-18)
  rather than constantly, matches "triggered by this traffic shape," not "always
  on."

**H2 — Cloudflare edge↔origin connection-pool / stale-keepalive race. Confidence:
MEDIUM.**

Explains the *residual* cases where origin shows a completed, successful response
(the 22:35:08 PUT → 204, and both 07-18 13:24 payroll actions → 204) yet the
browser still got 503. One well-documented CDN behavior class: the edge picks a
pooled persistent connection to origin that's mid-teardown (keepalive expiring,
worker rotating, or simply an edge-side idle/response timeout on the
client-facing leg) — the origin finishes and logs success, but the edge has
already given up on relaying that specific response to the browser and surfaces a
5xx there instead, sometimes retrying transparently and creating the exact "one
origin entry, browser saw error" signature. Cannot be fully confirmed or ruled out
from origin-side logs alone — this hypothesis lives specifically in the CF↔origin
leg, which the VPS side cannot observe. Not mutually exclusive with H1: a bot
challenge could account for the majority of clean-blocked cases, while a smaller
number of "204 but still 503'd" cases look more like this connection-race
signature specifically.

**H3 — Host/VPS resource exhaustion (CPU, memory, conntrack, OOM). Confidence:
LOW — largely ruled out.**

`sar` shows >96% CPU idle and stable ~2.9GB available memory through every sampled
window; `journalctl -k` shows zero OOM/conntrack/memory-pressure messages;
`docker inspect npm` shows no crash/restart. Kept as a residual possibility only
because `sar`'s 10-minute sampling could theoretically miss a very brief (<10 min)
spike, but nothing else in the evidence (repeatable failures spread across a full
20-40 minute window, not a single narrow spike) supports it.

**H4 — Origin nginx (NPM) hitting its own connection/worker/timeout limits.
Confidence: LOW — ruled out.**

Config is standard and generous (90s timeouts, default `worker_connections`, no
unusual body/buffer limits); the error log shows zero `[error]`-level entries and
the access log shows literally zero 503s ever logged for this host. If NPM itself
were rejecting requests, it would log them; it never did.

**H5 — Hourly Let's Encrypt renewal cron causing a global nginx reload that drops
in-flight/pooled connections for the teas vhost. Confidence: LOW — checked,
timing doesn't line up.**

Runs 24×/day, every day, regardless of whether an incident is occurring; the one
run that landed inside an incident window (2026-07-16 13:02:29) didn't touch
`teas.kazaki-rio.com`'s own certificate and completed in 6 seconds while incident
activity continued for another 8+ minutes. Documented for completeness, not
pursued as primary cause.

## Proposed fixes (config diffs proposed only — NOT applied)

**For H1 (Bot Management/WAF) — the actual fix lives in Cloudflare, not in this
repo or on the VPS.** Proposed CF Rule (Ham applies via dashboard, if confirmed):

```
# WAF → Custom Rules → new rule
Rule name: "S13 — skip bot challenge for TEAS API/asset traffic"
Expression:
  (http.host eq "teas.kazaki-rio.com" and
   (starts_with(http.request.uri.path, "/api/proxy/") or
    starts_with(http.request.uri.path, "/_next/static/") or
    http.request.uri.query contains "_rsc="))
Action: Skip
  ↳ Skip: Bot Fight Mode / Super Bot Fight Mode, Managed Challenge, Rate Limiting
    (whichever is confirmed as the actual blocker in Security → Events)
```

Do not skip *all* WAF for the whole host — scope the exception to the specific
paths that must never be challenged (XHR/fetch/asset paths can't solve a JS
challenge; a normal page nav to `/login` etc. can still be challenged safely).

**For H2 (edge↔origin connection race) — origin-side hardening (defensive, not
proven necessary from origin logs, but cheap and safe):**

Proposed addition to `13.conf`'s `location /` block (NPM "Advanced" custom config
tab for this proxy host), retry an upstream connection failure internally before
ever surfacing an error:

```nginx
proxy_next_upstream error timeout http_502 http_503 http_504;
proxy_next_upstream_tries 2;
```

Proposed increase to NPM's global custom config (`/data/nginx/custom/
http_top.conf`, currently absent/empty on this box) to widen the origin-side
keepalive window so it comfortably exceeds any Cloudflare-side pooled-connection
lifetime, reducing the stale-connection race window:

```nginx
# /data/nginx/custom/http_top.conf (new file — currently doesn't exist)
keepalive_timeout 300s;
keepalive_requests 1000;
```

Neither of these origin-side changes can fix a *purely* edge-fabricated 503 (H1) —
they only help the H2 slice of the problem, and only if CF's own "HTTP/2 to
Origin" / keep-alive settings (see CF checklist below) turn out to be the
mismatched side of the race.

**H3/H4/H5** — no fix proposed; not confirmed as causal.

## What Ham must check in the Cloudflare dashboard (cannot be verified from the VPS)

1. **Security → Events** (WAF/Bot activity log) — filter by hostname
   `teas.kazaki-rio.com`, time range covering all three windows: 2026-07-16
   13:02–13:12 ICT, 2026-07-16 22:10–22:40 ICT, 2026-07-18 ~13:10–13:30 ICT. Look
   for `Block` / `Managed Challenge` / `JS Challenge` actions on
   `/api/proxy/employees/2`, `/api/proxy/payroll/runs/5/approve`,
   `/api/proxy/payroll/runs/5/post`, and any `?_rsc=` or `/_next/static/` request
   at those exact timestamps.
2. **Security → Bots** — is Bot Fight Mode or Super Bot Fight Mode enabled? What
   action is set for "Definitely automated" traffic (Block vs. Managed Challenge)?
   This is the single highest-value thing to check first given H1.
3. **Security → WAF → Rate limiting rules** — any rule keyed on request rate per
   session/IP/path that could match a fast prefetch burst (Next.js fired 15 RSC
   prefetches in 14 seconds in one observed burst) or rapid sequential API calls.
4. **Analytics → Security Analytics** (or Traffic Analytics) — filter status 503,
   hostname `teas.kazaki-rio.com`, the three windows; check whether the dashboard
   breaks out "edge-generated" vs. "origin-passed-through" 5xx (Cloudflare's
   analytics UI usually distinguishes these).
5. **Instant Logs / Logpush** (if available on the plan) — pull the Ray ID for one
   of the exact captured timestamps (e.g. **2026-07-16 22:35:08 ICT, PUT
   `/api/proxy/employees/2`**, confirmed origin 204 at that same second) and check
   its `OriginResponseStatus`, `OriginResponseTime`, and `EdgeResponseStatus`
   fields — this single Ray ID lookup would definitively confirm or refute H1 vs
   H2 for that request.
6. **SSL/TLS → Edge Certificates → HTTP/2 to Origin** — confirm this matches
   origin's `http2 on` (in `13.conf`); consider toggling off as an isolation test
   if H2 is suspected, since HTTP/2-to-origin multiplexing changes how connections
   are pooled/reused vs. HTTP/1.1 keep-alive.
7. **Network → Argo Smart Routing** — if enabled, it adds an extra proxying hop
   that can independently introduce edge-side connection variance; worth checking
   whether the incident windows correlate with Argo route changes.
8. **Caching → Configuration / Cache Rules** — confirm no rule matches
   `/_next/static/*` or `/api/proxy/*` in a way that could **cache** an
   error response (a cached 503 would reproduce on retry without ever hitting
   origin again, which would also explain repeat failures on the same path within
   a session).

## Notes / non-goals

- No config was edited, no service was restarted, and no fix was applied anywhere
  on the VPS or in this repo — this task was investigation-only per the dispatch.
- The unrelated 5× real origin `500`s (RBAC users on 07-12, companies RLS bug on
  07-18, already fixed) were checked and confirmed to be a *different* failure
  class (both sides log the same status — a real app bug, not an edge-fabricated
  error) so they should not be pulled into any S13 fix.
- The recurring `attachments/3/download` background poll (flagged in §3) is
  unrelated to S13 but looks like it might be worth a separate look — not
  investigated further here, out of scope for this task.
