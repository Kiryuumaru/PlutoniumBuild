# Scope — PlutoniumBuild 0.1.0 (AUTHORITATIVE)

> Developer documentation — for maintainers, not end users.
>
> Status: **authoritative for `0.1.0`**. Freezes the Core vs Helpers
> boundaries plus the package/scaffold distribution boundary for the 0.1.0
> build.
>
> - Decision (confirmed): scope is **everything `NukeBuildHelpers` is, plus polyfill of its
>   `NukeBuild` dependencies**. Audited recreation only — no verbatim copy-paste —
>   with **zero `Nuke.Common` dependency**.
> - Sources (read-only): `.inspirations/NukeBuildHelpers` @ `main 5c17b96`,
>   `.inspirations/NukeBuild` @ `develop 27e8077`; audit maps in
>   `docs/nuke_build_helpers/` (17 pages) and `docs/nuke_build/` (12 pages).
  (Note: these audit maps are local, untracked audit material — read-only
  references used during the recreate, not shipped with the repo.)
> - Status: **Phase-B plus distribution landed** (`src/Plutonium.Core/`,
>   `src/Plutonium.Helpers/`, `src/Plutonium.Shell/`, and
>   `src/PlutoniumBuild.Package/` exist with landed code; `scaffold/`,
>   `init.sh`, and `init.ps1` landed; `tests/` landed, `samples/` remains
>   **planned**). Code is source of truth; fix this doc if code evolves,
>   never silently change code to match docs.
> - Pins: `global.json` SDK `10.0.402` (`latestPatch`,
>   `allowPrerelease: false`), `src/Directory.Build.props` version `0.1.0`.
>   The `PlutoniumBuild` package targets `net10.0`; the three libraries stay
>   `net8.0` / `net9.0` / `net10.0` for tests and set `IsPackable=false`.
> - Dependency direction: Shell → Helpers → Core. Core stays BCL-only,
>   Helpers stays library-only; the `ApplicationBuilderHelpers` (`4.1.160`)
>   reference lives only in `Plutonium.Shell` and in the package project.
>   The package embeds all three libraries and declares no `Plutonium.*`
>   package dependency.

## In scope — `Plutonium.Core` (polyfill of Helpers' Nuke deps)

Standalone runner replacing what Helpers reuses from Nuke directly
(see `docs/nuke_build_helpers/overlap-map.md`):

- Target runner: named targets with dependency DSL — name-from-member, `DependsOn` /
  `Triggers` / `Before` / `After` (incl. `Try*` capability-gated variants),
  `OnlyWhen` static (plan-time) vs dynamic (run-time), `Requires`, `WhenSkipped`,
  failure policies (`ProceedAfterFailure` / `AssuredAfterFailure`), `Unlisted`.
  Sequential default; parallelism via entry fan-out, not shards
  (see `docs/nuke_build/targets-identity.md`, `execution-graph-planner-executor.md`,
  `scheduling-partitioning.md`).
- Plan-then-execute split: topological order, `--skip` replanning, static/dynamic
  conditions, failure/skip semantics, per-target timing and summary
  (see `docs/nuke_build/execution-graph-planner-executor.md`).
- CLI / app shell: owned by `Plutonium.Shell` on `ApplicationBuilderHelpers`
  (`4.1.160`) — 16 verbs plus kebab/legacy aliases, shared `--skip` /
  `--args` / `--list` / `--all`, exit mapping `0` / `2` / `1` / `130`;
  unknown-arg failure with suggestions (see `docs/nuke_build/cli-parameters.md`,
  `docs/nuke_build_helpers/cli-commands.md`).
- Logging / host: level switch, per-target context enricher, console + file sinks,
  templates, CI grouping hooks (see `docs/nuke_build/logging-host.md`).
- Workflow emitters (GitHub + Azure only) + host detection: own emitter pair mirroring
  Helpers' trigger set, output-matrix contract, per-entry fan-out, step injection;
  `Local` fallback when no CI host detected
  (see `docs/nuke_build/ci-generation-adapters.md`,
  `docs/nuke_build_helpers/workflows-github.md`, `workflows-azure.md`,
  `workflows-local.md`).
