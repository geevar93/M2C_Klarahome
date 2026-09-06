# CI Pipeline & Quality Gates

> **Status:** Step 5 of [`IMPLEMENTATION_PLAN.md`](IMPLEMENTATION_PLAN.md) — complete.
> **Specification:** [`06-infrastructure-devops.md`](06-infrastructure-devops.md) §6,
> [`09-nfr-testing-observability.md`](09-nfr-testing-observability.md) §1.4 and §2.
> **Last updated:** 2026-09-05

This document is the operator's guide to the quality gates: what they check, how to run them on
your own machine, how to read a failure, and what has to be configured on GitHub before the gates
can actually block a merge.

---

## 1. One pipeline, two entry points

There is exactly **one** definition of the gates: [`tools/ci.ps1`](../tools/ci.ps1).

```
                      tools/ci.ps1
                     /            \
       tools/ci.sh  /              \  .github/workflows/ci.yml
      (developers)                     (GitHub Actions)
```

The workflow does not restate the commands — every job step is `pwsh ./tools/ci.ps1 -Stage <name>`.
This is deliberate. A pipeline whose steps live only in YAML can only be tested by pushing, and it
drifts from what developers run until "it passes locally" and "it passes in CI" mean different
things. Here they are the same code path, so a gate can be reproduced and debugged in seconds
without a push.

`tools/ci.sh` is a thin POSIX wrapper that calls the same script through `pwsh`; it is not a second
implementation. The gates parse TRX counters and cobertura line rates, and two hand-written parsers
of the same XML would eventually disagree about whether the build passes — the one failure a
quality gate must never have. PowerShell 7 is the common denominator: it is already on the Windows
dev setup and preinstalled on every GitHub-hosted runner.

---

## 2. Running the gates locally

```bash
./tools/ci.sh                          # everything except container images
./tools/ci.sh format test              # just the two a code change usually trips
./tools/ci.sh package                  # build both images, scan with Trivy
./tools/ci.sh --skip-integration       # no Docker daemon: accept the gap, see below
./tools/ci.sh --coverage-minimum 80    # raise the floor for this run
```

```powershell
./tools/ci.ps1                         # Windows, same thing
./tools/ci.ps1 -Stage format,test
./tools/ci.ps1 -Stage package
```

| Stage | What it does | Fails when |
|---|---|---|
| `restore` | `dotnet restore --locked-mode`, `dotnet tool restore`, `npm ci` | a lock file does not match the manifest |
| `build` | `dotnet build -c Release -p:ContinuousIntegrationBuild=true -warnaserror` | any analyzer warning |
| `format` | `dotnet format --verify-no-changes`, `prettier --check` | any file is not formatted, or violates a `.editorconfig` style rule |
| `test` | the three backend suites with coverage, then the frontend unit tests | a test fails, a suite reports too few tests, or coverage is below the minimum |
| `lint` | `nx run-many --target=lint --all` | an ESLint error, including a module-boundary violation |
| `audit` | `npm audit --audit-level=high`, `dotnet list package --vulnerable` | a **high or critical** advisory |
| `codegen` | re-exports the API's OpenAPI document and compares the client it would generate against the one committed in `libs/data-access/api/src/generated`. Writes nothing | the committed client differs from what the current contract produces |
| `frontend` | production builds of `storefront` and `admin` | a build error or a budget overrun |
| `package` | `docker build` of the api and migrator images, then Trivy | a build failure, or a fixable high/critical CVE in an image |

`package` is not part of a default run: two container image builds are minutes of work a developer
checking a code change does not want, and CI runs it as its own job regardless.

---

## 3. How the backend tests are run, and why not `dotnet test`

**The suites are executed as their own executables**, not through `dotnet test`:

```
dotnet dotnet-coverage collect --settings coverage.settings.xml --output-format cobertura \
    --output artifacts/coverage/UnitTests.cobertura.xml \
    -- tests/KlaraHome.UnitTests/bin/Release/net10.0/KlaraHome.UnitTests -result-trx <path>
```

Two reasons, and neither is style.

**1. `dotnet test` can report a silent zero.** The Microsoft.Testing.Platform orchestrator reaches
the test host over a loopback JSON-RPC connection. When an endpoint-security agent blocks that
connection, the orchestrator reports **`Zero tests ran` and exits 0** — a green build on a suite
that never ran. This was hit on a developer machine during Step 4 and is recorded in
[`dev-setup.md`](dev-setup.md) §7. Running the executable is the same runner without the RPC hop.

