# CLI reference

Run your build from the terminal. With no arguments your app opens an interactive picker; otherwise use `shell` plus a verb.

```text
mybuild                                # interactive picker (the default)
mybuild shell Test                     # run your tests locally
mybuild shell Bump --args myapp=minor+1  # release a new version
mybuild shell github-workflow          # regenerate the CI file
```

Add `--list` to any command to preview its steps instead of running them.

## Verbs

Type the command exactly as shown. Some verbs accept a second spelling.

| Command | Alias | What it does |
| ------- | ----- | ------------ |
| (no arguments) | — | Open the interactive menu picker (the default) |
| `shell Fetch` | — | Download the latest commits and tags |
| `shell Version` | — | Show every app's current version |
| `shell Bump` | — | Release a new version, then watch it finish |
| `shell BumpAndForget` | `shell bump-and-forget` | Release without watching |
| `shell StatusWatch` | `shell status-watch` | Watch a release that is already running |
| `shell Run` | — | Run everything on your machine |
| `shell Test` | — | Run the test steps on your machine |
| `shell Build` | — | Run the build steps on your machine |
| `shell Publish` | — | Run the publish steps on your machine |
| `shell github-workflow` | `shell GithubWorkflow` | Write the GitHub Actions file |
| `shell azure-workflow` | `shell AzureWorkflow` | Write the Azure Pipelines file |
| `shell pipeline-pre-setup` | `shell PipelinePreSetup` | Generated CI runs this; do not call it yourself |
| `shell pipeline-post-setup` | `shell PipelinePostSetup` | Generated CI runs this; do not call it yourself |
| `shell pipeline-entry-run` | `shell PipelineEntryRun` | Generated CI runs this; one entry per job, selected by `PLUTONIUM_ENTRY` |
| `shell delete-origin-tags --args app` | `shell DeleteOriginTags` | Delete an app's version tags |

Sources: `src/Plutonium.Shell/PlutoniumVerbCommands.cs:15-197`, `src/Plutonium.Shell/PlutoniumInteractiveCommand.cs:12`, `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:32-78`.

## Shared options

Every command accepts the same options.

| Option | What it does |
| ------ | ------------ |
| `--skip <step>` | Skip a step. Repeat to skip more than one. Empty values are rejected. |
| `--args <value>` | Pass `app=expression` to `Bump` or `BumpAndForget`, or an app name to `delete-origin-tags`. Repeat per app. |
| `--list` | Show steps instead of running them. |
| `--all` | With `--list`, also show the unlisted steps (the CI-internal trio plus the tag-wipe maintenance verb). |

Sources: `src/Plutonium.Shell/PlutoniumShellCommand.cs:15-29` (option definitions), `src/Plutonium.Shell/PlutoniumShellCommand.cs:38-51` (empty `--skip` rejected; `--list` prints targets; `--all` includes unlisted).

For `Bump` and `BumpAndForget`, `--args` takes `app=expression` (for example `myapp=minor+1`) or, when your build has exactly one app, a bare expression. Bad values fail before anything runs. Source: `src/Plutonium.Shell/PlutoniumBumpArg.cs:24-53`, `src/Plutonium.Shell/PlutoniumShellState.cs:121-152`. The bump grammar is `major` / `minor` / `patch` / `prerelease` (or `pre`), with `+` to step up or `>` to set a value — see [Versioning](versioning.md).

## Exit codes

| Code | Meaning |
| ---- | ------- |
| `0` | Done. |
| `2` | Wrong usage: unknown command or option, bad `--args`, interactive with no terminal. |
| `1` | Something failed while running. |
| `130` | Cancelled. |

Sources: `src/Plutonium.Shell/PlutoniumShellCommand.cs:42,57,68` (codes `2` and `1`); `130` is the cancel code from the underlying `ApplicationBuilderHelpers` host.

## Tag safety

`delete-origin-tags` without `--args` refuses unless `PLUTONIUM_ALLOW_TAG_WIPE=true` is set — a full wipe never happens by accident. Source: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:78,401-411`.