- Minimal process runner + narrow typed tool surface: env, capture, exit codes, secret masking;
  `dotnet` / `git` / `gh` typed wrappers for the calls Helpers actually invokes
  (`Tool Git` / `Tool Gh`, `ls-remote`, `repo view`, dotnet build/test/publish-style calls),
  plus the terminal façade (`PlutoniumTerminal`, entry-facing via `IRunContext.Terminal`)
  that runs arbitrary external tools with captured, masked output forwarded to the run log
  after completion (stdout Information, stderr Warning; no streaming, no shell)
  (see `docs/nuke_build/tooling-wrappers.md`,
  `docs/nuke_build_helpers/dependencies-stack.md`, `docs/terminal-calls.md`).
- Path / file utilities piecemeal, only what entries need: globbing, zip (assets),
  JSON/YAML, path predicates, hashing (MD5/SHA-1/SHA-256/SHA-512)
  (see `docs/nuke_build/utilities-misc.md`,
  `docs/nuke_build_helpers/caching-artifacts-assets.md`).

## In scope — `Plutonium.Helpers` (all Helpers features)

Every Helpers-facing feature (see `docs/nuke_build_helpers/index.md`):

- Entries DSL: `TestEntry` / `BuildEntry` / `PublishEntry` / `WorkflowConfigEntry`
  delegates, `IRunEntryDefinition` + per-kind interfaces/definitions with documented
  defaults, fluent extensions (sync/async × context/no-context), `AppId` REQUIRED rule,
  `Matrix` expansion, `ExecuteBeforeBuild`, `DisplayName` / `WorkflowId` / `WorkflowBuilder`
  (see `docs/nuke_build_helpers/entries-dsl.md`).
- Run context: `IRunContext` (`PipelineType` + `Apps` map + `Terminal`) / `AppRunContext`
  (`AppId`, `RunType`, `AppVersion`, `BumpVersion` / `PullRequestVersion`, per-app
  `OutputDirectory`), `RunType` flags, bump/PR payload models, local-vs-pipeline setup
  paths (see `docs/nuke_build_helpers/run-context.md`, `docs/terminal-calls.md`).
- Versioning / tags / bump: `{appId}/{SemVersion}` tag scheme, `GetAllVersions` taxonomy,
  semver bump semantics (Major/Minor/Patch/prerelease), branch-bound prereleases,
  `-failed` markers, `bump-*` trigger tags, `--args` bump mini-language,
  `EnvironmentBranches` validation, optional JSON file versioning (`AllVersionsFile`)
  (see `docs/nuke_build_helpers/versioning-tags-bump.md`).
- StatusWatch / version UX: `Version` (fetch + reconcile + per-app tables),
  `Bump` (bump-then-watch to completion), `BumpAndForget` (fire-and-forget),
  `StatusWatch` (attach to in-flight runs), repaint-in-place watch tables
  (see `docs/nuke_build_helpers/statuswatch-version-ux.md`).
- GitHub workflows: `PRE_SETUP` job + per-entry output matrix
  (`CONDITION` / `RUNS_ON` / `RUN_SCRIPT` / `CACHE_*` / `CHECKOUT_*`),
  `PLUTONIUM_PRE_SETUP` (base64 JSON) contract, trigger set (env branches, `bump-*` tags,
  all PRs), pre/post step injection; each entry job runs `shell pipeline-entry-run`
  and selects its entry with `PLUTONIUM_ENTRY`
  (see `docs/nuke_build_helpers/workflows-github.md`).
  - Clarification (Option C narrowing, not a scope break): `PRE_SETUP` output matrix
    stays in scope but membership narrows to genuinely dynamic values (`CONDITION` +
    `BUILD_ID` / `HAS_RELEASE` via new primitive); static `RUNS_ON` / `RUN_SCRIPT` /
    `CACHE_*` / `CHECKOUT_*` stay baked at generation via `EntrySetupMap`, restoring
    every-emitted-output-has-a-producer contract. The narrowed state is now built:
    `BuildPreSetupOutputs` in both pipeline files emits `CONDITION`-only
    (plus the payload output alongside `BUILD_ID` / `HAS_RELEASE` live outputs
    via `ExportJobOutput`), and `FinalizePreSetupAsync` wires them
    via `ExportJobOutput`.
