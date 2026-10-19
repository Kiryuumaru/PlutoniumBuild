# Scope — PlutoniumBuild 0.1.0 (AUTHORITATIVE)

> Developer documentation — for maintainers, not end users.
>
> Status: **authoritative for `0.1.0`**. Freezes Core vs Helpers boundaries for the 0.1.0 build.
>
> - Decision (confirmed): scope is **everything `NukeBuildHelpers` is, plus polyfill of its
>   `NukeBuild` dependencies**. Audited recreation only — no verbatim copy-paste —
>   with **zero `Nuke.Common` dependency**.
> - Sources (read-only): `.inspirations/NukeBuildHelpers` @ `main 5c17b96`,
>   `.inspirations/NukeBuild` @ `develop 27e8077`; audit maps in
>   `docs/nuke_build_helpers/` (17 pages) and `docs/nuke_build/` (12 pages).
  (Note: these audit maps are local, untracked audit material — read-only
  references used during the recreate, not shipped with the repo.)
> - Status: **Phase-B landed** (`src/Plutonium.Core/`,
>   `src/Plutonium.Helpers/`, `src/Plutonium.Shell/` exist with landed code;
>   `tests/` landed, `samples/` remains **planned**). Code is source of truth;
>   fix this doc if code evolves, never silently change code to match docs.
> - Pins: `global.json` SDK `10.0.401`, `src/Directory.Build.props` version `0.1.0`.
> - Dependency direction: Shell → Helpers → Core. Core stays BCL-only,
>   Helpers stays library-only; the `ApplicationBuilderHelpers` (`4.1.160`)
>   reference lives only in `Plutonium.Shell`.

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
- Minimal process runner + narrow tool surface: env, capture, exit codes, secret masking;
  `dotnet` / `git` / `gh` equivalents only — the surface Helpers actually invokes
  (`Tool Git` / `Tool Gh`, `ls-remote`, `repo view`, dotnet build/test/publish-style calls)
  (see `docs/nuke_build/tooling-wrappers.md`,
  `docs/nuke_build_helpers/dependencies-stack.md`).
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
- Run context: `IRunContext` (`PipelineType` + `Apps` map) / `AppRunContext`
  (`AppId`, `RunType`, `AppVersion`, `BumpVersion` / `PullRequestVersion`, per-app
  `OutputDirectory`), `RunType` flags, bump/PR payload models, local-vs-pipeline setup
  paths (see `docs/nuke_build_helpers/run-context.md`).
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
  export/mask at pre/post-setup (see `docs/nuke_build_helpers/secrets.md`).
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
- Runner scripts: committed root `build.sh` / `build.ps1` / `build.cmd`
  (repo-local SDK bootstrap when no usable `dotnet`, then build + test) and
  consumer copy-in templates under `templates/` (see `docs/workflows.md`).
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

## Stack

- TFMs: `net8.0` / `net9.0` / `net10.0` (matches Helpers today —
  see `docs/nuke_build_helpers/dependencies-stack.md`). `net11` is an open question,
  not included yet.
- SDK pin: `global.json` `10.0.401` (`rollForward latestMajor`).
- Version: `0.1.0` per `src/Directory.Build.props`.

## Out of scope (for 0.1.0)

- Any dependency on `Nuke.Common` — PlutoniumBuild stands alone
  (see `docs/dev/packages.md`). The single third-party package,
  `ApplicationBuilderHelpers` (`4.1.160`), lives only in `Plutonium.Shell`;
  Core and Helpers stay dependency-free.
- Verbatim copy-paste from `.inspirations/` — audited recreation only.
- `Nuke.Components` shareable-capability interfaces — entries DSL supersedes them
  (see `docs/nuke_build/components.md`,
  `docs/nuke_build_helpers/drop-keep-signals.md`).
- `Nuke.GlobalTool` (`nuke :setup` / `:trigger` / `:secrets` / `:update` / `:complete`,
  scaffolding, Cake interop) — committed `build.*` scripts only at 0.1.0
  (see `docs/nuke_build/globaltool.md`).
- Solution / SourceGen / ProjectModel (`[Solution]` injection, strongly-typed solution
  generator, MSBuild introspection) — entries use path globs instead
  (see `docs/nuke_build/solution-sourcegen.md`).
- CI adapters beyond GitHub + Azure: AppVeyor, Bamboo, Bitbucket, Bitrise, GitLab,
  Jenkins, SpaceAutomation, TeamCity, TravisCI — 9 of 11 adapters excluded
  (see `docs/nuke_build/ci-generation-adapters.md`).
- Tool wrappers beyond `dotnet` / `git` / `gh` surface: remaining 63 of 66 wrappers
  (Docker, MSBuild, NuGet, Npm, PowerShell, SignTool, SonarScanner, VSTest, Xunit,
  notifiers, etc.) are future catalog, not 0.1.0
  (see `docs/nuke_build/tooling-wrappers.md`).
- `Nuke.Tooling.Generator`, `Nuke.MSBuildTasks`, `Nuke.Utilities.*` wholesale —
  port only the globbing/compression/JSON/YAML/path pieces entries need
  (see `docs/nuke_build/utilities-misc.md`).
- Telemetry / usage reporting — no port without explicit consent design
  (see `docs/nuke_build/utilities-misc.md`,
  `docs/nuke_build_helpers/drop-keep-signals.md`).
- `tasksync.md` agent-protocol scratch, dependabot hygiene YAMLs, placeholder unit test —
  not product (see `docs/nuke_build_helpers/drop-keep-signals.md`,
  `dependencies-stack.md`).
- `DeleteOriginTags` full-wipe (no-args) mode — dangerous maintenance op; gate or drop
  at implementation time, do not ship ungated.
- macOS / container / arm runner variants — absent upstream; defer until demanded.
- Future transports / pipelines beyond GitHub + Azure — reserved per `docs/dev/packages.md`.

## Open questions

- TFMs: include `net11`? Stays open — `net8/9/10` ship first; decide before build
  based on SDK demand.
- API-name compat: keep upstream names (`Nuke*`, `BaseNukeBuildHelpers`,
  `nuke-cicd.yml`) vs rename to Plutonium names? Stays open — no broad renaming
  decision in this freeze. Decided in code: the pre-setup payload variable is
  `PLUTONIUM_PRE_SETUP`; `NUKE_PRE_SETUP` is a legacy inspiration reference only.
- Logger choice: Serilog-compatible host vs lighter built-in host? Stays open —
  record the decision at implementation time.
- Prompt-table lib choice: Sharprompt (upstream, `3.1.2`) vs equivalent? Stays open —
  budget one prompt lib, no choice made here.
- Audit gaps carried forward (see `docs/nuke_build_helpers/gaps.md`): `BuildWorkflow`
  YAML-emission bodies skimmed, `StartStatusWatch` / `GetAllVersions` polling/grouping
  not line-audited, `TestEntry` / `PublishEntry` consumer docs not diffed
  method-by-method — verify during implementation, do not treat this freeze as a
  line-audit certificate.
