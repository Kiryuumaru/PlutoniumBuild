# Architecture — PlutoniumBuild

> Developer documentation — for maintainers, not end users.
>
> Status: **0.1.0 distribution landed.** `src/Plutonium.Core/`,
> `src/Plutonium.Helpers/`, `src/Plutonium.Shell/`, and
> `src/PlutoniumBuild.Package/` exist with landed code; `scaffold/`,
> `init.sh`, and `init.ps1` exist; `tests/` landed; `samples/` remains
> **planned**. Workflow security and scaffold hardening are landed and
> tested: read-only GitHub defaults, job-scoped secrets, pinned actions,
> Azure stage-result normalization, lock/version invariants, reparse
> refusal, and update rollback.
> Pins: `global.json` SDK `10.0.402` (`latestPatch`,
> `allowPrerelease: false`), `src/Directory.Build.props` version `0.1.0`.
> Package `net10.0`; libraries `net8.0` / `net9.0` / `net10.0`.

Docs follow code: landed code is source of truth. Authoritative split:
`concepts/scope.md`.

## Projects and roles

| Project | Path | Role |
| ------- | ---- | ---- |
| `Plutonium.Core` | `src/Plutonium.Core/` | Standalone engine: target runner with dependency DSL, plan-then-execute, logging with secret masking, process runner (`dotnet` / `git` / `gh` typed surface) plus the terminal façade (`Process/PlutoniumTerminal.cs`) for arbitrary external tools, path utilities, and the GitHub + Azure workflow emitter pair. Owns planning and execution; owns no CLI parsing. |
| `Plutonium.Helpers` | `src/Plutonium.Helpers/` | Author-facing layer: entries DSL (`TestEntry` / `BuildEntry` / `PublishEntry` / `WorkflowConfigEntry` definitions, discovery, extensions, runner), `AppId`, run-context shapes, versioning UX, and the GitHub / Azure / local pipelines. Owns the verb set and `--args` conventions. |
| `Plutonium.Shell` | `src/Plutonium.Shell/` | CLI/app shell on `ApplicationBuilderHelpers` (`4.1.160`): 16 verbs plus kebab/legacy aliases, shared `--skip` / `--args` / `--list` / `--all`, per-invocation state, exit mapping `0` / `2` / `1` / `130`. Entry point: `AddPlutoniumShell`. Routes to Helpers verbs; adds no build semantics. |
| `PlutoniumBuild.Package` | `src/PlutoniumBuild.Package/` | Distribution boundary: packs Core, Helpers, and Shell into the single `PlutoniumBuild` NuGet package. Holds packaging metadata and the collect target; owns no library code. |

## Package boundary

`src/PlutoniumBuild.Package/PlutoniumBuild.Package.csproj` is the only
project with `IsPackable=true`. All three libraries inherit
`IsPackable=false` from `src/Directory.Build.props`.

- The package targets `net10.0` only; the libraries stay multi-target for
  the test matrix.
- `IncludeBuildOutput=false` prevents an empty package assembly. The
  `CollectPlutoniumAssemblies` target adds the three DLLs, XML docs, and
  PDBs under `lib/net10.0`.
- Project references to Core, Helpers, and Shell use `PrivateAssets="all"`,
  so they are not NuGet dependencies. `ApplicationBuilderHelpers` `4.1.160`
  is the only dependency in the manifest.
- `docs/package-readme.md` is packed as `README.md`. Keep its links absolute
  and do not reference repository-internal paths.
- `dotnet pack` on the solution must produce exactly one distributable
  package. `tests/Plutonium.Packaging.Tests` pins that shape.

## Generated workflow security

The GitHub and Azure builders share entry discovery and pipeline invocation,
but they enforce host-specific security contracts:

- GitHub sets workflow-level `permissions: contents: read`. Only `PRE_SETUP`
  and `POST_SETUP` add `contents: write`; those jobs receive the
  `GITHUB_TOKEN` environment variable and every declared secret. Entry jobs
  receive only the declared secrets that apply to them (all unscoped secrets
  plus their `EntryIds` scope) and check out with `persist-credentials: false`.
  The workflow document carries no workflow-level environment.
- `EntryIds` is validated against discovered run entries before emission; an
  unknown id fails generation. Empty `EntryIds` means every entry job. The
  Azure generator does not apply `EntryIds`; its workflow document carries
  the token and declared secrets as pipeline variables.
- GitHub action steps pin full 40-character commit SHAs with version
  comments: `actions/checkout`, `actions/cache`, `actions/upload-artifact`,
  and `actions/download-artifact`. Azure uses native `checkout` and
  `publish` steps and keeps `Cache@2` as a task version.
- The GitHub workflow name becomes a validated file slug (lowercase letters,
  digits, hyphens; letter or digit first). The runner rejects a name that
  cannot form that slug, and the emitter refuses a path outside
  `.github/workflows`. Azure always writes `azure-pipelines.yml`.