**2. A count is asserted, not assumed.** Each suite declares a floor in `tools/ci.ps1`:

```powershell
$TestSuites = [ordered]@{
    'UnitTests'         = @{ Minimum = 90;  NeedsDocker = $false }
    'ArchitectureTests' = @{ Minimum = 10;  NeedsDocker = $false }
    'IntegrationTests'  = @{ Minimum = 55;  NeedsDocker = $true  }
}
```

These are tripwires, not targets. A suite that discovers nothing fails the build instead of passing
it. **Raise a floor when a step adds tests; never lower one to make a build go green.**

### Docker and the integration suites

The `IntegrationTests` suite starts a throwaway PostgreSQL 18 container through Testcontainers.

- **No Docker, no flag** → the run **fails**. A missing daemon is not a reason to skip a gate
  quietly.
- **`--skip-integration`** → the suite is skipped, and the **coverage threshold is not applied**.
  Without those tests, coverage drops from ~89 % to ~35 %; failing on that number would teach
  people that the coverage gate is noise. The run reports the figure, says plainly that it is not a
  full verdict, and leaves the gate to the run that measured everything.

---

## 4. Coverage

**The agreed minimum is 70 % line coverage**, from
[`09-nfr-testing-observability.md`](09-nfr-testing-observability.md) §1.4. It is enforced inside
`tools/ci.ps1`, not in the workflow YAML, so it holds locally too and cannot be dropped by editing
a workflow file.

At the close of Step 5, with 179 tests:

| Assembly | Line % | Branch % |
|---|--:|--:|
| KlaraHome.Api | 98.0 | 84.8 |
| KlaraHome.Infrastructure | 91.6 | 72.9 |
| KlaraHome.Contracts | 66.7 | 100.0 |
| KlaraHome.SharedKernel | 62.2 | 61.8 |
| KlaraHome.Modules.Platform | 62.1 | 0.0 |
| **Total** | **88.81** | **72.26** |

What is measured is narrowed by [`src/backend/coverage.settings.xml`](../src/backend/coverage.settings.xml):
our own assemblies only, no test assemblies, no EF migrations, no generated members. A coverage
number is only worth gating on if it moves when our own code changes — left unfiltered,
FluentValidation alone contributes 8,757 branches, none of them ours.

> That settings file has three ways to fail **silently**, all of them documented inside it. The
> most surprising: an XML comment containing a double hyphen (`--`) is illegal XML, so the whole
> file is rejected and default settings are used instead. The tool logs
> `Coverage settings file is not a valid file` at warning level and reports coverage anyway. If a
> filter ever appears to do nothing, re-run with `--log-level Verbose --log-file <path>` and read
> the first line.

---

## 5. What runs on GitHub

[`.github/workflows/ci.yml`](../.github/workflows/ci.yml) — on every pull request to `main` or
`develop`, every push to those branches, and on merge queues.

| Job | Runner | Contains |
|---|---|---|
| **Backend** | ubuntu-latest | restore, build, format, **the API-client codegen gate**, all three suites with coverage; uploads TRX + cobertura; posts a coverage table to the run summary |
| **Frontend** | ubuntu-latest | `npm ci`, prettier, ESLint, unit tests, both production builds |
| **Security** | ubuntu-latest | npm + NuGet advisories, and a **gitleaks scan of full history** |
| **Container images** | ubuntu-latest | builds the api and migrator images, Trivy scan → SARIF to the Security tab, then fails on high/critical |
| **CI** | ubuntu-latest | the single aggregate status check |

Everything runs on **ubuntu-latest**: it is what the VPS runs, it is where the chiseled images are
built, and Testcontainers gets a native Docker daemon rather than a virtualised one.

**Deployment is not here.** Staging and production deploys are Step 32's, and they need a VPS, a
registry and secrets that do not exist yet. This workflow stops at a verified, scanned image; the
images are built but not pushed.

### The `CI` aggregate job

Branch protection is configured against check *names*, and a matrix job's names change whenever the
matrix does. The `CI` job's name never changes, so the rule is configured once and stays correct.
It requires every other job to be `success` — not merely "not failed", because a cancelled or
skipped required job is not a pass either, and treating it as one is how a green tick comes to mean
nothing.

---

## 6. Making it block a merge

