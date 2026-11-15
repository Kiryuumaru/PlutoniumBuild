# AGENTS.md — PlutoniumBuild Agent Rules

> Status: Phase-B plus distribution landed. `src/Plutonium.Core`,
> `src/Plutonium.Helpers`, `src/Plutonium.Shell`, and the single
> `PlutoniumBuild` package exist with landed code; `tests/` landed;
> `scaffold/`, `init.sh`, and `init.ps1` landed; `samples/` remains
> **planned**. The repository is public and 0.1.0 is published on nuget.org;
> nuget.org publishing is manual and confirm-gated. Versioning behavior is
> landed and tested: tag and `versions.json` modes, outcome stamping,
> run-result gating, and `StatusWatch`. Workflow security and scaffold
> hardening are landed and tested: GitHub read-only defaults and job-scoped
> secrets, pinned actions, Azure stage-result gating, lock/version invariants,
> reparse refusal, update rollback, and PowerShell smoke on Ubuntu (skip
> build) and Windows CI.
> Pins: `global.json` SDK `10.0.402` (`latestPatch`, `allowPrerelease: false`)
> / `src/Directory.Build.props` version `0.1.0`. The package targets `net10.0`;
> the three libraries stay `net8.0` / `net9.0` / `net10.0` for tests and are
> not packable — do not invent release status.

On conflict, **Section 9 Safe-agent overrides win**. On violation, fail fast
and state it.

## 1. Project map

```text
/
├── AGENTS.md               # this file — agent working agreements
├── README.md               # consumer how-to (package, scaffold, CLI)
├── INIT.md                 # agent entry point (install, run, CI, update)
├── PlutoniumBuild.sln     # solution (Phase-B plus distribution landed)
├── global.json             # SDK pin only: 10.0.402, latestPatch, no prerelease
├── build.sh                # maintainer runner: bootstrap SDK, build + test (bash)
├── build.ps1               # maintainer runner: same, in Windows PowerShell
├── build.cmd               # maintainer runner: Windows CMD wrapper for build.ps1
├── init.sh                 # consumer scaffold installer (bash, default branch, optional --ref/--sha)
├── init.ps1                # consumer scaffold installer (Windows PowerShell 5.1)
├── .gitattributes          # LF guards for *.sh / *.cmd
├── LICENSE                 # MIT
├── scaffold/
│   ├── build/              # consumer build app payload: build.cs, Program.cs,
│   │                       # build.csproj (package ref), lock, launchers/updaters
│   ├── root/               # thin root build.sh / build.ps1 / build.cmd wrappers
│   ├── smoke/              # bash + PowerShell scaffold safety smokes
│   └── lock.sh             # verify/write scaffold/build/plutonium.lock
├── src/
│   ├── CONCEPT.md          # 1-page recreate intent
│   ├── Directory.Build.props # version/LangVersion/TFM settings; IsPackable=false
│   ├── Plutonium.Core/     # LANDED — target runner, logging, path, process, workflows
│   ├── Plutonium.Helpers/  # LANDED — entries, AppId, run contexts, pipelines
│   ├── Plutonium.Shell/    # LANDED — CLI/app shell on ApplicationBuilderHelpers 4.1.160
│   └── PlutoniumBuild.Package/ # LANDED — the single packable net10.0 NuGet package
├── tests/                  # LANDED — Core / Helpers / Shell / Packaging / Scaffold / Sdk
├── samples/                # PLANNED
├── .github/workflows/      # ci.yml + publish.yml (manual, main-only OIDC publish)
├── docs/
│   ├── index.md            # user docs hub (install/CLI/versioning/workflows)
│   ├── cli.md              # CLI reference (verbs, options, exit codes)
│   ├── versioning.md       # versioning how-to (builder vs app versions, Bump,
│   │                       # tags, outcome stamps, versions.json)
│   ├── workflows.md        # CI-generation how-to (GitHub/Azure emitters, scaffold runners)
│   ├── terminal-calls.md   # how-to: run any external tool from a step
│   ├── secrets.md          # how-to: declare/scope secrets, env keys, CI exposure
│   ├── package-readme.md   # packed as the NuGet package README
│   ├── dev/                # developer docs (packages.md, architecture.md)
│   ├── adr/                # architecture decision records
│   ├── agents/             # workflow config (issue-tracker, triage-labels, domain)
│   └── concepts/
│       └── scope.md        # AUTHORITATIVE in/out scope for 0.1.0
└── .inspirations/
    ├── NukeBuildHelpers/   # read-only audit ref (main @ 5c17b96)
    └── NukeBuild/          # read-only audit ref (develop @ 27e8077)
```

