# CI Playwright browser + yarn caching — design spec

**Date:** 2026-08-19
**Branch:** `feat/ci-playwright-cache`
**Status:** Approved for implementation
**Scope:** CI infrastructure only. No test, product, or docker-orchestration behavior changes.

## Problem

The `pn-playwright-test` job fans out into 26 shards (matrix `test: [a..z]`) in
both `.github/workflows/dotnet-core-pr.yml` and `.github/workflows/dotnet-core-master.yml`.
Every shard runs, from a cold runner:

- `yarn install` in the checked-out host frontend (`eform-angular-frontend/eform-client`), and
- `npx playwright install --with-deps chromium` — which downloads the Chromium
  build from the Playwright CDN and installs apt system deps.

The Playwright browser download is the dominant slow/flaky cost: 26 concurrent
CDN fetches per workflow run, with no caching. This spec removes that cost by
caching the browser binary and the yarn package cache, keyed so the cache
auto-invalidates when the Playwright version changes.

## Constraints (why the obvious shortcut is rejected)

- The job runs on a **bare `ubuntu-latest` runner** because it orchestrates
  host docker directly: `docker network create`, `docker run` for MariaDB
  (`mariadb:10.8`) and RabbitMQ (`rabbitmq:latest`), `docker run --name my-container
  -p 4200:5000 ...` for the app-under-test, plus `docker exec`/`docker restart`
  against those containers. The app is reached at `localhost:4200` (host port
  4200 → container port 5000). Moving the job into a Playwright container would
  require docker-in-docker and rewriting the container/app network wiring —
  **considered and rejected.**
- Playwright is **not pinned in this repo.** The version is resolved at job time
  from the host frontend lockfile that the job checks out:
  `eform-angular-frontend/eform-client/yarn.lock`. The cached browser build must
  match the `@playwright/test` version exactly, so the cache key must derive from
  that resolved version rather than from anything in this repo.
- The change is **duplicated across both workflow files** (`dotnet-core-pr.yml`
  and `dotnet-core-master.yml`). Both must be kept in sync.

## Verified facts from the current workflows (read 2026-08-19)

Line numbers are from the current HEAD (`c6635997`) of this worktree.

### `.github/workflows/dotnet-core-pr.yml`

| Item | Location |
| --- | --- |
| `pn-playwright-test:` job header | line 59 |
| `needs: backend-pn-build` | line 60 |
| matrix `test: [a..z]` (26 shards) | line 65 |
| **Extract branch name** step (`id: extract_branch`) | lines 70–77 |
| **Use Node.js** — `actions/setup-node@v3`, `node-version: 22` | lines 97–100 |
| **Preparing Frontend checkout** — `microting/eform-angular-frontend` → `path: eform-angular-frontend` | lines 101–107 |
| frontend checkout `ref:` expression | line 106: `ref: ${{ steps.extract_branch.outputs.BRANCH }}` |
| **yarn install** (`cd eform-angular-frontend/eform-client && yarn install`) | lines 132–133 |
| **Install Playwright browsers** (`... && npx playwright install --with-deps chromium`) | lines 134–135 |

The `pr.yml` **Extract branch name** step body (lines 72–77) has a fallback:

```yaml
- name: Extract branch name
  id: extract_branch
  run: |
    BRANCH=$(echo ${GITHUB_REF#refs/heads/})
    if [[ "$BRANCH" != "stable" && "$BRANCH" != "master" && "$BRANCH" != "angular19" ]]; then
      BRANCH="stable"
    fi
    echo "BRANCH=$BRANCH" >> $GITHUB_OUTPUT
```

On a `pull_request` event `GITHUB_REF` is `refs/pull/N/merge`, so
`${GITHUB_REF#refs/heads/}` does not strip and the branch falls through the
guard to `BRANCH="stable"` in practice — but the prewarm job must replicate this
**exact** block so it resolves the same ref the shards do.

### `.github/workflows/dotnet-core-master.yml`

| Item | Location |
| --- | --- |
| `pn-playwright-test:` job header | line 65 |
| `needs: backend-pn-build` | line 66 |
| matrix `test: [a..z]` (26 shards) | line 71 |
| **Extract branch name** step (`id: extract_branch`) | lines 76–78 |
| **Use Node.js** — `actions/setup-node@v3`, `node-version: 22` | lines 98–101 |
| **Preparing Frontend checkout** → `path: eform-angular-frontend` | lines 102–108 |
| frontend checkout `ref:` expression | line 107: `ref: ${{ steps.extract_branch.outputs.BRANCH }}` |
| **yarn install** | lines 133–134 |
| **Install Playwright browsers** | lines 135–136 |

The `master.yml` **Extract branch name** step (lines 76–78) is **different** — no
fallback, because it runs on `push` (real branch ref):

```yaml
- name: Extract branch name
  id: extract_branch
  run: echo "BRANCH=$(echo ${GITHUB_REF#refs/heads/})" >> $GITHUB_OUTPUT
```