- Azure workflows: same semantics in Azure trigger/stage syntax; only the step-dictionary
  shape differs (`run`/`name`/`id` vs `script`/`name`/`displayName`), and entry jobs
  also run `shell pipeline-entry-run` with `PLUTONIUM_ENTRY`
  (see `docs/nuke_build_helpers/workflows-azure.md`).
- Local workflows: `Run` / `Test` / `Build` / `Publish` direct in-process parity,
  `RunType`-filtered, artifact pack/unpack still active
  (see `docs/nuke_build_helpers/workflows-local.md`).
- Runner OS: `RunnerOS` abstraction (`Name` + pipeline-OS + run-script) with 6 named
  OSes (`UbuntuLatest` / `Ubuntu2204` / `Ubuntu2004` / `WindowsLatest` / `Windows2022` /
  `Windows2019`); entry-level `.RunnerOS(...)` + workflow-config pre/post setup OSes
  (see `docs/nuke_build_helpers/runner-os.md`).
- Caching / artifacts / assets: three stores — entry cache (`CachePath` +
  `CacheInvalidator`), packed artifacts (`___` separator, `$common` pseudo-app),
  release assets (`AddReleaseAsset` + hash append); temp-root layout
  (see `docs/nuke_build_helpers/caching-artifacts-assets.md`).
- Secrets: `[SecretVariable]`-equivalent declarative attribute + reflection scan +
  export/mask at pre/post-setup; generated env keys are `PLUTONIUM_<NAME>` (override
  per secret), and keys colliding with reserved pipeline wiring are rejected at
  generation. An optional `EntryIds` scope limits generated GitHub entry jobs;
  pre/post setup always receive declared secrets, and Azure keeps them as pipeline
  variables (see `docs/nuke_build_helpers/secrets.md`, `docs/secrets.md`).
- CLI commands: author verbs (`Fetch`, `Version`, `Interactive`, `Bump`,
  `BumpAndForget`, `StatusWatch`, `Run`, `Test`, `Build`, `Publish`,
  `GitHubWorkflow` (frozen wire `GithubWorkflow`, legacy name accepted),
  `AzureWorkflow`), pipeline-internal unlisted trio
  (`PipelinePreSetup`, `PipelinePostSetup`, `PipelineEntryRun`), `DeleteOriginTags`
  maintenance verb
  (gated — see Out of scope), default verb `Interactive`.
  Shell routing lives in `Plutonium.Shell` (`PlutoniumVerbCommands.cs`,
  `AddPlutoniumShell` in `PlutoniumShellExtensions.cs`)
  (see `docs/nuke_build_helpers/cli-commands.md`).
- Runner scripts: maintainer root `build.sh` / `build.ps1` / `build.cmd`
  (repo-local SDK bootstrap when no usable `dotnet`, then build + test) and
  the consumer scaffold (`scaffold/build/`, `scaffold/root/`) installed by
  `init.sh` / `init.ps1` and refreshed by `build/update.*`
  (see `docs/workflows.md`).
- Interactive console: TTY-first picker over runnable tasks, app+part prompts with
  `--args` non-interactive fallback, `BuildOption` menu model, `ConsoleTableHelpers`
  snapshot + watch variants (see `docs/nuke_build_helpers/interactive-console.md`).
- Workflow config: `WorkflowConfigEntry` six knobs (`Name`, `PreSetupRunnerOS`,
  `PostSetupRunnerOS`, `AppendReleaseNotesAssetHashes`, `EnablePrereleaseOnRelease`,
  `UseJsonFileVersioning`) with documented defaults
  (see `docs/nuke_build_helpers/entries-dsl.md`).
- Third-party libs to budget (not Nuke): SemVer, YAML emitter, prompt lib
  (see `docs/nuke_build_helpers/dependencies-stack.md`,
  `interactive-console.md`).