## 2. Scope / doc authority

- Authoritative scope: `docs/concepts/scope.md`. It defines what is in
  (target runner, entries, AppId versioning, GitHub/Azure workflow gen,
  the single package, one-line scaffold init, and update flow) vs out
  (`Nuke.Common` dependency, copy-paste, future `PlutoniumBuild.*`
  integration packages). Mark open questions there; do not invent scope
  elsewhere.
- `src/CONCEPT.md` states recreate intent (1 page).
- `docs/dev/packages.md` states package contents, the builder version, and
  the single-package dependency rule.
- `docs/package-readme.md` is packed as the NuGet package README: keep its
  links absolute and avoid repo-internal-only references.
- Code is source of truth; docs follow code. Never
  document an unbuilt feature as built.
- Docs changes are docs-only; never edit code, scripts, tests, or package
  metadata to make a doc claim true. Record stale claims for the owner.
- Inspirations under `.inspirations/` are read-only audit references. Audited
  recreation only — no verbatim copy.

## 3. Commands

- `./build.sh` (`./build.cmd` on Windows) — maintainer runner: bootstrap the
  pinned SDK when no usable `dotnet` is on `PATH` (repo-local
  `.plutonium/temp`, disposable), then build + test.
- `dotnet build PlutoniumBuild.sln` — build landed `src/` + `tests/` projects.
- `dotnet test` — run landed `tests/`. Core, Helpers, and Shell tests run on
  `net8.0` / `net9.0` / `net10.0`; Packaging, Scaffold, and Sdk tests run on
  `net10.0`.
- `dotnet pack src/PlutoniumBuild.Package/PlutoniumBuild.Package.csproj
  -c Release -o artifacts` — build the single `PlutoniumBuild` package.
  `dotnet pack` on the solution must still produce one distributable package.
- `bash scaffold/lock.sh verify` — verify the scaffold manifest hashes,
  lock completeness, canonical and unique reserved keys, package-version
  invariant, and SDK pin. Use `write` after changing scaffold files.
- `bash scaffold/smoke/source-credentials.sh`,
  `bash scaffold/smoke/lock-contract.sh`,
  `bash scaffold/smoke/preserve-drift.sh`,
  `bash scaffold/smoke/update-rollback.sh`, and
  `bash scaffold/smoke/ref-resolution.sh` — scaffold safety smokes.
  `pwsh scaffold/smoke/powershell-smoke.ps1` exercises the PowerShell
  installer, updater, and build flow; CI runs it on Ubuntu with `-SkipBuild`
  and runs the full flow on Windows.
- `.github/workflows/publish.yml` — manual nuget.org publish from `main`.
  Dispatch it with `confirm` set to true; the job is skipped otherwise and
  on any ref other than `refs/heads/main`. It packs the package, refuses a
  version that already exists on nuget.org, and pushes with a short-lived
  (about one hour) API key minted by NuGet Trusted Publishing through GitHub
  OIDC (`NuGet/login`); the OIDC token is single-use and no NuGet secret is
  stored. It needs no public repository and no deployment environment.
  Manual and confirm-gated; never run it as part of docs work.

Docs tooling: none yet. Record verification as `Not run (docs-only)` unless
project docs tooling is added.

## 4. Quality gate

Applies to landed code; state as gate, not current status:

- `0 warnings / 0 errors` on build.
- `100%` tests pass.
- Packaging tests assert one distributable package, `net10.0` only, MIT,
  the packed readme, and `ApplicationBuilderHelpers` `4.1.160` as the only
  dependency.
- Repository workflow policy tests assert the CI check names and triggers,
  the computed matrix, the Post Setup result gate, and the manual main-only
  publish confirmation, version-exists guard, and OIDC `NuGet/login` →
  `NUGET_API_KEY` push.
