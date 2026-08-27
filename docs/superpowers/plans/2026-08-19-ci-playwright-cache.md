# CI Playwright browser + yarn caching Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cache the Playwright Chromium browser and the yarn package cache in the 26-shard `pn-playwright-test` job of both CI workflow files, keyed on the resolved `@playwright/test` version so it auto-invalidates on version bumps.

**Architecture:** Add a new single-run `prewarm-playwright` job (per file) that resolves the Playwright version from the host frontend `yarn.lock` and downloads the browser once into `actions/cache`. The shard job gains a `needs` dependency on it, restores the browser cache keyed on the prewarm job's output version, restores a standalone yarn cache, and splits the browser-install step into a cache-miss (full `--with-deps`) path and a cache-hit (`install-deps` only) path. The change is duplicated verbatim across `dotnet-core-pr.yml` and `dotnet-core-master.yml`, with each file reusing its **own** `extract_branch` block.

**Tech Stack:** GitHub Actions, `actions/cache@v4`, `actions/checkout@v3`, `actions/setup-node@v3` (node 22), yarn v1 classic, Playwright CLI.

## Global Constraints

- **Source of truth:** `docs/superpowers/specs/2026-08-19-ci-playwright-cache-design.md`. Follow it exactly.
- **No local test harness for GitHub Actions YAML.** The only local verification per task is that the modified file still parses/lints cleanly (`actionlint` if available, else `python3 -c "import yaml; yaml.safe_load(open(...))"`). The **authoritative** validation is the PR's own CI run showing `prewarm-playwright` populating the browser cache and the shards restoring it (cache-hit path runs `npx playwright install-deps chromium` with no browser download). State this in every task's verification.
- **Two files in sync:** every change lands in BOTH `.github/workflows/dotnet-core-pr.yml` and `.github/workflows/dotnet-core-master.yml`. Any adjustment must be mirrored.
- **Per-file `extract_branch`:** the two files' `extract_branch` step bodies differ (pr.yml has a `stable` fallback; master.yml is a plain one-liner). In each file the `prewarm-playwright` job MUST copy **that file's own** `extract_branch` block verbatim.
- **Identical key strings (do NOT rename):**
  - browser cache path: `~/.cache/ms-playwright`
  - prewarm browser cache key: `${{ runner.os }}-playwright-${{ steps.pw.outputs.version }}`
  - shard browser cache key: `${{ runner.os }}-playwright-${{ needs.prewarm-playwright.outputs.version }}`
  - yarn cache path: `~/.cache/yarn`; key: `${{ runner.os }}-yarn-${{ hashFiles('eform-angular-frontend/eform-client/yarn.lock') }}`; restore-keys: `${{ runner.os }}-yarn-`
  - step ids: `pw` (version resolution), `pwcache` (browser cache); job output name: `version`.
  The version component of the prewarm key and the shard key MUST resolve to the same string — that is the whole point of exposing `version` as a job output.
- **Indentation (both files):** job names at 2 spaces; job-level keys (`runs-on`, `outputs`, `needs`, `strategy`, `steps`) at 4 spaces; step dashes (`- name:`) at 4 spaces; step keys at 6 spaces; `with:` children at 8 spaces.
- **Standard commits only** — no `--amend`, no force, no push, no PR from these tasks.
- **Scope:** CI infrastructure only. No test, product, or docker-orchestration behavior changes.

---

## File Structure

- `.github/workflows/dotnet-core-pr.yml` — add `prewarm-playwright` job before `pn-playwright-test`; wire the shard job. (Tasks 1–2)
- `.github/workflows/dotnet-core-master.yml` — same two changes, mirrored, using master.yml's own `extract_branch`. (Tasks 3–4)
- Task 5 validates both files parse/lint and finalizes.

Line numbers below are from the current HEAD of this worktree (re-confirmed 2026-08-19). Re-check them before editing; anchor edits on the surrounding text shown, not the numbers.

---

### Task 1: Add `prewarm-playwright` job to `dotnet-core-pr.yml`