- Azure `POST_SETUP` reads each entry stage's result from
  `stageDependencies.<STAGE>.<STAGE>.result` and normalizes `Succeeded`,
  `SucceededWithIssues`, `Failed`, `Canceled`, and `Skipped`. Both hosts run
  post-setup after an entry failure, then gate success stamping and publish
  on the normalized result.

## Scaffold boundary

`scaffold/` is the consumer payload, not a set of copy-in templates:

- `scaffold/build/` is a small `net10.0` build app. `build.csproj`
  references the `PlutoniumBuild` package, `Program.cs` wires
  `AddPlutoniumShell`, and `build.cs` is the user-owned sample with
  Test/Build/Publish terminal calls. `plutonium.lock` records the source
  commit, package version, SDK version, and SHA-256 hashes for managed files.
- `scaffold/root/` holds thin root wrappers `build.sh`, `build.ps1`, and
  `build.cmd`. Generated CI invokes those root names; they forward into
  `build/`.
- `init.sh` / `init.ps1` install the payload from a full 40-character commit
  SHA. They reject source URLs with credentials, query strings, or fragments;
  require every payload file except the lock itself to carry a `managed` or
  `preserve` lock entry; and require the lock's `package_version`,
  `src/Directory.Build.props`, and `scaffold/build/build.csproj` pin to agree.
  They reject symlinked or non-regular payload files, refuse an existing
  `build/` or conflicting root wrappers, stage the install atomically, and
  never touch the consumer solution. `init.ps1` also refuses a reparse-point
  `build/` or root wrapper destination, including a dangling one.
- `build/update.sh` / `build/update.ps1` apply the same source contract,
  refresh managed files from a pinned commit, bump the package pin, and
  preserve user-owned files. Each update journals every affected path under
  `build/temp/update/<stamp>`. If apply or the smoke build fails, it restores
  replaced files and removes files introduced by the new lock.

The repository root runners are separate: `build.sh`, `build.ps1`, and
`build.cmd` here are maintainer runners that build `PlutoniumBuild.sln` and
run every test. In a consumer repository, the same root names are managed
thin wrappers into `build/`.

### Managed vs user-owned files

| Path | Owner | Update behavior |
| ---- | ----- | --------------- |
| `build/build.cs` | User | Preserved |
| `build/Directory.Build.props` | User | Preserved |
| `build/Directory.Build.targets` | User | Preserved |
| `build/build.slnx` | User | Preserved; the lock records its hash |
| `build/build.csproj` | Managed | Replaced; package version bumped to the lock's `package_version` |
| `build/Program.cs` | Managed | Replaced |
| `build/global.json` | Managed | Replaced |
| `build/plutonium.lock` | Managed | Replaced; consumer source kept, commit, package version, and hashes updated |
| `build/build.sh/.ps1/.cmd` | Managed | Replaced |
| `build/update.sh/.ps1/.cmd` | Managed | Replaced |
| `build/bootstrap.sh/.ps1` | Managed | Replaced |
| `build/.gitignore` | Managed | Replaced |
| root `build.sh/.ps1/.cmd` | Managed | Thin wrappers into `build/` |

## SDK bootstrap and update flow

`scaffold/build/bootstrap.sh` and `.ps1` are the single verified bootstrap
shared by the scaffold and the maintainer runner.

1. Clear executable-affecting host and restore variables before probing or
   installing: `DOTNET_STARTUP_HOOKS`, `DOTNET_ADDITIONAL_DEPS`,
   `DOTNET_SHARED_STORE`, `NUGET_PACKAGES`, `NUGET_HTTP_CACHE_PATH`,
   `DOTNET_HOST_PATH`, `DOTNET_ROOT`, and `DOTNET_ROLL_FORWARD`.
2. Require `global.json` to set `sdk.version`, `sdk.rollForward`, and
   `sdk.allowPrerelease`; reject unsupported roll-forward values, a
   non-boolean prerelease flag, and a prerelease version with
   `allowPrerelease: false`.
3. Use a usable `dotnet` on `PATH` or in the cache when its reported version
   satisfies the pin under those settings; otherwise download the pinned
   installer and SDK archive.
4. Verify the installer SHA-256 and the archive SHA-512 from
   `plutonium.lock` before execution.
5. Install under the caller's cache directory: `build/temp` for a consumer,
   `.plutonium/temp` for this repository.
6. Never install globally and never leave `PATH` changed outside the
   process.

`init` and `update` both fetch a full commit SHA, check out exactly that
commit, verify the payload against the lock, and reject symlinks or
non-regular files. `update` keeps the consumer's source identity in the lock
and refreshes the commit, manifest, package version, and file hashes.

## Verification

- `bash scaffold/lock.sh verify` checks lock completeness, managed-file hashes,
  the package-version invariant, and the SDK pin.
- Bash smoke scripts cover credential-bearing source URLs, unlisted payload
  files, preserve drift, and update rollback.
