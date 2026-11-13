# Versioning and releases

Each app in your build gets its own version line. Check versions with `Version`, release new ones with `Bump`, and follow the run with `StatusWatch`.

## Builder versions vs app versions

PlutoniumBuild itself has one version: the `PlutoniumBuild` package version in `src/Directory.Build.props` (currently `0.1.0`). That builder version is not your app version.

Each app in your build has its own version line, described below. A consumer app version is per app and per environment: a tag such as `myapp/1.2.3` on an environment branch, or an entry in `versions.json` when JSON file versioning is enabled. `build/update.*` bumps only the builder package pin.

## Name your apps

Every step picks an app with `AppId`. An AppId uses lowercase letters, digits, dots, and dashes, and starts with a letter or digit — `myapp`, `web.frontend`, `api-v2` all work.

Source: `src/Plutonium.Helpers/App/PlutoniumAppId.cs:108` (`^[a-z0-9][a-z0-9.-]*$`).

## Check versions

```text
mybuild shell Version
```

`Version` fetches commits and tags, reconciles them per app, and prints one version table per app. In `versions.json` mode, it also verifies that the file does not trail the latest tag. Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:90-91,142-179`.

## Release a new version

```text
mybuild shell Bump --args myapp=minor+1
```

`Bump` reads the `--args` expression and rejects a version that is not ahead of the latest version in that environment. A release must be bumped on the main environment branch; a prerelease must be bumped on its prerelease branch. Without `--args`, it asks you which bumps to apply. Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:93,191-270`, `src/Plutonium.Helpers/Versioning/PlutoniumVersions.cs:297-365`.

The expression names a part and an amount:

| Expression | Meaning |
| ---------- | ------- |
| `myapp=major+1` | Step the major part up by 1 |
| `myapp=minor+1` | Step the minor part up by 1 |
| `myapp=patch+2` | Step the patch part up by 2 |
| `myapp=pre+1` | Step the prerelease part up by 1 (`prerelease` works too) |
| `myapp='minor>3'` | Set the minor part to exactly 3 |

`pre` needs a dotted numeric tail, such as `main.4`. Bumps apply in rank order, from prerelease up to major. A higher part resets the lower parts. Amounts default to 1. Quote expressions that contain `>` in your shell. Sources: `src/Plutonium.Helpers/Versioning/PlutoniumBumpArgs.cs:24-29,106-172`, `src/Plutonium.Helpers/Versioning/PlutoniumSemVersionBumps.cs:43-104`.

If your build has exactly one app, you can skip the name:

```text
mybuild shell Bump --args minor+1
```

Source: `src/Plutonium.Helpers/Versioning/PlutoniumBumpArgs.cs:106-125`.

### What Bump pushes

`Bump` makes the release visible to CI in four steps:

1. Creates `<app>/<version>-bump` for every bumped app.
2. Pushes the current commit to `origin`.
3. Pushes each bump tag.
4. Deletes the old `bump-<branch>` tag, force-moves it to the current commit, and force-pushes it.

Generated workflows watch `bump-*` tags and environment-branch pushes. The trigger tag carries the branch name. Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:237-270`, `src/Plutonium.Helpers/Pipelines/PlutoniumPipelineDetection.cs:260-299`.

`BumpAndForget` does the same pushes without watching. `BumpAndForget` and `bump-and-forget` are the same command.

## Watch a release

`Bump` watches the apps and environment it bumped. It reconciles tag state every second and stops when every target has a `-passed` stamp. If any target has a `-failed` stamp, it throws `Pipeline run has failed.` and exits nonzero. Source: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:260-269,314-428`.

`BumpAndForget` returns after the pushes. Run `StatusWatch` to attach to the current branch and repaint all apps and environments until you cancel with Ctrl+C (exit `130`).

| Status | Meaning |
| ------ | ------- |
| `Published` | The latest version has a `-passed` stamp |
| `Run Failed` | The latest version has a `-failed` stamp |
| `Publishing` | The latest version has a `-queue` stamp |
| `Waiting for queue` | The latest version has only a `-bump` stamp |
| `Not published` | No release state for that app and environment |

