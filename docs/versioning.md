# Versioning and releases

Each app in your build gets its own version line. Check versions with `Version`, release new ones with `Bump`, and follow along with `StatusWatch`.

## Name your apps

Every step picks an app with `AppId`. An AppId uses lowercase letters, digits, dots, and dashes, and starts with a letter or digit — `myapp`, `web.frontend`, `api-v2` all work.

Source: `src/Plutonium.Helpers/App/PlutoniumAppId.cs:108` (`^[a-z0-9][a-z0-9.-]*$`).

## Check versions

```text
mybuild shell Version
mybuild shell Fetch
```

`Fetch` downloads the latest commits and tags. `Version` fetches, then prints one version table per app. Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:90-91`.

## Release a new version

```text
mybuild shell Bump --args myapp=minor+1
```

`Bump` reads the `--args` expression, tags the new version, pushes the tag, then watches the release finish. Without `--args` it asks you which bumps to apply. Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:93,179-217`.

The expression names a part and an amount:

| Expression | Meaning |
| ---------- | ------- |
| `myapp=major+1` | Step the major part up by 1 |
| `myapp=minor+1` | Step the minor part up by 1 |
| `myapp=patch+2` | Step the patch part up by 2 |
| `myapp=pre+1` | Step the prerelease part up by 1 (`prerelease` works too) |
| `myapp=minor>3` | Set the minor part to exactly 3 |

Parts rank so the biggest change wins when you combine them (`minor+1,pre+2`). Amounts default to 1. Sources: `src/Plutonium.Helpers/Versioning/PlutoniumBumpArgs.cs:24-29,127-172`.

If your build has exactly one app, you can skip the name:

```text
mybuild shell Bump --args minor+1
```

Source: `src/Plutonium.Helpers/Versioning/PlutoniumBumpArgs.cs:106-125`.

## Release without watching

```text
mybuild shell BumpAndForget --args myapp=patch+1
mybuild shell StatusWatch
```

`BumpAndForget` releases and returns at once (`bump-and-forget` also works). `StatusWatch` attaches to a release that is already running (`status-watch` also works). Sources: `src/Plutonium.Helpers/App/PlutoniumBuildVerbs.cs:94-95`, `src/Plutonium.Shell/PlutoniumVerbCommands.cs:39-68`.

## Where versions live

Versions live in git tags shaped like `myapp/1.2.3` — the lowercase AppId, a slash, then the version. A `Bump` release tags `myapp/1.3.0-bump`; the `-bump` marker tells the pipeline this tag starts a release. Sources: `src/Plutonium.Helpers/Versioning/PlutoniumVersionTags.cs:24-33,60-65`.