**Files:**
- Modify: `.github/workflows/dotnet-core-pr.yml` (insert a new job immediately before `  pn-playwright-test:` at line 59)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: job `prewarm-playwright` with output `version` (the resolved `@playwright/test` version string, e.g. `1.58.2`), consumed by Task 2 via `${{ needs.prewarm-playwright.outputs.version }}`.

- [ ] **Step 1: Insert the new job before `pn-playwright-test`**

The current text at lines 55–60 is:

```yaml
    - uses: actions/upload-artifact@v4
      with:
        name: work-items-planning-container
        path: work-items-planning-container.tar
  pn-playwright-test:
    needs: backend-pn-build
```

Insert the entire `prewarm-playwright` job between the `path: work-items-planning-container.tar` line and the `  pn-playwright-test:` line, so it reads:

```yaml
    - uses: actions/upload-artifact@v4
      with:
        name: work-items-planning-container
        path: work-items-planning-container.tar
  prewarm-playwright:
    runs-on: ubuntu-latest
    outputs:
      version: ${{ steps.pw.outputs.version }}
    steps:
    - name: Extract branch name
      id: extract_branch
      run: |
        BRANCH=$(echo ${GITHUB_REF#refs/heads/})
        if [[ "$BRANCH" != "stable" && "$BRANCH" != "master" && "$BRANCH" != "angular19" ]]; then
          BRANCH="stable"
        fi
        echo "BRANCH=$BRANCH" >> $GITHUB_OUTPUT
    - name: 'Preparing Frontend checkout'
      uses: actions/checkout@v3
      with:
        fetch-depth: 1
        repository: microting/eform-angular-frontend
        ref: ${{ steps.extract_branch.outputs.BRANCH }}
        path: eform-angular-frontend
    - name: Use Node.js
      uses: actions/setup-node@v3
      with:
        node-version: 22
    - name: Resolve Playwright version
      id: pw
      run: |
        cd eform-angular-frontend/eform-client
        VER=$(awk -F'"' '/^"@playwright\/test@/{b=1} b&&/^  version/{print $2; exit}' yarn.lock)
        echo "version=$VER" >> "$GITHUB_OUTPUT"
    - name: Cache Playwright browsers
      id: pwcache
      uses: actions/cache@v4
      with:
        path: ~/.cache/ms-playwright
        key: ${{ runner.os }}-playwright-${{ steps.pw.outputs.version }}
    - name: Download browsers (cache miss only)
      if: steps.pwcache.outputs.cache-hit != 'true'
      run: npx playwright@${{ steps.pw.outputs.version }} install chromium
  pn-playwright-test:
    needs: backend-pn-build
```

Notes:
- The `extract_branch` block above is copied **verbatim from pr.yml's own** shard step (lines 70–77), including the `stable` fallback.
- Prewarm uses `fetch-depth: 1` (shallow) per spec §A.2 — only `yarn.lock` is needed. The `ref:` expression is identical to the shard's, so the resolved version cannot diverge.
- No `--with-deps` in the download step — prewarm only needs the browser binary in `~/.cache/ms-playwright`; apt system-deps are handled per-shard.

- [ ] **Step 2: Verify the file still parses**

Run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/dotnet-core-pr.yml')); print('OK')"`
Expected: `OK` (no traceback).

If `actionlint` is installed, also run: `actionlint .github/workflows/dotnet-core-pr.yml`
Expected: no output (exit 0). (If `actionlint` is not installed, skip — the yaml parse is sufficient locally; CI is the authoritative check.)

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/dotnet-core-pr.yml
git commit -m "ci(pr): add prewarm-playwright job to prewarm the browser cache"
```

---

### Task 2: Wire the `pn-playwright-test` shard job in `dotnet-core-pr.yml` to the caches

**Files:**
- Modify: `.github/workflows/dotnet-core-pr.yml` — shard `needs` (line 60 pre-Task-1; shifts down after Task 1's insertion), yarn cache before `yarn install`, browser cache + split install steps replacing `Install Playwright browsers` (lines 132–135 pre-Task-1).

**Interfaces:**
- Consumes: `prewarm-playwright` job's `version` output (Task 1) via `${{ needs.prewarm-playwright.outputs.version }}`.
- Produces: nothing consumed by later pr.yml tasks.

> After Task 1, the shard job's line numbers shifted down by the inserted job. Anchor every edit below on the surrounding text, not the numbers.

- [ ] **Step 1: Add `prewarm-playwright` to the shard's `needs`**

Current (shard job header):

```yaml
  pn-playwright-test:
    needs: backend-pn-build
    runs-on: ubuntu-latest