## In scope — distribution and scaffold

- Single NuGet package `PlutoniumBuild` in
  `src/PlutoniumBuild.Package/`, targeting `net10.0`, MIT licensed, version
  `0.1.0` from `src/Directory.Build.props`. It packs `Plutonium.Core`,
  `Plutonium.Helpers`, and `Plutonium.Shell` (DLL, XML, PDB) under
  `lib/net10.0` and declares `ApplicationBuilderHelpers` `4.1.160` as its
  only dependency. There are no separate `Plutonium.Core` / `Plutonium.Helpers`
  / `Plutonium.Shell` packages, and no `Plutonium.*` package dependencies
  (see `docs/dev/packages.md`).
- Consumer scaffold in `scaffold/build/` (build app, package pin, lock,
  launchers, updater, SDK bootstrap) plus thin root wrappers in
  `scaffold/root/`. The scaffold build app targets `net10.0` and references
  `PlutoniumBuild` `0.1.0`; `build.cs`, the isolation props/targets, and
  `build.slnx` are user-owned (see `docs/dev/architecture.md`).
- One-line init: `init.sh` / `init.ps1` install the scaffold from a full
  40-character commit SHA. Before writing, they reject credential-bearing
  source URLs, require every payload file except the lock itself to be listed
  as managed or preserve, enforce the lock/props/csproj version invariant, and
  refuse symlinked or non-regular payloads; `init.ps1` also refuses
  reparse-point destinations. They refuse an existing `build/` or conflicting
  root wrappers and never touch the consumer solution. The raw URL and
  nuget.org package are release-gated by a public repository, a release commit,
  and NuGet trusted publishing.
- Update flow: `build/update.sh` / `build/update.ps1` refresh managed files,
  bump the package pin, preserve `build/build.cs` and the user-owned props and
  solution, journal a backup under `build/temp/update/<stamp>`, and roll back
  on a failed smoke build, removing files added by the new lock.
- SDK bootstrap: `scaffold/build/bootstrap.sh` / `.ps1` clear host and restore
  variables that can redirect the toolchain, require `sdk.version`,
  `sdk.rollForward`, and `sdk.allowPrerelease` in `build/global.json`, resolve
  the pinned SDK (`10.0.402`, `latestPatch`), and install it under
  `build/temp`; the maintainer runner uses `.plutonium/temp`.

## Stack

- Package TFM: `net10.0` only.
- Library and test TFMs: `net8.0` / `net9.0` / `net10.0` (matches Helpers
  today — see `docs/nuke_build_helpers/dependencies-stack.md`). `net11` is an
  open question, not included yet.
- SDK pin: `global.json` `10.0.402` (`latestPatch`, `allowPrerelease: false`).
- Version: `0.1.0` per `src/Directory.Build.props`; the scaffold pins the same
  package version in `build/build.csproj`.

## Out of scope (for 0.1.0)

- Any dependency on `Nuke.Common` — PlutoniumBuild stands alone
  (see `docs/dev/packages.md`). The single third-party package,
  `ApplicationBuilderHelpers` (`4.1.160`), lives only in `Plutonium.Shell`
  and in the package project; Core and Helpers stay dependency-free.
- Verbatim copy-paste from `.inspirations/` — audited recreation only.
- `Nuke.Components` shareable-capability interfaces — entries DSL supersedes them
  (see `docs/nuke_build/components.md`,
  `docs/nuke_build_helpers/drop-keep-signals.md`).
- `Nuke.GlobalTool` (`nuke :setup` / `:trigger` / `:secrets` / `:update` /
  `:complete`) and Cake interop — PlutoniumBuild ships its own `init`/`update`
  scaffold instead (see `docs/workflows.md`).
- Solution / SourceGen / ProjectModel (`[Solution]` injection, strongly-typed solution
  generator, MSBuild introspection) — entries use path globs instead
  (see `docs/nuke_build/solution-sourcegen.md`).
- CI adapters beyond GitHub + Azure: AppVeyor, Bamboo, Bitbucket, Bitrise, GitLab,
  Jenkins, SpaceAutomation, TeamCity, TravisCI — 9 of 11 adapters excluded
  (see `docs/nuke_build/ci-generation-adapters.md`).
