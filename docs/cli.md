# CLI reference

Run your build from the terminal. With no arguments your app opens an interactive picker; otherwise use `shell` plus a verb.

```text
mybuild                                # interactive picker (the default)
mybuild shell Test                     # run your tests locally
mybuild shell Bump --args myapp=minor+1  # release a new version
mybuild shell github-workflow          # regenerate the CI file
```

Add `--list` to any command to list the available verbs and targets instead of running them.

In a scaffolded repository, the managed `build/Program.cs` prepends `shell` when the first argument is a verb; the root `build.sh` / `build.ps1` / `build.cmd` wrappers only forward arguments. `./build.sh Test` therefore runs the same command as `./build.sh shell Test`.

## Verbs

Type the command exactly as shown. Some verbs accept a second spelling.

| Command | Alias | What it does |
| ------- | ----- | ------------ |
| (no arguments) | — | Open the interactive menu picker (the default) |
| `shell Fetch` | — | Download the latest commits and tags |
| `shell Version` | — | Show every app's current version |
| `shell Bump` | — | Release a new version, then watch it finish; refused in `versions.json` mode |
| `shell BumpAndForget` | `shell bump-and-forget` | Release without watching; refused in `versions.json` mode |
| `shell StatusWatch` | `shell status-watch` | Watch a running release until canceled |
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
| `--list` | List the available verbs and targets instead of running them. |
| `--all` | With `--list`, also list the unlisted targets (the CI-internal trio plus the tag-wipe maintenance verb). |

Sources: `src/Plutonium.Shell/PlutoniumShellCommand.cs:15-29` (option definitions), `src/Plutonium.Shell/PlutoniumShellCommand.cs:38-51` (empty `--skip` rejected; `--list` prints targets; `--all` includes unlisted).

For `Bump` and `BumpAndForget`, `--args` takes `app=expression` (for example `myapp=minor+1`) or, when your build has exactly one app, a bare expression. Bad values fail before anything runs. Source: `src/Plutonium.Shell/PlutoniumBumpArg.cs:24-53`, `src/Plutonium.Shell/PlutoniumShellState.cs:121-152`. The bump grammar is `major` / `minor` / `patch` / `prerelease` (or `pre`), with `+` to step up or `>` to set a value — see [Versioning](versioning.md).

## Release flow

`Bump` validates the bump, creates `<app>/<version>-bump` tags, pushes the current commit and those tags, then force-moves and force-pushes `bump-<branch>`. It watches the apps it bumped. The watch stops when they all have a `-passed` stamp and exits `1` when one has a `-failed` stamp. `BumpAndForget` does the same pushes without watching. Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:196-199,237-270,314-428`.

`StatusWatch` attaches to the current branch and repaints tag status until you cancel. Use it after `BumpAndForget`. It does not turn a failed run into a nonzero exit; `Bump` does that. See [Watch a release](versioning.md#watch-a-release) and [Release outcomes](versioning.md#release-outcomes). Source: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:278-287,351-390`.

With `UseJsonFileVersioning` enabled, both bump verbs fail with `Bump is disabled if UseJsonFileVersioning is enabled.` Edit `versions.json` and commit it instead. See [JSON file mode](versioning.md#json-file-mode). Source: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:196-199`.

## Exit codes

| Code | Meaning |
| ---- | ------- |
| `0` | Done. |
| `2` | Wrong usage: unknown command or option, bad `--args`, interactive with no terminal. |
| `1` | Something failed while running. |
| `130` | Cancelled. |

Sources: `src/Plutonium.Shell/PlutoniumShellCommand.cs:42,57,68` (codes `2` and `1`); `130` is the cancel code from the underlying `ApplicationBuilderHelpers` host.

## Tag safety

`delete-origin-tags` without `--args` refuses unless `PLUTONIUM_ALLOW_TAG_WIPE=true` is set — a full wipe never happens by accident. Source: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:78,596-604`.