```

Change to:

```yaml
  pn-playwright-test:
    needs: [backend-pn-build, prewarm-playwright]
    runs-on: ubuntu-latest
```

- [ ] **Step 2: Insert the yarn cache before `yarn install`**

Current text (shard job, "yarn install" step, was lines 132–133):

```yaml
    - name: yarn install
      run: cd eform-angular-frontend/eform-client && yarn install
```

Change to (insert the `Cache yarn` step directly above it):

```yaml
    - name: Cache yarn
      uses: actions/cache@v4
      with:
        path: ~/.cache/yarn
        key: ${{ runner.os }}-yarn-${{ hashFiles('eform-angular-frontend/eform-client/yarn.lock') }}
        restore-keys: ${{ runner.os }}-yarn-
    - name: yarn install
      run: cd eform-angular-frontend/eform-client && yarn install
```

- [ ] **Step 3: Add the browser cache and split the install step**

Current text (shard job, "Install Playwright browsers" step, was lines 134–135):

```yaml
    - name: Install Playwright browsers
      run: cd eform-angular-frontend/eform-client && npx playwright install --with-deps chromium
```

Replace those two lines entirely with:

```yaml
    - name: Cache Playwright browsers
      id: pwcache
      uses: actions/cache@v4
      with:
        path: ~/.cache/ms-playwright
        key: ${{ runner.os }}-playwright-${{ needs.prewarm-playwright.outputs.version }}
    - name: Install Playwright (+browsers) — cache miss
      if: steps.pwcache.outputs.cache-hit != 'true'
      run: cd eform-angular-frontend/eform-client && npx playwright install --with-deps chromium
    - name: Install Playwright system deps only — cache hit
      if: steps.pwcache.outputs.cache-hit == 'true'
      run: cd eform-angular-frontend/eform-client && npx playwright install-deps chromium
```

After this step the shard region reads, in order: `Cache yarn` → `yarn install` → `Cache Playwright browsers` → `Install Playwright (+browsers) — cache miss` → `Install Playwright system deps only — cache hit` → `Create errorShots directory` (unchanged, next existing step).

- [ ] **Step 4: Verify the file still parses**

Run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/dotnet-core-pr.yml')); print('OK')"`
Expected: `OK`.

If available: `actionlint .github/workflows/dotnet-core-pr.yml` → no output (exit 0).

Also grep to confirm the two keys share an identical version component and the ids are consistent:

Run: `grep -nE 'playwright-\$\{\{ (runner\.os|steps\.pw|needs\.prewarm)|id: pw' .github/workflows/dotnet-core-pr.yml`
Expected: shows the prewarm key `...-playwright-${{ steps.pw.outputs.version }}`, the shard key `...-playwright-${{ needs.prewarm-playwright.outputs.version }}`, and `id: pw` / `id: pwcache` — the trailing `...playwright-<X>` component identical modulo the source of `version`.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/dotnet-core-pr.yml
git commit -m "ci(pr): restore Playwright browser + yarn caches in shard job"
```

---

### Task 3: Add `prewarm-playwright` job to `dotnet-core-master.yml`

**Files:**
- Modify: `.github/workflows/dotnet-core-master.yml` (insert a new job immediately before `  pn-playwright-test:` at line 65)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: job `prewarm-playwright` with output `version`, consumed by Task 4.

> master.yml's `extract_branch` is the plain one-liner (no fallback). Everything else in the job is identical to Task 1.

- [ ] **Step 1: Insert the new job before `pn-playwright-test`**

The current text at lines 61–66 is:

```yaml
    - uses: actions/upload-artifact@v4
      with:
        name: work-items-planning-container
        path: work-items-planning-container.tar
  pn-playwright-test:
    needs: backend-pn-build