- `scaffold/smoke/powershell-smoke.ps1` exercises `init.ps1`, `update.ps1`,
  and `build.ps1` in child PowerShell processes. Windows CI runs the full
  flow; Linux CI parses every PowerShell file and runs the smoke with
  `-SkipBuild`.
- CI also runs the full build and test matrix on Linux and Windows.

## Dependency rules

Dependency direction: Shell → Helpers → Core.

- `Plutonium.Core` must not depend on `Nuke.Common`. Core and Helpers stay
  dependency-free (BCL only); the single third-party package,
  `ApplicationBuilderHelpers` (`4.1.160`), lives in `Plutonium.Shell` and in
  the package project.
- Verified in the project files: `Plutonium.Helpers.csproj` carries a single
  `ProjectReference` to Core; `Plutonium.Shell.csproj` carries the one
  `PackageReference` plus `ProjectReference`s to Core and Helpers.
  `PlutoniumBuild.Package.csproj` references all three with
  `PrivateAssets="all"` and declares the same `ApplicationBuilderHelpers`
  package.

## Layout

```text
src/
├── CONCEPT.md              # 1-page recreate intent
├── Directory.Build.props   # version 0.1.0 / LangVersion / TFM / IsPackable=false
├── Plutonium.Core/         # Runner/ Logging/ FileSystem/ Process/ Hosting/ Workflows/
├── Plutonium.Helpers/      # App/ Entries/ Runtime/ Pipelines/ Versioning/
├── Plutonium.Shell/        # PlutoniumShellExtensions.cs (AddPlutoniumShell),
│                           # PlutoniumShellState.cs, PlutoniumShellCommand.cs,
│                           # PlutoniumVerbCommands.cs, PlutoniumInteractiveCommand.cs,
│                           # PlutoniumBumpArg.cs
└── PlutoniumBuild.Package/ # PlutoniumBuild.Package.csproj (net10.0 package)
scaffold/
├── build/                  # consumer build app, lock, launchers, updaters, bootstrap
├── root/                   # thin root wrappers
├── smoke/                  # bash + PowerShell scaffold safety smokes
└── lock.sh                 # verify/write scaffold/build/plutonium.lock
init.sh / init.ps1          # consumer scaffold installers
tests/                      # Core / Helpers / Shell / Packaging / Scaffold / Sdk
samples/                    # PLANNED — no code yet
```

## Folder notes

- Core `Runner/` (`PlutoniumTargetDefinition.cs` + builder, `PlutoniumRunner.cs`): dependency DSL and the topological plan-then-execute runner. `Process/` (`PlutoniumProcessRunner.cs` request + runner, `PlutoniumProcessResult.cs`, `PlutoniumTerminal.cs`): capture, masking, timeout, and the entry-facing terminal façade. `Workflows/Emitters/` (`PlutoniumGitHubEmitter.cs`, `PlutoniumAzureEmitter.cs`): `Render` / `WriteWorkflow`, `PlutoniumHost.ExportJobOutput` output wiring.
- Helpers `Entries/` (`Definitions/` interfaces + entry definitions, `Discovery/` with `PlutoniumBuildBase` / `PlutoniumEntryDiscovery`, `Extensions/` fluent configuration, `PlutoniumEntryRunner.cs`), `App/` (`PlutoniumAppId.cs`, `PlutoniumBuildVerbs.cs` `Version` / `Bump` / `BumpAndForget` / `StatusWatch`, `PlutoniumSecretVariableAttribute.cs` + `PlutoniumSecrets.cs`), `Pipelines/` (`PlutoniumGitHubPipeline.cs` / `PlutoniumAzurePipeline.cs` / `PlutoniumLocalPipeline.cs`; `PlutoniumPipelineInvocation.cs` composes generated `shell pipeline-pre-setup` / `shell pipeline-post-setup` / `shell pipeline-entry-run` lines, entry jobs select one entry via `PLUTONIUM_ENTRY`, and the unlisted `PipelineEntryRun` verb rebuilds that entry from the pre-setup payload; CONDITION-only live outputs plus payload / `BUILD_ID` / `HAS_RELEASE` via `ExportJobOutput`; legacy full-matrix `BuildEntryExports` is `[Obsolete]`), `Runtime/` (`IRunContext` / `AppRunContext` / `PlutoniumRunContext`; `IRunContext.Terminal` exposes the Core terminal façade), `Versioning/` (`PlutoniumBumpArgs` mini-language, `PlutoniumVersionTags` `{appId}/{SemVersion}` scheme, `PlutoniumVersionsFile` `versions.json` mode).
- Shell: `PlutoniumShellCommand.cs` shared `--skip` / `--args` / `--list` / `--all` on the `shell` group node, `PlutoniumShellState.cs` per-invocation state, `PlutoniumBumpArg.cs` `--args` parser.
- Inspirations under `.inspirations/` (`NukeBuildHelpers` `main @ 5c17b96`, `NukeBuild` `develop @ 27e8077`) are read-only audit references. Audited recreation only — no verbatim copy.