`StatusWatch` keeps running after a failed stamp. Use `Bump` when a failed release must end the command. Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:278-287,351-390`, `src/Plutonium.Shell/PlutoniumShellCommand.cs:42,57,68`.

## Release outcomes

A release run creates a draft GitHub release before the entry jobs start. After the entry jobs, post-setup reads `PLUTONIUM_RUN_RESULT_<ENTRY>` for every run entry. GitHub fills `PLUTONIUM_RUN_RESULT_GITHUB_<ENTRY>` from `needs.<ENTRY>.result`; Azure fills `PLUTONIUM_RUN_RESULT_AZURE_<ENTRY>` from `stageDependencies.<STAGE>.<STAGE>.result` for the stage generated from that entry. Entry ids are uppercased, and non-alphanumeric characters become `_` in both the plain variable and the Azure stage id. Post-setup runs after a failed entry job on both hosts: GitHub uses `success() || failure() || always()`, and Azure uses `or(succeeded(), failed())`.

GitHub `failure` and `cancelled` become `error`; Azure `SucceededWithIssues`, `Failed`, and `Canceled` become `error`. `Succeeded` becomes `success`, `Skipped` becomes `skipped`, and an empty result does not fail. Any `error` result chooses the failure path. Sources: `src/Plutonium.Core/Workflows/PlutoniumPipelineContract.cs:30-52,176-191`, `src/Plutonium.Helpers/Pipelines/PlutoniumGitHubPipeline.cs:135-148,465-486`, `src/Plutonium.Helpers/Pipelines/PlutoniumAzurePipeline.cs:136-154,400-417`, `src/Plutonium.Helpers/Pipelines/PlutoniumPreSetup.cs:164-180`.

Post-setup then takes one of two paths:

| Outcome | Stamped tags | Release action |
| ------- | ------------ | -------------- |
| Success | `<app>/<version>-passed`, `<app>/<version>`, and `<app>/latest` or `<app>/latest-<env>` | Publishes the draft release |
| Failure | `<app>/<version>-failed` and `<app>/<version>` | Deletes the draft release and throws `Run has error(s).` |

Success stamps the latest marker as `<app>/latest` on the main environment branch and `<app>/latest-<env>` on other environments. A failure prevents publishing. Both outcomes stamp only apps that have a release in the run. Sources: `src/Plutonium.Helpers/Pipelines/PlutoniumPreSetup.cs:186-200,552-653`, `src/Plutonium.Helpers/Versioning/PlutoniumVersionTags.cs:91-102`.

Local runs do not create or publish a GitHub release. A successful local run writes a `<app>.release` marker under the temporary directory. Sources: `src/Plutonium.Helpers/Pipelines/PlutoniumPreSetup.cs:144-147,552-582`.

## Where versions live

Tags carry release state. All app ids are lowercased; the version stays strict SemVer.

| Tag | Meaning |
| --- | ------- |
| `<app>/<version>` | Release version tag, written during the run |
| `<app>/<version>-bump` | `Bump` asked CI to release this version |
| `<app>/<version>-queue` | Pre-setup queued the version for the run |
| `<app>/<version>-passed` | Post-setup success |
| `<app>/<version>-failed` | Post-setup failure |
| `<app>/latest` | Latest passed version on the main environment branch |
| `<app>/latest-<env>` | Latest passed version on another environment |
| `build.<id>` and `build.<id>-<env>` | Build marker and draft release |
| `bump-<branch>` | `Bump` trigger tag for the branch |

Sources: `src/Plutonium.Helpers/Versioning/PlutoniumVersionTags.cs:23-102,131-218`, `src/Plutonium.Helpers/Pipelines/PlutoniumPreSetup.cs:320-352,552-608`.

## JSON file mode

`UseJsonFileVersioning` stores each app and environment's current version in `versions.json` at the repository root. Enable it on `WorkflowConfig`:

```csharp
protected override WorkflowConfigEntry WorkflowConfig =>
    definition => definition.UseJsonFileVersioning(true);
```

The file is a flat map of app id to environment to strict SemVer:

```json
{
  "myapp": {
    "main": "1.2.3",
    "dev": "1.3.0-dev.1"
  }
}
```

Running `github-workflow` or `azure-workflow` with the option enabled reconciles the file with the tags and rewrites it. A missing or unreadable file is treated as empty. A version in the file that is ahead of the latest tag is kept as a bump for the next run. Sources: `src/Plutonium.Helpers/Entries/Extensions/PlutoniumWorkflowConfigEntryExtensions.cs:233-272`, `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:437-460,700-732`, `src/Plutonium.Helpers/Versioning/PlutoniumVersionsFile.cs:25-30,44-72,128-175`, `src/Plutonium.Helpers/Versioning/PlutoniumVersions.cs:185-219`.

To release in this mode, edit the version to one ahead of the latest tag for that app and environment, commit it, and push the environment branch. The commit run treats the ahead file version as a bump, creates the `-queue` and plain tags, and starts the draft release. Sources: `src/Plutonium.Helpers/Pipelines/PlutoniumPreSetup.cs:244-262,320-352`, `src/Plutonium.Helpers/Versioning/PlutoniumVersions.cs:367-385`.

`Bump` and `BumpAndForget` are refused in this mode. They fail with `Bump is disabled if UseJsonFileVersioning is enabled.` Source: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:196-199`.

`Version` and branch or pull-request pre-setup verify the file. A file that trails the latest tag is invalid and stops the command or run. Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:142-179`, `src/Plutonium.Helpers/Pipelines/PlutoniumPreSetup.cs:62-88`, `src/Plutonium.Helpers/Versioning/PlutoniumVersionsFile.cs:177-214`.

Tags still record release state and outcome stamps in this mode. Regenerate a workflow after tags move ahead of the file; generation rewrites the file from the reconciled tags.