```

Insert the entire `prewarm-playwright` job between the `path: work-items-planning-container.tar` line and the `  pn-playwright-test:` line, so it reads:

```yaml
    - uses: actions/upload-artifact@v4
      with:
        name: work-items-planning-container
        path: work-items-planning-container.tar
  prewarm-playwright:
    runs-on: ubuntu-latest
    outputs:
      version: ${{ steps.pw.outputs.version }}
    steps:
    - name: Extract branch name
      id: extract_branch
      run: echo "BRANCH=$(echo ${GITHUB_REF#refs/heads/})" >> $GITHUB_OUTPUT
    - name: 'Preparing Frontend checkout'
      uses: actions/checkout@v3
      with:
        fetch-depth: 1
        repository: microting/eform-angular-frontend
        ref: ${{ steps.extract_branch.outputs.BRANCH }}
        path: eform-angular-frontend
    - name: Use Node.js
      uses: actions/setup-node@v3
      with:
        node-version: 22
    - name: Resolve Playwright version
      id: pw
      run: |
        cd eform-angular-frontend/eform-client
        VER=$(awk -F'"' '/^"@playwright\/test@/{b=1} b&&/^  version/{print $2; exit}' yarn.lock)
        echo "version=$VER" >> "$GITHUB_OUTPUT"
    - name: Cache Playwright browsers
      id: pwcache
      uses: actions/cache@v4
      with:
        path: ~/.cache/ms-playwright
        key: ${{ runner.os }}-playwright-${{ steps.pw.outputs.version }}
    - name: Download browsers (cache miss only)
      if: steps.pwcache.outputs.cache-hit != 'true'
      run: npx playwright@${{ steps.pw.outputs.version }} install chromium
  pn-playwright-test:
    needs: backend-pn-build
```

Notes:
- The `extract_branch` block above is copied **verbatim from master.yml's own** shard step (lines 76–78) — the plain one-liner, **no** `stable` fallback. This is the only line that differs from Task 1's job.
- Everything else (outputs, checkout with `fetch-depth: 1`, node 22, `id: pw` resolve, `id: pwcache` cache, miss-only download) is byte-identical to the pr.yml job.

- [ ] **Step 2: Verify the file still parses**

Run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/dotnet-core-master.yml')); print('OK')"`
Expected: `OK`.

If available: `actionlint .github/workflows/dotnet-core-master.yml` → no output (exit 0).

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/dotnet-core-master.yml
git commit -m "ci(master): add prewarm-playwright job to prewarm the browser cache"
```

---

### Task 4: Wire the `pn-playwright-test` shard job in `dotnet-core-master.yml` to the caches

**Files:**
- Modify: `.github/workflows/dotnet-core-master.yml` — shard `needs` (line 66 pre-Task-3; shifts down after Task 3), yarn cache before `yarn install`, browser cache + split install steps replacing `Install Playwright browsers` (lines 133–136 pre-Task-3).

**Interfaces:**
- Consumes: `prewarm-playwright` job's `version` output (Task 3) via `${{ needs.prewarm-playwright.outputs.version }}`.
- Produces: nothing consumed later.

> After Task 3, the shard job's line numbers shifted down by the inserted job. Anchor every edit on the surrounding text.

- [ ] **Step 1: Add `prewarm-playwright` to the shard's `needs`**

Current (shard job header):

```yaml
  pn-playwright-test:
    needs: backend-pn-build
    runs-on: ubuntu-latest
```

Change to:

```yaml
  pn-playwright-test:
    needs: [backend-pn-build, prewarm-playwright]
    runs-on: ubuntu-latest
