# PlutoniumBuild

Build automation for any repository. Describe test, build, and publish steps in C#, run them from your terminal, and generate GitHub Actions and Azure Pipelines files from the same definitions — with per-app versioning handled for you.

> Status: **0.1.0 distribution landed.** The engine, author layer, CLI, the single `PlutoniumBuild` package, and the scaffold installer/updater are built. `samples/` remains **planned**.
>
> **Release gate:** the repository is currently private and the 0.1.0 distribution is not published. The one-line installer and `dotnet add package PlutoniumBuild` only work after the repository is public, a release commit is published, and NuGet trusted publishing is configured. The commands below use `<release-sha>` as the placeholder for that full 40-character commit SHA.

## Why use it

- Automate CI/CD for any repository and any stack: steps are C# entries, and any step can run any external tool as a terminal call.
- Describe what to test, build, and publish once, then run the same entries on your machine and in CI's per-entry jobs.
- Generate CI workflow files from those definitions, so they cannot drift out of sync.
- Give each app its own version line, with commands to inspect and release new versions.

## Scaffold a repository

Run the installer from the repository you want to build. This one line downloads the installer from the release commit and runs it:

```bash
curl -fsSL https://raw.githubusercontent.com/Kiryuumaru/PlutoniumBuild/<release-sha>/init.sh | bash -s -- --sha <release-sha>
```

Prefer to inspect the script before running it? Download it first:

```bash
curl -fsSL -o init.sh https://raw.githubusercontent.com/Kiryuumaru/PlutoniumBuild/<release-sha>/init.sh
bash init.sh --sha <release-sha>
```

On Windows PowerShell:

```powershell
Invoke-WebRequest https://raw.githubusercontent.com/Kiryuumaru/PlutoniumBuild/<release-sha>/init.ps1 -OutFile init.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\init.ps1 -Sha <release-sha>
```

Before you run it:

- The source repository must be public and the `<release-sha>` must be a published release commit. Both are release-gated.
- `git` must be on `PATH`.
- Run the installer from the target repository root.
- The target must not already have a `build/` directory or a root `build.sh`, `build.ps1`, or `build.cmd`. Move existing files aside first.

The installer fetches exactly the pinned commit. Before writing, it rejects a credential-bearing `--source` (`-Source`) URL, requires every scaffold payload file except the lock itself to be listed as managed or preserve in the fetched lock, and checks that the lock package version, `src/Directory.Build.props`, and the build app's package pin agree. It refuses symlinked or non-regular payload files, and on Windows it also refuses a reparse-point `build/` or root wrapper destination. It installs atomically and never edits your solution or adds projects to it.

## What the scaffold installs

```text
build/
├── build.cs               # user-owned: replace the sample Test/Build/Publish entries
├── Directory.Build.props  # user-owned: isolation for the build app
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
└── temp/                  # created on first run: SDK, scratch, output, cache
```

The installer also adds thin root wrappers `build.sh`, `build.ps1`, and `build.cmd`. Generated CI invokes those root wrappers, which forward into `build/`.

The scaffold's `.gitignore` keeps `temp/`, `bin/`, and `obj/` out of git. Keep `*.sh` and `*.cmd` at LF endings if your repository uses `core.autocrlf`.

## Run your build

```bash
./build.sh Test
./build.sh Build
./build.sh Publish
./build.sh               # interactive picker
```

On Windows, use `.\build.cmd Test` or `build\build.cmd Test`.

The launcher reads `build/global.json` (SDK `10.0.402`) and installs that SDK under `build/temp` when no usable `dotnet` is on `PATH` or in the cache. It then builds `build/build.csproj`, runs the build app with the repository root as the working directory, and passes every argument through.

`build.sh Test` is equivalent to `build.sh shell Test`: the scaffold entry point adds the `shell` group when the first argument is a verb.

## Update the scaffold

```bash
build/update.sh --sha <release-sha>
```

On Windows, use `build\update.cmd -Sha <release-sha>`.

