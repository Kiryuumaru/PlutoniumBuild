# PlutoniumBuild

Build automation for any repository. Describe test, build, and publish steps in
C#, then run them locally or generate GitHub Actions and Azure Pipelines files
from the same definitions.

## Install

```bash
dotnet add package PlutoniumBuild
```

The package targets `net10.0` and depends only on `ApplicationBuilderHelpers`
`4.1.160`.

## Scaffold a repository

The scaffold installs a small `build/` app, the root launchers, and the update
tooling. Run this from the repository you want to build:

```bash
curl -fsSL https://raw.githubusercontent.com/Kiryuumaru/PlutoniumBuild/6aec14937739661d9c24affffaa6c8928c1f2655/init.sh | bash -s -- --sha 6aec14937739661d9c24affffaa6c8928c1f2655
```

The source repository must be public and the pinned commit must exist. The
installer also needs `git` on `PATH` and refuses to overwrite an existing
`build/` or conflicting root `build.sh` / `build.ps1` / `build.cmd`. If you
override the default with `--source` (`-Source`), use a credential-free URL: the
installer rejects userinfo, query strings, and fragments.

To inspect the script first, download it to `init.sh.dl`:

```bash
rm -f init.sh.dl && curl -fsSL -o init.sh.dl https://raw.githubusercontent.com/Kiryuumaru/PlutoniumBuild/6aec14937739661d9c24affffaa6c8928c1f2655/init.sh
```

Download to a fresh file. Do not run a leftover `init.sh.dl` from an earlier
attempt.

Inspect `init.sh.dl`, then run it:

```bash
mv init.sh.dl init.sh && bash init.sh --sha 6aec14937739661d9c24affffaa6c8928c1f2655
```

On Windows PowerShell:

```powershell
Invoke-WebRequest https://raw.githubusercontent.com/Kiryuumaru/PlutoniumBuild/6aec14937739661d9c24affffaa6c8928c1f2655/init.ps1 -OutFile init.ps1 -ErrorAction Stop
powershell -NoProfile -ExecutionPolicy Bypass -File .\init.ps1 -Sha 6aec14937739661d9c24affffaa6c8928c1f2655
```

Download to a fresh file. Do not run an `init.ps1` left over from an earlier
attempt.

The installer verifies the pinned commit, requires every scaffold payload file
except the lock itself to be listed in the lock, checks that the lock package
version, the source project version, and the build app's package pin agree, and
rejects symlinked or non-regular payload files. On Windows, it also refuses a
reparse-point `build/` or root wrapper destination. It never edits your
solution or adds projects to it.

## What the scaffold installs

```text
build/
├── build.cs               # user-owned: your Test/Build/Publish entries
├── Directory.Build.props  # user-owned: build-app isolation
├── Directory.Build.targets
├── build.slnx             # user-owned: solution for the build app
├── build.csproj           # managed: PackageReference PlutoniumBuild 0.1.0
├── Program.cs             # managed: entry point and options
├── global.json            # managed: SDK 10.0.402
├── plutonium.lock         # managed: source commit, package version, file hashes
├── build.sh/.ps1/.cmd     # managed: launchers
├── update.sh/.ps1/.cmd    # managed: updater
├── bootstrap.sh/.ps1      # managed: verified SDK bootstrap
├── .gitignore             # managed: temp/, bin/, obj/
├── README.md              # managed: build guide
└── temp/                  # created on first run: SDK, scratch, output, cache
```

The installer adds thin root wrappers `build.sh`, `build.ps1`, and `build.cmd`.
Generated CI invokes those root wrappers, which forward into `build/`.

## Run

```bash
./build.sh Test
./build.sh Build
./build.sh Publish
./build.sh               # interactive picker
```

On Windows, use `.\build.cmd Test` or `build\build.cmd Test`. The launcher
installs the SDK pinned in `build/global.json` under `build/temp` when no usable
`dotnet` is on `PATH` or in the cache, builds `build/build.csproj`, and runs it.

## Update

```bash
build/update.sh --sha 6aec14937739661d9c24affffaa6c8928c1f2655
```

On Windows, use `build\update.cmd -Sha 6aec14937739661d9c24affffaa6c8928c1f2655`. The updater applies the
same source checks as the installer, replaces managed files, and bumps the
`PlutoniumBuild` package pin. It preserves `build/build.cs`,
`build/Directory.Build.props`, `build/Directory.Build.targets`, and
`build/build.slnx`. If apply or the smoke build fails, it restores replaced
files and removes files introduced by the new lock.

## Wire it yourself

If you do not use the scaffold, add the package to a `net10.0` executable
project:

```xml
<ItemGroup>
  <PackageReference Include="PlutoniumBuild" Version="0.1.0" />
</ItemGroup>
```

Add an entry point:

```csharp
using ApplicationBuilderHelpers;
using Plutonium.Core.FileSystem;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Shell;

var root = Directory.GetCurrentDirectory();
var state = Path.Combine(root, "build", "temp");
var options = new PlutoniumBuildOptions(
    ["main"],
    "main",
    new PlutoniumPath(root),
    new PlutoniumPath(Path.Combine(state, "scratch")),
    new PlutoniumPath(Path.Combine(state, "output")),
    new PlutoniumPath(Path.Combine(state, "cache")),
    "https://github.com/your-org/your-repo");

var builder = ApplicationBuilder.Create();
builder.AddPlutoniumShell(new MyBuild(), options);
return await builder.RunAsync(args);
```

`MyBuild` declares `TestEntry`, `BuildEntry`, and `PublishEntry` properties plus
a `WorkflowConfig` property. Each entry names an `AppId` and runs tools through
`context.Terminal`:

```csharp
using Plutonium.Core.Process;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Discovery;
using Plutonium.Helpers.Entries.Extensions;

internal sealed class MyBuild : PlutoniumBuildBase
{
    protected override WorkflowConfigEntry WorkflowConfig =>
        definition => definition.Name("My pipeline");

    public TestEntry Test => definition => definition
        .AppId("myapp")
        .Execute(async context =>
        {
            var result = await context.Terminal.RunAsync(
                new PlutoniumProcessRequest("npm", "test"));
            result.EnsureSucceeded();
        });
}
```

The terminal has no shell: pass the executable and one raw argument string, and
quote arguments for your OS yourself. Any tool works — npm, cargo, make, a
script — so the repository under build does not have to be C#.

## Command line

```text
mybuild                                # interactive picker
mybuild shell Test                     # run test entries
mybuild shell Build                    # run build entries
mybuild shell Publish                  # run publish entries
mybuild shell github-workflow          # write the GitHub Actions file
mybuild shell azure-workflow           # write azure-pipelines.yml
```

Add `--list` to a verb to list the available targets instead of running them. In a
scaffolded repository, `./build.sh Test` is the same command as
`./build.sh shell Test`.

## Generated workflows

`github-workflow` writes a GitHub Actions file under `.github/workflows`;
`azure-workflow` writes `azure-pipelines.yml`. The GitHub workflow defaults to
`permissions: contents: read`. Only pre-setup and post-setup get write
permission, the pipeline token in their job environment, and every declared
secret; each entry job gets only the secrets that apply to it and does not
persist checkout credentials.
Every GitHub action is pinned to a full commit SHA. The GitHub workflow name is
validated into a safe file slug and kept under `.github/workflows`. Azure keeps
`Cache@2` as a task version.

## Versions

The `PlutoniumBuild` package version (`0.1.0`) is the builder version, set by
the PlutoniumBuild maintainers. It is separate from your app versions. Your
apps use per-app and per-environment tags such as `myapp/1.2.3`, or the optional
`versions.json` mode. `build/update.*` bumps the builder package pin only.

A tag-mode release starts with `Bump`. It pushes the current commit, writes
`<app>/<version>-bump` for each bumped app, force-moves `bump-<branch>`, and
watches the generated run. `BumpAndForget` skips the watch; `StatusWatch`
attaches until you cancel.

Post-setup reads each entry job's normalized `PLUTONIUM_RUN_RESULT_*` value.
GitHub fills the raw value from `needs.<ENTRY>.result`; Azure fills it from
`stageDependencies.<STAGE>.<STAGE>.result`. On a release run, any `error`
prevents publishing: it stamps
`<app>/<version>-failed` and `<app>/<version>`, deletes the draft release, and
fails the run. On a release run with no `error` result, it stamps
`<app>/<version>-passed`, `<app>/<version>`, and `<app>/latest` or
`<app>/latest-<env>`, then publishes the draft.

`UseJsonFileVersioning(true)` switches to `versions.json`: workflow generation
writes the file, you release by editing the version and committing, and both
`Bump` verbs are refused. `Version` rejects a file that trails the latest tag.

## Documentation

- [Documentation hub](https://github.com/Kiryuumaru/PlutoniumBuild/blob/main/docs/index.md)
- [CLI reference](https://github.com/Kiryuumaru/PlutoniumBuild/blob/main/docs/cli.md)
- [Versioning](https://github.com/Kiryuumaru/PlutoniumBuild/blob/main/docs/versioning.md)
- [CI workflows](https://github.com/Kiryuumaru/PlutoniumBuild/blob/main/docs/workflows.md)
- [Terminal calls](https://github.com/Kiryuumaru/PlutoniumBuild/blob/main/docs/terminal-calls.md)
- [Secrets](https://github.com/Kiryuumaru/PlutoniumBuild/blob/main/docs/secrets.md)

## License

MIT. See [LICENSE](https://github.com/Kiryuumaru/PlutoniumBuild/blob/main/LICENSE).