```

- [ ] **Step 2: Insert the yarn cache before `yarn install`**

Current text (shard job, "yarn install" step, was lines 133–134):

```yaml
    - name: yarn install
      run: cd eform-angular-frontend/eform-client && yarn install
```

Change to (insert the `Cache yarn` step directly above it):

```yaml
    - name: Cache yarn
      uses: actions/cache@v4
      with:
        path: ~/.cache/yarn
        key: ${{ runner.os }}-yarn-${{ hashFiles('eform-angular-frontend/eform-client/yarn.lock') }}
        restore-keys: ${{ runner.os }}-yarn-
    - name: yarn install
      run: cd eform-angular-frontend/eform-client && yarn install
```

- [ ] **Step 3: Add the browser cache and split the install step**

Current text (shard job, "Install Playwright browsers" step, was lines 135–136):

```yaml
    - name: Install Playwright browsers
      run: cd eform-angular-frontend/eform-client && npx playwright install --with-deps chromium
```

Replace those two lines entirely with:

```yaml
    - name: Cache Playwright browsers
      id: pwcache
      uses: actions/cache@v4
      with:
        path: ~/.cache/ms-playwright
        key: ${{ runner.os }}-playwright-${{ needs.prewarm-playwright.outputs.version }}
    - name: Install Playwright (+browsers) — cache miss
      if: steps.pwcache.outputs.cache-hit != 'true'
      run: cd eform-angular-frontend/eform-client && npx playwright install --with-deps chromium
    - name: Install Playwright system deps only — cache hit
      if: steps.pwcache.outputs.cache-hit == 'true'
      run: cd eform-angular-frontend/eform-client && npx playwright install-deps chromium
```

After this step the shard region reads, in order: `Cache yarn` → `yarn install` → `Cache Playwright browsers` → `Install Playwright (+browsers) — cache miss` → `Install Playwright system deps only — cache hit` → `Create errorShots directory` (unchanged).

- [ ] **Step 4: Verify the file still parses**

Run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/dotnet-core-master.yml')); print('OK')"`
Expected: `OK`.

If available: `actionlint .github/workflows/dotnet-core-master.yml` → no output (exit 0).

