# Generating CI workflows

Describe your steps once in C#, then generate the CI files from those definitions — the files cannot drift out of sync with your build.

## Regenerate the files

```text
mybuild shell github-workflow
mybuild shell azure-workflow
```

Run these after changing your steps. Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:100-101`, `src/Plutonium.Shell/PlutoniumVerbCommands.cs:103-133`.

Entry ids name the generated entry jobs. Every final id — including one set through `WorkflowId` or `Id` — must use ASCII letters, digits, `_`, or `-`, starting with a letter or `_`; an invalid id fails regeneration with an error naming it. Regenerate existing workflow files after upgrading; no compatibility path is provided.

Secrets are part of the generated files too: the workflow environment maps each declared secret to `PLUTONIUM_<NAME>` (or its per-secret override). Regenerate after adding, renaming, or re-keying a secret. Files generated before the `PLUTONIUM_` prefix change carry the older `NUKE_<NAME>` key and must be regenerated. See [Secrets](secrets.md). Sources: `src/Plutonium.Helpers/Pipelines/PlutoniumPipelineDetection.cs:171-198`, `src/Plutonium.Helpers/App/PlutoniumSecretDeclaration.cs:36-40`.

| Command | File it writes |
| ------- | -------------- |
| `shell github-workflow` | `.github/workflows/<name>.yml` under your repository root |
| `shell azure-workflow` | `azure-pipelines.yml` at your repository root |

Sources: `src/Plutonium.Core/Workflows/Emitters/PlutoniumGitHubEmitter.cs:191-211` (writes under `.github/workflows`), `src/Plutonium.Core/Workflows/Emitters/PlutoniumAzureEmitter.cs:213-232` (writes `azure-pipelines.yml`), `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:244-265` (which pipeline builds which file).

The GitHub file name comes from your `WorkflowConfig` entry's name (lowercased, spaces become dashes). (GitHub only — the Azure output is always `azure-pipelines.yml`.) Source: `src/Plutonium.Helpers/Pipelines/PlutoniumGitHubPipeline.cs:144,157`, `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:249`, `src/Plutonium.Core/Workflows/Emitters/PlutoniumAzureEmitter.cs:223`. Only GitHub Actions and Azure Pipelines are supported in 0.1.0.

## What the generated files do

Each generated file mirrors your entries: one pipeline job per entry, plus pre-setup and post-setup jobs. Each entry job's run step is gated by the same condition your build defines. The pre-setup job resolves versions for the run. Push the files with your repo; CI runs the same entries as `shell Run`, one entry per job:

```text
mybuild shell Run      # everything, on your machine
mybuild shell Test     # test steps only
mybuild shell Build    # build steps only
mybuild shell Publish  # publish steps only
```

Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:96-99` (local verbs), `src/Plutonium.Helpers/Pipelines/PlutoniumGitHubPipeline.cs:279-377` and `src/Plutonium.Helpers/Pipelines/PlutoniumAzurePipeline.cs:290-381` (entry and setup jobs).

## Runner scripts

The generated CI files invoke `./build.sh` on Linux and `./build.cmd` on Windows, so your repository needs those scripts at its root. PlutoniumBuild ships them as templates — copy all three into your repository root:

- [`templates/build.sh`](../templates/build.sh) — bash runner.
- [`templates/build.ps1`](../templates/build.ps1) — PowerShell runner.
- [`templates/build.cmd`](../templates/build.cmd) — CMD entry that calls `build.ps1`.

Edit the one project line — `APP_PROJECT` in `build.sh`, `$AppProject` in `build.ps1` — to point at your build app project (default `build/_build.csproj`), or set the `PLUTONIUM_APP_PROJECT` environment variable to override it for one-off runs. Keep the executable bit on `build.sh`. Keep LF line endings for the copied scripts — add `*.sh text eol=lf` and `*.cmd text eol=lf` to your `.gitattributes` (the same guard this repository uses).

Each script uses the SDK pinned in your `global.json`, installing it into `.plutonium/temp` when no usable `dotnet` is on `PATH` (the latest STS channel when your repository pins no version). It then builds your build app and runs it, passing every argument through. Recommended: commit a `.plutonium/.gitignore` containing `/temp` so the scratch directory stays out of your repository.

Sources: `src/Plutonium.Helpers/Pipelines/RunnerOS/PlutoniumRunnerOS.cs:95-148` (the run scripts); `templates/build.sh:18`, `templates/build.ps1:21` (the project line).

## Leave the CI-internal steps alone

The generated files call `shell pipeline-pre-setup`, `shell pipeline-post-setup`, and `shell pipeline-entry-run` themselves. Each entry job runs one entry, selected by the `PLUTONIUM_ENTRY` job variable. The `pipeline-entry-run` verb rebuilds that entry from the pre-setup payload. You never run these by hand — pre-setup resolves versions, and post-setup publishes results inside the CI run. Sources: `src/Plutonium.Helpers/Pipelines/PlutoniumPipelineInvocation.cs:14-20` (command tokens), `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:102-104,307-386`, `src/Plutonium.Shell/PlutoniumVerbCommands.cs:135-181`, `src/Plutonium.Helpers/Pipelines/PlutoniumGitHubPipeline.cs:324-345`, `src/Plutonium.Helpers/Pipelines/PlutoniumAzurePipeline.cs:328-349`.
