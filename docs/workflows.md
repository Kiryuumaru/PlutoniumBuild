# Generating CI workflows

Describe your steps once in C#, then generate the CI files from those definitions — the files cannot drift out of sync with your build.

## Regenerate the files

```text
mybuild shell github-workflow
mybuild shell azure-workflow
```

Run these after changing your steps. Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:100-101`, `src/Plutonium.Shell/PlutoniumVerbCommands.cs:103-133`.

Entry ids name the generated entry jobs. Every final id — including one set through `WorkflowId` or `Id` — must use ASCII letters, digits, `_`, or `-`, starting with a letter or `_`; an invalid id fails regeneration with an error naming it. Regenerate existing workflow files after upgrading; no compatibility path is provided.

Secrets are part of the generated files too. On GitHub, declared secrets are job-scoped: a secret with no `EntryIds` goes to every entry job, and a secret with `EntryIds` goes only to the named entries. Pre-setup and post-setup receive every declared secret. The workflow has no workflow-level secret `env` block, and only pre-setup and post-setup get `GITHUB_TOKEN` in their job environment and `contents: write`. Regenerate after adding, renaming, re-keying, or rescoping a secret. See [Secrets](secrets.md). Sources: `src/Plutonium.Helpers/Pipelines/PlutoniumGitHubPipeline.cs:176-188,202-245,383-446,465-486`, `src/Plutonium.Helpers/App/PlutoniumSecretVariableAttribute.cs:49-53`.

| Command | File it writes |
| ------- | -------------- |
| `shell github-workflow` | `.github/workflows/<name>.yml` under your repository root |
| `shell azure-workflow` | `azure-pipelines.yml` at your repository root |

Sources: `src/Plutonium.Core/Workflows/Emitters/PlutoniumGitHubEmitter.cs:191-229` (writes under `.github/workflows`), `src/Plutonium.Core/Workflows/Emitters/PlutoniumAzureEmitter.cs:203-232` (writes `azure-pipelines.yml`), `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:437-452` (which pipeline builds which file).

The GitHub file name is a slug of your `WorkflowConfig` name: trimmed, lowercased, spaces become hyphens, and the result must start with a letter or digit and use only lowercase letters, digits, and hyphens. A name that cannot form that slug — a dot, underscore, slash, backslash, absolute path, or empty name — fails regeneration before a file is written. The YAML `name:` keeps your display name. The runner validates the slug, and the emitter rejects any path that resolves outside `.github/workflows`. (GitHub only — the Azure output is always `azure-pipelines.yml`.) Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:455-475`, `src/Plutonium.Core/Workflows/Emitters/PlutoniumGitHubEmitter.cs:213-224`, `src/Plutonium.Helpers/Pipelines/PlutoniumGitHubPipeline.cs:173`. Only GitHub Actions and Azure Pipelines are supported in 0.1.0.

## What the generated files do

Each generated file mirrors your entries: one entry job (and, on Azure, one stage) per entry, plus pre-setup and post-setup jobs. Each entry job's run step is gated by the same condition your build defines. The pre-setup job resolves versions for the run. Push the files with your repo; CI runs the same entries as `shell Run`, one entry per job:

Generated workflows trigger on pushes to your environment branches, `bump-*` tags, and pull requests. `Bump` pushes the current commit, writes each app's `<app>/<version>-bump` tag, and force-moves `bump-<branch>`; that trigger starts the release run.

```text
mybuild shell Run      # everything, on your machine
mybuild shell Test     # test steps only
mybuild shell Build    # build steps only
mybuild shell Publish  # publish steps only
```