Run: `grep -nE 'playwright-\$\{\{ (runner\.os|steps\.pw|needs\.prewarm)|id: pw' .github/workflows/dotnet-core-master.yml`
Expected: prewarm key `...-playwright-${{ steps.pw.outputs.version }}`, shard key `...-playwright-${{ needs.prewarm-playwright.outputs.version }}`, and `id: pw` / `id: pwcache` present.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/dotnet-core-master.yml
git commit -m "ci(master): restore Playwright browser + yarn caches in shard job"
```

---

### Task 5: Validate both files and finalize

**Files:**
- Read-only verification of both workflow files. No edits unless validation fails.

- [ ] **Step 1: Parse both files**

Run:

```bash
python3 -c "import yaml; [yaml.safe_load(open(f)) for f in ['.github/workflows/dotnet-core-pr.yml','.github/workflows/dotnet-core-master.yml']]; print('both OK')"
```

Expected: `both OK` (no traceback).

- [ ] **Step 2: Lint if actionlint is available**

Run: `command -v actionlint >/dev/null && actionlint .github/workflows/dotnet-core-pr.yml .github/workflows/dotnet-core-master.yml && echo "actionlint clean" || echo "actionlint not installed — CI is authoritative"`
Expected: `actionlint clean` if installed, else the fallback message. Neither is a failure; the yaml parse in Step 1 is the local gate.

- [ ] **Step 3: Confirm the two files' new blocks match (except extract_branch)**

Run:

```bash
grep -nE 'prewarm-playwright:|id: pw$|id: pwcache|-playwright-\$\{\{|-yarn-\$\{\{|install-deps chromium|needs: \[backend-pn-build, prewarm-playwright\]' .github/workflows/dotnet-core-pr.yml .github/workflows/dotnet-core-master.yml
```

Expected: each file shows the `prewarm-playwright:` job, `id: pw`, `id: pwcache`, both `-playwright-${{ ... }}` keys, the `-yarn-${{ ... }}` key, the `install-deps chromium` hit-path, and the widened `needs:` list. The only intended difference between the files is the `extract_branch` body (pr.yml multi-line with `stable` fallback; master.yml one-liner).

- [ ] **Step 4: No commit**

This task makes no source changes. If Steps 1–3 pass, there is nothing to commit. If any check fails, return to the offending task, fix, and re-run its own verification and commit — do not amend prior commits.

**Authoritative validation (post-merge / on the PR):** open the PR's Actions run and confirm (a) `prewarm-playwright` runs once and, on a cold cache, executes `Download browsers (cache miss only)`; (b) the shards' `Cache Playwright browsers` step reports a cache hit and the `Install Playwright system deps only — cache hit` step runs (no browser download); (c) `Cache yarn` restores; (d) all 26 shards still pass. On the first run after a Playwright version bump, prewarm downloads once and all shards hit.

---

## Self-Review

**1. Spec coverage** (spec `2026-08-19-ci-playwright-cache-design.md` → task):
- §A `prewarm-playwright` job (pr.yml) → Task 1. (master.yml) → Task 3.
- §A.1 extract-branch verbatim per file → Task 1 (pr fallback variant), Task 3 (master one-liner). Global Constraints restates the divergence.
- §A.2 frontend checkout, `ref: ${{ steps.extract_branch.outputs.BRANCH }}`, `fetch-depth: 1` shallow → Tasks 1 & 3, Step 1.
- §A.3 setup-node@v3 node 22 → Tasks 1 & 3.
- §A.4 resolve version via awk into `id: pw` output `version` → Tasks 1 & 3 (`echo "version=$VER" >> "$GITHUB_OUTPUT"`).
- §A.5 `actions/cache@v4` restore (`id: pwcache`) + miss-only `npx playwright@<ver> install chromium` (no `--with-deps`) → Tasks 1 & 3.
- §A job-level `outputs: version:` → Tasks 1 & 3, `outputs` block.
- §B.1 shard `needs: [backend-pn-build, prewarm-playwright]` → Tasks 2 & 4, Step 1.
- §B.2 shard browser cache keyed on `needs.prewarm-playwright.outputs.version` → Tasks 2 & 4, Step 3.
- §B.3 split install: miss `install --with-deps chromium`, hit `install-deps chromium`, each `cd eform-angular-frontend/eform-client && ...` → Tasks 2 & 4, Step 3.
- §B.4 standalone yarn cache after frontend checkout / before `yarn install` → Tasks 2 & 4, Step 2.
- "No local test harness; CI is authoritative" → Global Constraints + every task's verification + Task 5.
- Two-files-in-sync → Tasks 3–4 mirror 1–2; Task 5 Step 3 asserts parity.

**2. Placeholder scan:** No TBD/TODO/"similar to Task N"/"add error handling". Every YAML block is complete with the target file's exact indentation; master.yml's YAML is repeated in full (not referenced) even though it mirrors pr.yml.

**3. Type/name consistency:**
- Step ids `pw` and `pwcache` — identical in both jobs, both files.
- Job output name `version` — identical everywhere; prewarm sets `steps.pw.outputs.version`, shards read `needs.prewarm-playwright.outputs.version`.
- Browser cache path `~/.cache/ms-playwright` and yarn cache path `~/.cache/yarn` — identical in all four locations.
- Prewarm browser key `${{ runner.os }}-playwright-${{ steps.pw.outputs.version }}` and shard browser key `${{ runner.os }}-playwright-${{ needs.prewarm-playwright.outputs.version }}` — the `${{ runner.os }}-playwright-` prefix is byte-identical and the version component resolves to the same string (the shard reads the prewarm job's own output), so prewarm's saved key matches the shard's restore key exactly.
- Yarn key `${{ runner.os }}-yarn-${{ hashFiles('eform-angular-frontend/eform-client/yarn.lock') }}` with restore-keys `${{ runner.os }}-yarn-` — identical in Tasks 2 & 4.