The updater fetches and verifies the pinned commit and applies the same source checks as the installer. It replaces managed files and bumps the `PlutoniumBuild` package pin. It preserves `build/build.cs`, `build/Directory.Build.props`, `build/Directory.Build.targets`, and `build/build.slnx`, and never touches your root solution. It journals every affected path under `build/temp/update/<stamp>`; if apply or the smoke build fails, it restores replaced files and removes files introduced by the new lock.

## Add the package to an existing build app

```bash
dotnet add package PlutoniumBuild
```

The package targets `net10.0` and depends only on `ApplicationBuilderHelpers` `4.1.160`. Use it from a `net10.0` executable project. The scaffold already creates this project; to wire one yourself, add the package reference:

```xml
<ItemGroup>
  <PackageReference Include="PlutoniumBuild" Version="0.1.0" />
</ItemGroup>
```

Then add an entry point:

```csharp
using ApplicationBuilderHelpers;
using Plutonium.Core.FileSystem;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Shell;

var root = Directory.GetCurrentDirectory();
var state = Path.Combine(root, "build", "temp");
var options = new PlutoniumBuildOptions(
    ["main"], // your environment branches
    "main", // your main branch
    new PlutoniumPath(root), // repository root
    new PlutoniumPath(Path.Combine(state, "scratch")),
    new PlutoniumPath(Path.Combine(state, "output")),
    new PlutoniumPath(Path.Combine(state, "cache")),
    "https://github.com/your-org/your-repo");

var builder = ApplicationBuilder.Create();
builder.AddPlutoniumShell(new MyBuild(), options);
return await builder.RunAsync(args);
```

## Describe your build

A build is a plain class with one property per step:

```csharp
using Plutonium.Core.Process;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Discovery;
using Plutonium.Helpers.Entries.Extensions;

internal sealed class MyBuild : PlutoniumBuildBase
{
    // Required: names your pipeline. The defaults are fine to start with.
    protected override WorkflowConfigEntry WorkflowConfig =>
        definition => definition.Name("My pipeline");

    public TestEntry Test => definition => definition
        .AppId("myapp")
        .Execute(async context =>
        {
            var result = await context.Terminal.RunAsync(
                new PlutoniumProcessRequest("npm", "ci"));
            result.EnsureSucceeded();
        });
}
```

Add `BuildEntry` and `PublishEntry` properties the same way. Each step picks an app with `AppId` (lowercase letters, digits, dots, and dashes, starting with a letter or digit) and lists what it does with `Execute`. A `PublishEntry` step works the same way for publishing. Step ids come from the property name by default; every final id — including one set through `WorkflowId` or `Id` — must use ASCII letters, digits, `_`, or `-`, starting with a letter or `_`. An invalid id fails workflow generation with an error naming the id. Discovery finds these properties automatically; every step needs an `AppId`. A `WorkflowConfig` property must exist — `PlutoniumBuildBase` supplies a default, so override it only to configure the pipeline.

A step runs any external tool you need — npm, cargo, make, a script — through the terminal:

```csharp
using Plutonium.Core.Process;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Extensions;

public BuildEntry Compile => definition => definition
    .AppId("myapp")
    .Execute(async context =>
    {
        var result = await context.Terminal.RunAsync(new PlutoniumProcessRequest("npm", "ci"));
        result.EnsureSucceeded();
    });
```

There is no shell: pass the executable plus one raw argument string, and quote arguments for your OS yourself. On Windows, run `.cmd` shims such as `npm` through `cmd`, for example `new PlutoniumProcessRequest("cmd", "/c npm ci")`. Output is captured, masked, and forwarded to the run log when the tool exits; a non-zero exit code surfaces through `EnsureSucceeded`. See [Terminal calls](docs/terminal-calls.md) for the contract.

## Command line

With no arguments your app opens an interactive picker over everything it can do. Otherwise use `shell` plus a verb; in a scaffolded repository the root wrapper adds `shell` for you, so `./build.sh Test` works:

```text
./build.sh Test                        # run your tests locally
./build.sh Bump --args myapp=minor+1   # release a new version
./build.sh github-workflow             # regenerate the CI file
```

Add `--list` to any command to preview its steps instead of running them. See the [CLI reference](docs/cli.md) for all 16 verbs, aliases, options, and exit codes.

## What it generates