In a scaffolded repository, use `./build.sh Run` and so on; the root wrapper adds `shell` for you.

Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:96-99` (local verbs), `src/Plutonium.Helpers/Pipelines/PlutoniumGitHubPipeline.cs:383-487` and `src/Plutonium.Helpers/Pipelines/PlutoniumAzurePipeline.cs:321-418` (entry and setup jobs), `src/Plutonium.Helpers/Pipelines/PlutoniumPipelineDetection.cs:260-299` (triggers), `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:237-270` (`Bump` pushes).

## Generated workflow security

GitHub workflows set top-level `permissions: contents: read`. Only the pre-setup and post-setup jobs add `permissions: contents: write`; those jobs also get `GITHUB_TOKEN` and every declared secret in their job environment. Entry jobs receive only the declared secrets that apply to them and check out with `persist-credentials: false`, so later entry steps cannot reuse the checkout token. The pre-setup and post-setup checkouts keep credentials so they can push tags and manage the release.

Every GitHub action step is pinned to a 40-character commit SHA with a version comment: `actions/checkout`, `actions/cache`, `actions/upload-artifact`, and `actions/download-artifact`. Azure uses native `checkout` and `publish` steps and keeps its cache task at `Cache@2`. Sources: `src/Plutonium.Helpers/Pipelines/PlutoniumGitHubPipeline.cs:383-446,465-486`, `src/Plutonium.Core/Workflows/Emitters/PlutoniumGitHubEmitter.cs:23-29`, `src/Plutonium.Core/Workflows/Emitters/PlutoniumAzureEmitter.cs:45-52`.

## Release results

The post-setup job runs after the entry jobs, including after a failed entry job. It reads one normalized result per run entry from `PLUTONIUM_RUN_RESULT_<ENTRY>`. GitHub fills `PLUTONIUM_RUN_RESULT_GITHUB_<ENTRY>` from `needs.<ENTRY>.result`; Azure fills `PLUTONIUM_RUN_RESULT_AZURE_<ENTRY>` from `stageDependencies.<STAGE>.<STAGE>.result` for the stage generated from that entry. Post-setup runs after an entry failure on both hosts: GitHub uses `success() || failure() || always()`, Azure uses `or(succeeded(), failed())`.

The generated workflows normalize the raw host values. GitHub `failure` and `cancelled` become `error`; Azure `SucceededWithIssues`, `Failed`, and `Canceled` become `error`; `Succeeded` becomes `success`, `Skipped` becomes `skipped`, and an empty result stays empty.

Any entry result of `error` fails the run before publishing. On a release run, post-setup stamps `<app>/<version>-failed` and `<app>/<version>`, deletes the draft release, and throws. On a release run with no `error` result, post-setup stamps `<app>/<version>-passed`, `<app>/<version>`, and `<app>/latest` or `<app>/latest-<env>`, then publishes the draft. See [Release outcomes](versioning.md#release-outcomes).

Sources: `src/Plutonium.Core/Workflows/PlutoniumPipelineContract.cs:30-52`, `src/Plutonium.Helpers/Pipelines/PlutoniumGitHubPipeline.cs:135-148,465-486`, `src/Plutonium.Helpers/Pipelines/PlutoniumAzurePipeline.cs:127-154,400-417`, `src/Plutonium.Helpers/Pipelines/PlutoniumPreSetup.cs:164-200,584-622`.

## Runner scripts and scaffold

The generated CI files invoke `./build.sh` on Linux and `./build.cmd` on Windows. In a scaffolded repository those root files are thin managed wrappers; they forward into `build/build.sh` or `build/build.ps1`, which build `build/build.csproj` and run the build app.

Do not edit the launchers or point them at a project. The scaffold keeps `build.csproj` in `build/`, and `build/update.*` owns the launchers. Edit `build/build.cs` for your Test/Build/Publish steps.

The launcher resolves the SDK pinned in `build/global.json` (`10.0.402`, `latestPatch`) and installs it under `build/temp` when no usable `dotnet` is on `PATH` or in the cache. Before probing, the bootstrap clears host and restore variables that can redirect the toolchain, and it requires `sdk.version`, `sdk.rollForward`, and `sdk.allowPrerelease` in `global.json`; unsupported values stop the launcher. `build/plutonium.lock` pins the installer and SDK archive hashes. `build/.gitignore` keeps `temp/`, `bin/`, and `obj/` out of git.

Install the scaffold once with `init.sh` or `init.ps1`. Refresh managed files and the package pin with `build/update.sh --sha <commit>` or `build/update.ps1 -Sha <commit>`. The updater preserves `build/build.cs`, `build/Directory.Build.props`, `build/Directory.Build.targets`, and `build/build.slnx`. See the [README](../README.md) for the installer command and the `<release-sha>` prerequisite.

The installer and updater enforce a source contract before writing. A source URL may not contain credentials (userinfo, query, or fragment). Every source payload file except the lock itself must be tagged `managed` or `preserve` in the lock, and the lock's package version must match `src/Directory.Build.props` and `scaffold/build/build.csproj`. Symlinked or non-regular payload files are refused, and `init.ps1` also refuses a reparse-point `build/` or root wrapper destination. An update journals every affected path under `build/temp/update/<stamp>`; if apply or the smoke build fails, it restores replaced files and removes files introduced by the new lock. Bash smoke scripts cover credential rejection, lock completeness, preserve drift, and rollback, and the PowerShell installer/updater flow runs on Ubuntu with `-SkipBuild` and the full build flow on Windows.

Keep `*.sh` and `*.cmd` at LF line endings if your repository uses `core.autocrlf`; add `*.sh text eol=lf` and `*.cmd text eol=lf` to your `.gitattributes`.

Sources: `src/Plutonium.Helpers/Pipelines/RunnerOS/PlutoniumRunnerOS.cs:95-148` (generated run scripts); `scaffold/root/build.sh`, `scaffold/root/build.ps1`, `scaffold/root/build.cmd` (root wrappers); `scaffold/build/build.sh:11-26`, `scaffold/build/build.ps1:15-40` (bootstrap, build, and run); `scaffold/build/Program.cs:1-22` (options and shell prefix); `scaffold/build/update.sh:147-157,200-263,317-335,375-382,631-671` (source URL, version invariant, source contract, payload refusal, and rollback); `init.sh:59-74,180-243,278-291` and `init.ps1:31-52,134-190,256-263,352-367` (source contract); `scaffold/lock.sh:117-234` (lock completeness and version invariant); `scaffold/build/bootstrap.sh:17-23,123-141` (environment and SDK settings); `.github/workflows/ci.yml:24-96` (smoke and build/test jobs).

## Leave the CI-internal steps alone

The generated files call `shell pipeline-pre-setup`, `shell pipeline-post-setup`, and `shell pipeline-entry-run` themselves. Each entry job runs one entry, selected by the `PLUTONIUM_ENTRY` job variable. The `pipeline-entry-run` verb rebuilds that entry from the pre-setup payload. You never run these by hand — pre-setup resolves versions, and post-setup aggregates entry results and stamps the release outcome. Sources: `src/Plutonium.Helpers/Pipelines/PlutoniumPipelineInvocation.cs:14-20` (command tokens), `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:102-104,468-581`, `src/Plutonium.Shell/PlutoniumVerbCommands.cs:135-181`, `src/Plutonium.Helpers/Pipelines/PlutoniumGitHubPipeline.cs:347-369`, `src/Plutonium.Helpers/Pipelines/PlutoniumAzurePipeline.cs:362-381`.