> **Critical:** the two files' `extract_branch` bodies differ. In each file the
> prewarm job MUST copy that file's own `extract_branch` block verbatim. The
> `ref:` expression consuming it (`ref: ${{ steps.extract_branch.outputs.BRANCH }}`)
> is identical in both.

### Other verified facts

- **Step ordering matters:** in the shard job `actions/setup-node@v3` (node 22)
  runs **before** the Frontend checkout. Therefore setup-node's built-in
  `cache: yarn` cannot see the lockfile at setup-node time; the yarn cache must be
  a **standalone `actions/cache` step placed after the frontend checkout.**
- The frontend checkout targets the **public** repo `microting/eform-angular-frontend`
  with `fetch-depth: 0` and **no token/secret** — the default `actions/checkout`
  token suffices. Prewarm can safely use a shallow checkout (`fetch-depth: 1`)
  since only `yarn.lock` is needed.
- `testinginstallpn.sh` (run in the "Copy dependencies" step between frontend
  checkout and `yarn install`) only edits `src/app/plugins/plugins.routing.ts`
  via `perl -pi`. It does **not** modify `package.json` or `yarn.lock`. So the
  resolved Playwright version and the yarn.lock hash are stable across the whole
  job, and prewarm's fresh checkout resolves the identical version.
- **Lockfile format confirmed** against the current host checkout
  (`microting/eform-angular-frontend`, branch `stable`,
  `eform-client/yarn.lock`): yarn v1 classic. The relevant block is:

  ```
  "@playwright/test@^1.50.0":
    version "1.58.2"
    resolved "https://registry.yarnpkg.com/@playwright/test/-/test-1.58.2.tgz#..."
  ```

  The awk in Component A below extracts `1.58.2` correctly from this format
  (verified by running it against the real file).

## Design

Two components, applied identically to **both** workflow files.

### A. New `prewarm-playwright` job

Runs before the shards and guarantees exactly **one** browser download per
version bump, so all 26 shards cache-hit — even on the first run after a
Playwright upgrade. The shards gain a dependency on it (Component B.1).

Steps:

1. **Extract branch name** — copy the surrounding file's own `extract_branch`
   step verbatim (the `pr.yml` fallback variant in `pr.yml`; the plain
   `master.yml` variant in `master.yml`).
2. **Frontend checkout** — `actions/checkout@v3`, `repository: microting/eform-angular-frontend`,
   `ref: ${{ steps.extract_branch.outputs.BRANCH }}`, `path: eform-angular-frontend`.
   Use `fetch-depth: 1` (shallow) — only `eform-client/yarn.lock` is required.
   This MUST use the same `ref:` expression the shards use, or the resolved
   version could diverge.
3. **Use Node.js** — `actions/setup-node@v3` with `node-version: 22` (the `npx
   playwright ... install` in step 5 needs node).
4. **Resolve Playwright version** into a job output:

   ```yaml
   - name: Resolve Playwright version
     id: pw
     run: |
       cd eform-angular-frontend/eform-client
       VER=$(awk -F'"' '/^"@playwright\/test@/{b=1} b&&/^  version/{print $2; exit}' yarn.lock)
       echo "version=$VER" >> "$GITHUB_OUTPUT"
   ```

   Verified: this yields `1.58.2` against the current host `yarn.lock` (yarn v1
   classic; block header `"@playwright/test@^1.50.0":`). See Risks for the
   must-re-verify note if the host ever migrates lockfile format / package
   manager.

5. **Cache + download browsers:**

   ```yaml
   - name: Cache Playwright browsers
     id: pwcache
     uses: actions/cache@v4
     with:
       path: ~/.cache/ms-playwright
       key: ${{ runner.os }}-playwright-${{ steps.pw.outputs.version }}
   - name: Download browsers (cache miss only)
     if: steps.pwcache.outputs.cache-hit != 'true'
     run: npx playwright@${{ steps.pw.outputs.version }} install chromium
   ```

   No `--with-deps` here — prewarm only needs the browser **binary** in
   `~/.cache/ms-playwright`; apt system-deps are runner-local and handled
   per-shard. `actions/cache`'s automatic post-step saves the cache on a miss.

Expose the resolved version as a **job output** so the shards key on the exact
same value:

```yaml
jobs:
  prewarm-playwright:
    runs-on: ubuntu-latest
    outputs:
      version: ${{ steps.pw.outputs.version }}
    steps:
      # ... steps 1–5 above
```

### B. `pn-playwright-test` (shard) changes

1. **Add the prewarm dependency:**

   ```yaml
   needs: [backend-pn-build, prewarm-playwright]
   ```

   (pr.yml line 60 / master.yml line 66 currently `needs: backend-pn-build`.)