- The GitHub Actions file under `.github/workflows` and `azure-pipelines.yml` at the repository root. Regenerate them after changing your steps.
- GitHub workflows default to `permissions: contents: read`. Only pre-setup and post-setup get write access, the pipeline token in their job environment, and all declared secrets; entry jobs get only the secrets that apply to them and do not persist checkout credentials. Actions are pinned to commit SHAs, and workflow names are validated before writing.
- Versions live in git tags like `myapp/1.2.3`; a release bump tags `myapp/1.3.0-bump`. Bump one with `--args myapp=minor+1` (`major` / `minor` / `patch` / `prerelease` / `pre`; `+` steps up, `>` sets).
- Generated workflows trigger on pushes to your environment branches, `bump-*` tags, and pull requests.

## Versioning and releases

A tag-mode release starts with `Bump`. It pushes the current commit, writes `<app>/<version>-bump` for each bumped app, force-moves `bump-<branch>`, and watches the generated run. `BumpAndForget` skips the watch; `StatusWatch` attaches until you cancel. `Bump` fails when a watched app reports a `-failed` stamp.

Post-setup reads each entry job's normalized `PLUTONIUM_RUN_RESULT_*` value. GitHub fills the raw value from `needs.<ENTRY>.result`; Azure fills it from `stageDependencies.<STAGE>.<STAGE>.result`. On a release run, any `error` prevents publishing: post-setup stamps `<app>/<version>-failed` and `<app>/<version>`, deletes the draft release, and fails the run. On a release run with no `error` result, post-setup stamps `<app>/<version>-passed`, `<app>/<version>`, and `<app>/latest` (or `<app>/latest-<env>`), then publishes the draft.

For `versions.json` mode, enable `UseJsonFileVersioning(true)`. Workflow generation writes the file, and you release by editing the version and committing it; `Bump` and `BumpAndForget` are refused. `Version` rejects a file that trails the latest tag. See [Versioning](docs/versioning.md).

## Builder and app versions

Two version lines stay separate:

| Version | Who owns it | Where it lives |
| ------- | ----------- | -------------- |
| Builder package | PlutoniumBuild maintainers | `src/Directory.Build.props` (currently `0.1.0`), packed as the `PlutoniumBuild` package |
| Consumer apps | You | Per-app and per-environment tags like `myapp/1.2.3`, or the optional `versions.json` file |

`build/update.*` bumps the builder package pin in `build/build.csproj`. It never changes your app versions. See [Versioning](docs/versioning.md).

## Repository development

- `./build.sh` (or `./build.cmd` on Windows) is the maintainer runner: it installs the pinned SDK into `.plutonium/temp` when needed, then builds `PlutoniumBuild.sln` and runs every test.
- CI verifies the scaffold lock and bash smoke scripts on Linux, runs the PowerShell scaffold smoke on Windows, and builds and tests on both hosts.
- `dotnet pack src/PlutoniumBuild.Package/PlutoniumBuild.Package.csproj -c Release -o artifacts` builds the single NuGet package.
- `.github/workflows/publish.yml` publishes to nuget.org with OIDC trusted publishing. It is manual, requires the `nuget-publish` environment, and is release-gated.

## Links

- [Docs](docs/index.md) — guides, CLI reference, versioning, workflows.
- [CLI reference](docs/cli.md) — every command, alias, option, and exit code.
- [Versioning](docs/versioning.md) — builder vs app versions, tags or `versions.json`, outcome stamps, and watches.
- [Workflows](docs/workflows.md) — generate the CI files from your steps.
- [Terminal calls](docs/terminal-calls.md) — run any external tool from a step.
- [Secrets](docs/secrets.md) — declare secrets and understand their environment keys.
- [Authoritative scope for 0.1.0](docs/concepts/scope.md) — developer doc: what is and is not included.
- [Packages](docs/dev/packages.md) — developer doc: the package contents and dependency rules.
- [Architecture](docs/dev/architecture.md) — developer doc: package, scaffold, and library boundaries.
- [Package readme](docs/package-readme.md) — the text packed into the NuGet package.
- [License](LICENSE) — MIT.
- The command line is powered by `ApplicationBuilderHelpers`.