- Tool wrappers beyond `dotnet` / `git` / `gh` surface: remaining 63 of 66 typed wrappers
  (Docker, MSBuild, NuGet, Npm, PowerShell, SignTool, SonarScanner, VSTest, Xunit,
  notifiers, etc.) are future catalog, not 0.1.0. Generic terminal calls are NOT new typed
  wrappers — `PlutoniumTerminal` runs any external tool through one request-based API
  (see `docs/nuke_build/tooling-wrappers.md`, `docs/terminal-calls.md`).
- `Nuke.Tooling.Generator`, `Nuke.MSBuildTasks`, `Nuke.Utilities.*` wholesale —
  port only the globbing/compression/JSON/YAML/path pieces entries need
  (see `docs/nuke_build/utilities-misc.md`).
- Telemetry / usage reporting — no port without explicit consent design
  (see `docs/nuke_build/utilities-misc.md`,
  `docs/nuke_build_helpers/drop-keep-signals.md`).
- `tasksync.md` agent-protocol scratch, dependabot hygiene YAMLs, placeholder unit test —
  not product (see `docs/nuke_build_helpers/drop-keep-signals.md`,
  `dependencies-stack.md`).
- `DeleteOriginTags` full-wipe (no-args) mode — dangerous maintenance op; the
  full wipe exists only behind `PLUTONIUM_ALLOW_TAG_WIPE=true` and is not part
  of the supported 0.1.0 workflow.
- macOS / container / arm runner variants — absent upstream; defer until demanded.
- Future transports / pipelines beyond GitHub + Azure — reserved for future
  `PlutoniumBuild.*` integration packages (see `docs/dev/packages.md`). No such
  package ships in 0.1.0.

## Direction — adoption beyond this repository

PlutoniumBuild is adoptable by any repository and any stack. Entries are C# steps; any
step can run any external tool as a terminal call, so the repository being built can
use any language or toolchain — the build definition itself stays a small .NET
project. Terminal calls are built (`PlutoniumTerminal` via `IRunContext.Terminal`,
see `docs/terminal-calls.md`).

Distribution is built at 0.1.0:

- One MIT NuGet package, `PlutoniumBuild`, embeds Core, Helpers, and Shell for
  `net10.0`.
- `init.sh` / `init.ps1` install the pinned scaffold, and `build/update.*`
  refreshes it while preserving user-owned files (see `docs/workflows.md`).

Future `PlutoniumBuild.*` integration packages remain separate and unbuilt.

## Open questions

- TFMs: include `net11`? Stays open — the libraries ship `net8/9/10` and the
  package ships `net10` first; decide based on SDK demand.
- API-name compat: keep upstream names (`Nuke*`, `BaseNukeBuildHelpers`,
  `nuke-cicd.yml`) vs rename to Plutonium names? Stays open — no broad renaming
  decision in this freeze. Decided in code: the pre-setup payload variable is
  `PLUTONIUM_PRE_SETUP`; `NUKE_PRE_SETUP` is a legacy inspiration reference only.
  Secret env keys are `PLUTONIUM_<NAME>` (`NUKE_<NAME>` in the inspirations) with a
  per-secret override; a key that collides with reserved pipeline wiring fails
  workflow generation, and old generated workflows must be regenerated
  (see `docs/secrets.md`).
- Logger choice: Serilog-compatible host vs lighter built-in host? Stays open —
  record the decision at implementation time.
- Prompt-table lib choice: Sharprompt (upstream, `3.1.2`) vs equivalent? Stays open —
  budget one prompt lib, no choice made here.
- Audit gaps carried forward (see `docs/nuke_build_helpers/gaps.md`): `BuildWorkflow`
  YAML-emission bodies skimmed, `StartStatusWatch` / `GetAllVersions` polling/grouping
  not line-audited, `TestEntry` / `PublishEntry` consumer docs not diffed
  method-by-method — verify during implementation, do not treat this freeze as a
  line-audit certificate.