**This is the one part of Step 5 that cannot be done from the repository**, because the repository
has no GitHub remote yet (`git remote -v` is empty). The workflow is valid — it is linted by
`actionlint` with zero findings and every gate it invokes has been run locally — but nothing can
*block* a merge until there is a remote with a protection rule on it.

Once the remote exists, configure this once, in **Settings → Rules → Rulesets**:

**Ruleset: `main` and `develop`**

- Target branches: `main`, `develop`
- ☑ Require a pull request before merging
  - Required approvals: **1** (anything touching money, stock, auth or personal data needs a
    reviewer who did not write it — `CONTRIBUTING.md`)
  - ☑ Dismiss stale approvals when new commits are pushed
- ☑ Require status checks to pass
  - ☑ Require branches to be up to date before merging
  - Required check: **`CI`** — the aggregate job, and the only one that needs naming
- ☑ Block force pushes
- ☑ Restrict deletions

**Also enable, in Settings → Code security:**

- Dependabot alerts and security updates (the schedule itself is in
  [`.github/dependabot.yml`](../.github/dependabot.yml))
- Secret scanning and push protection — GitHub's own, in front of the gitleaks job, so a
  credential is rejected at push time rather than found afterwards
- Code scanning: nothing to configure, the Trivy SARIF upload populates it

Verify with a throwaway PR that deliberately breaks one gate (a stray space in a `.cs` file is
enough) and confirm the merge button is disabled.

---

## 7. Reading a failure

| Symptom | Cause | Fix |
|---|---|---|
| `dotnet format found unformatted C#` | formatting, or an `.editorconfig` style rule | `cd src/backend && dotnet format` |
| Hundreds of `ENDOFLINE` errors, only on Windows | Stale CRLF in the working tree. `.gitattributes` normalises `*.cs` to LF in the repository, but a file written with CRLF and then committed keeps CRLF on disk — the commit is clean, the file is not | `git ls-files -z '*.cs' \| xargs -0 rm -f && git checkout -- '*.cs'` |
| `prettier found unformatted files` | generated or hand-edited frontend files | `cd src/frontend && npx prettier --write .` |
| `the committed API client has drifted` | somebody changed an endpoint, a DTO or a validation attribute and did not regenerate the client. This is the gate doing its job — the client is generated, never edited | `pwsh tools/generate-api-client.ps1`, then commit the regenerated files. Review that diff: it is the record of what the API change did to every consumer |
| A suite `reported only N tests, below the floor` | either the suite genuinely shrank, or it did not run | Check the count. If a step legitimately removed tests, lower the floor **in the same PR, with the reason in the message** |
| `No Docker daemon` in the test stage | Docker Desktop is not running | Start it, or `--skip-integration` and accept that coverage is not judged |
| `Line coverage X% is below the agreed minimum` | new code without tests | Add the tests. The threshold is a specification commitment, not a preference |
| npm or Nx output is interleaved with debugger chatter | `NODE_OPTIONS` carries a VS Code bootloader | Already cleared by `ci.ps1` and the workflow. If you hit it in your own shell: `NODE_OPTIONS= npx ...` |
| `Coverage settings file is not a valid file` (warning) | invalid XML in `coverage.settings.xml`, most likely a `--` inside a comment | Fix the XML. Until then, coverage is being measured with default filters |
| Trivy fails on a CVE with no fix available | it should not — the scan runs with `--ignore-unfixed` | If it does, the advisory now has a fix: take the base-image update Dependabot raised |

---

## 8. Deliberately not here

Recorded so their absence is not mistaken for an oversight.

| Not present | Why | Owner |
|---|---|---|
| Deploy to staging / production | No VPS, registry or secrets yet | **Step 32** |
| Image push to a registry | Same | **Step 32** |
| OpenAPI diff + generated client regeneration check | There is no generated client yet | **Step 22** |
| E2E (Playwright) and visual regression | No storefront to drive; visual baselines are pointless before the design exists | **Steps 23–25**, visual at **Step 30** |
| k6 load tests | Needs a baseline to compare against | **Step 29** |
| DAST (ZAP) | Needs a deployed environment | **Step 32** |
| A frontend coverage threshold | The libraries are placeholders; a number set now would be meaningless | **Step 22** |
| CodeQL / SAST | Not in the Step 5 deliverables; `.NET` analyzers and ESLint cover static analysis for now | Raised in the Parking Lot |