- Workflow security tests assert read-only default permissions, no
  workflow-level secret environment, write permission and the token
  environment only on pre/post setup, entry secret scoping and unknown-id
  failure, entry checkout without persisted credentials, SHA-pinned actions,
  and workflow-name/path containment.
- Scaffold tests assert init refusal, symlink/root-wrapper refusal, plain-name
  and fully qualified ref resolution with ambiguity refusal, lock canonicality
  and source identity, preserved user files, package-pin bumps, update
  idempotence, and the sample Test/Build/Publish flow.
- Scaffold smoke scripts assert credential-bearing source rejection, lock
  completeness, preserve drift, update rollback of newly added files, ref
  resolution and ambiguity refusal, and the PowerShell init/update/build flow
  on Linux (skip build) and Windows.
- Versioning tests assert `versions.json` generation/verification and Bump
  refusal, Bump HEAD/tag/trigger pushes, success and failure stamps, run-result
  failure gating, and watch cancellation/failure.
- `bash scaffold/lock.sh verify` passes after scaffold changes.

## 5. C# standards

- Null-gating: guard public entry boundaries; no unchecked null dereference.
- Primary constructors where idiomatic; keep DI-friendly.
- XML docs on all public cross-layer surface (Core ↔ Helpers ↔ consumers).
- LangVersion / TFM placeholders live in `src/Directory.Build.props`.
- `src/PlutoniumBuild.Package/` carries packaging metadata only; library
  code stays in Core, Helpers, and Shell.

## 6. Commenting

Banned in code and docs unless explicitly requested:

- `TODO`, `HACK`, `FIXME`, `#pragma` (disable/warning) — do not add.
- Explain *why* where non-obvious; never narrate *what* the code obviously does.

## 7. Working agreements

- Tools over memory — verify every claim against the repo / inspirations via
  reads, not recollection.
- Verify before claiming — check the file/line before asserting behavior.
- Docs follow code — once code lands, code wins; fix docs, never silently
  change code to match docs.
- Never commit `investigate/` scratch output; keep the tree clean.
- One file at a time; docs only unless explicitly tasked otherwise.
- No git writes by default (see §9).

## 8. Skills index

- Agent workflow config lives in `docs/agents/` (`issue-tracker.md`,
  `triage-labels.md`, `domain.md`); see `## Agent skills` below.
- No skills index file exists yet in this repo. When one is added (e.g.
  `SKILLS.md` or `docs/skills.md`), point to it here and follow it.
- Until then, this AGENTS.md is the sole agent ruleset plus §9 overrides.

## Agent skills

### Issue tracker

Issues live as GitHub issues via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary (`needs-triage` / `needs-info` / `ready-for-agent` / `ready-for-human` / `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout (`CONTEXT.md` + `docs/adr/` created lazily). See `docs/agents/domain.md`.

## 9. Safe-agent overrides (win on conflict)

These override §§1–8 on any conflict. Violations fail fast with an explicit
statement.

1. **Scout-if-unfamiliar → echo → wait → execute.** If unfamiliar, scout
   read-only first, echo understanding with file/doc refs, wait for explicit
   confirmation (`yes`/`correct`/`go ahead`), then execute. Never self-confirm.
2. **Questions are questions, not actions.** A `?` asks; do not act on it as
   an instruction.
3. **Own mistakes first.** When something fails, check your own steps before
   blaming tools/repo/user; state what you got wrong.
4. **Read project docs before acting.** Read `README.md`, this file,
   `docs/concepts/scope.md`, and relevant `docs/` before modifying anything.
5. **Verify after every action.** Re-read edited files; confirm claims and
   links resolve. Docs builds/link-checks when tooling exists, else record
   `Not run (docs-only)`.
6. **Keep docs in sync.** Every behavior-affecting change updates the
   corresponding doc in the same task; never leave docs stale.
7. **Debug honestly.** Report what you actually observed; do not invent
   passing results or hide failures.
8. **Always respond visibly.** Every turn produces a visible summary;
   no silent no-ops.
9. **No git writes by default.** Read-only git (`status`, `diff`, `log`) is
   allowed. No `commit` / `push` / `amend` / PR writes unless explicitly
   requested.