2. **Browser cache restore** — placed **after** the Frontend checkout (so
   `yarn.lock` exists) and **before** the existing "Install Playwright browsers"
   step. Keyed on the prewarm job's output version, guaranteeing an identical key
   to prewarm:

   ```yaml
   - name: Cache Playwright browsers
     id: pwcache
     uses: actions/cache@v4
     with:
       path: ~/.cache/ms-playwright
       key: ${{ runner.os }}-playwright-${{ needs.prewarm-playwright.outputs.version }}
   ```

3. **Split the browser install step.** Replace the single
   `npx playwright install --with-deps chromium` (pr.yml lines 134–135 /
   master.yml lines 135–136) with:

   ```yaml
   - name: Install Playwright (+browsers) — cache miss
     if: steps.pwcache.outputs.cache-hit != 'true'
     run: cd eform-angular-frontend/eform-client && npx playwright install --with-deps chromium
   - name: Install Playwright system deps only — cache hit
     if: steps.pwcache.outputs.cache-hit == 'true'
     run: cd eform-angular-frontend/eform-client && npx playwright install-deps chromium
   ```

   On a hit: browsers come from the restored cache; only apt system-deps run — no
   CDN download. On a miss: the shard self-heals with the full `--with-deps`
   install (and re-populates the cache via the restore step's post action).

4. **Yarn cache** — standalone `actions/cache` (setup-node's built-in yarn cache
   cannot be used; it runs before the frontend checkout). Placed after the
   frontend checkout, before "yarn install" (pr.yml lines 132–133 / master.yml
   lines 133–134):

   ```yaml
   - name: Cache yarn
     uses: actions/cache@v4
     with:
       path: ~/.cache/yarn
       key: ${{ runner.os }}-yarn-${{ hashFiles('eform-angular-frontend/eform-client/yarn.lock') }}
       restore-keys: ${{ runner.os }}-yarn-
   ```

   Placement relative to the "Copy dependencies" step is not sensitive:
   `testinginstallpn.sh` does not modify `yarn.lock`, so `hashFiles` is stable.

## Behavior / invariants

- **Steady state (Playwright version unchanged):** every shard skips the
  Playwright CDN browser download; yarn packages are served from
  `~/.cache/yarn`; only apt system-deps run. The dominant slow/flaky cost is
  removed.
- **Auto-update:** the browser cache key is the resolved `@playwright/test`
  version. A version bump in the host lockfile changes the key → prewarm
  downloads once → all shards repopulate on their next run. No manual
  cache-busting.
- **First run after a bump:** prewarm downloads the new browser exactly once
  (one download total), so all 26 shards still cache-hit.
- **No behavioral change** to the tests, the docker orchestration, the two
  `wait-on http://localhost:4200` gates, or the app-under-test. Pure CI
  infrastructure.

## Out of scope (explicit)

- The `wait-on http://localhost:4200 --timeout 120000` dev-server gate flakiness
  — the actual cause of the shard (t)/(l) failures — is **deliberately
  deferred** per decision; not addressed here.
- Moving the job into a Playwright container — **rejected** (needs DinD + app
  network rewrite).
- Caching the MariaDB / RabbitMQ `docker pull`s — separate concern, not in this
  change.

## Risks / implementation checks

- **Lockfile parse:** the awk is verified against the current host `yarn.lock`
  (yarn v1 classic → `1.58.2`). If the host `eform-angular-frontend` ever
  migrates to a different lockfile format or package manager (yarn Berry,
  `package-lock.json`, `pnpm-lock.yaml`), the extraction must be adapted.
  Re-verify the resolved `version=` value in the prewarm job logs on first run.
- **Concurrent cache saves:** on a cold `~/.cache/ms-playwright` miss, up to 26
  shards may attempt `actions/cache` saves for the same key. `actions/cache@v4`
  handles this gracefully — losers log a harmless "cache already exists" and
  continue. The prewarm job makes shard-time misses rare (prewarm saves the key
  before shards start), so this is a first-run edge only.
- **setup-node ordering:** do NOT switch to setup-node's built-in `cache: yarn`
  — the lockfile is absent when setup-node runs (it precedes the frontend
  checkout). The standalone yarn cache step (B.4) is required.
- **Two files in sync:** the change is duplicated across `dotnet-core-pr.yml` and
  `dotnet-core-master.yml`. Any adjustment must be mirrored.
- **Prewarm ↔ shard ref agreement:** the prewarm frontend checkout MUST use the
  same branch-ref logic as the shards' frontend checkout (copy each file's own
  `extract_branch` block verbatim), or the version prewarm resolves could differ
  from what the shards install, defeating the cache-hit guarantee.

## Testing / verification

Workflow YAML has no unit-test harness. Verification is by running the PR's own
CI:

- On the first run, the `prewarm-playwright` job populates the browser cache on a
  miss and the shards restore it.
- Confirm via the Actions logs that on shards the **cache-hit** path runs
  `npx playwright install-deps chromium` only (no browser download), that
  yarn was restored from cache, and that all 26 shards still pass.
- The change is inherently CI-validated on the PR itself.
