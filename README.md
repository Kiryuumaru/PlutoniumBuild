# PlutoniumBuild

Define your build steps in C#, run them from your terminal or in CI, and generate your GitHub Actions and Azure Pipelines workflow files from the same definitions — with per-app versioning handled for you.

> Status: **Phase-B landed.** The engine, author layer, and CLI exist with landed code; `tests/` landed, `samples/` still **planned**. Pins: SDK `10.0.401`, version `0.1.0`, targets `net8.0` / `net9.0` / `net10.0`.

## Why use it

- Describe what to test, build, and publish once, then run the same entries on your machine and in CI's per-entry jobs.
- Your CI workflow files are generated from those definitions, so they cannot drift out of sync.
- Each app gets its own version line, with commands to inspect and release new versions.

## Getting started

Reference `Plutonium.Helpers` and `Plutonium.Shell` from your build project (`Plutonium.Core` comes along as the engine), then describe your build as a plain class with one property per step:

```csharp
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Extensions;

public sealed class MyBuild
{
    // Required: names your pipeline. The defaults are fine to start with.
    public WorkflowConfigEntry WorkflowConfig => definition => definition;

    public TestEntry UnitTests => definition => definition
        .AppId("myapp")
        .Execute(() => { /* run your tests */ });

    public BuildEntry Compile => definition => definition
        .AppId("myapp")
        .Execute(() => { /* build your app */ });
}
```

Each step picks an app with `AppId` (lowercase letters, digits, dots, and dashes, starting with a letter or digit) and lists what it does with `Execute`. A `PublishEntry` step works the same way for publishing. Step ids come from the property name by default; every final id — including one set through `WorkflowId` or `Id` — must use ASCII letters, digits, `_`, or `-`, starting with a letter or `_`. An invalid id fails workflow generation with an error naming the id. Discovery finds these properties automatically; every step needs an `AppId`, and the `WorkflowConfig` property is required.

Put this entry point in your app's `Program.cs`, replacing the placeholders with your repo's values:

```csharp
using ApplicationBuilderHelpers;
using Plutonium.Core.FileSystem;
using Plutonium.Helpers.Pipelines.Context;
using Plutonium.Shell;

var root = "/path/to/your/repo"; // replace with your repository root
var options = new PlutoniumBuildOptions(
    ["main"], // your environment branches
    "main", // your main branch
    new PlutoniumPath(root), // repository root
    new PlutoniumPath(root + "/tmp"), // scratch directory — adjust
    new PlutoniumPath(root + "/out"), // shared output directory — adjust
    new PlutoniumPath(root + "/cache"), // shared cache directory — adjust
    "https://github.com/your-org/your-repo"); // replace with your repository URL

var builder = ApplicationBuilder.Create();
builder.AddPlutoniumShell(new MyBuild(), options);
return await builder.RunAsync(args); // args is your command line
```

This wires your build class to the command line; the logging defaults to the console. Then run your app:

```text
mybuild                                # interactive picker (the default)
mybuild shell Test                     # run your tests locally
mybuild shell Bump --args myapp=minor+1  # release a new version
mybuild shell github-workflow          # regenerate the CI file
```

Add `--list` to any command to preview its steps instead of running them.

## Running it

With no arguments your app opens an interactive picker over everything it can do. Otherwise use `shell` plus a verb — see the [CLI reference](docs/cli.md) for all 16 verbs, aliases, options, and exit codes.

## What it generates

- The GitHub Actions file under `.github/workflows` and `azure-pipelines.yml` at the repository root. Regenerate them after changing your steps.
- Versions live in git tags like `myapp/1.2.3`; a release bump tags `myapp/1.3.0-bump`. Bump one with `--args myapp=minor+1` (`major` / `minor` / `patch` / `prerelease` / `pre`; `+` steps up, `>` sets).

## Status and links

- Run `./build.sh` (or `./build.cmd` on Windows) — on a machine without the SDK it installs the pinned SDK into `.plutonium/temp` (repo-local, disposable, gitignored) first, then builds and runs the tests. With `dotnet` already installed, `dotnet build PlutoniumBuild.sln` and `dotnet test` work as before.
- [Docs](docs/index.md) — guides, CLI reference, versioning, workflows.
- [CLI reference](docs/cli.md) — every command, alias, option, and exit code.
- [Versioning](docs/versioning.md) — check versions and release new ones.
- [Workflows](docs/workflows.md) — generate the CI files from your steps.
- [Authoritative scope for 0.1.0](docs/concepts/scope.md) — developer doc: what is and is not included.
- [Packages](docs/dev/packages.md) — developer doc: what each library covers.
- [Architecture](docs/dev/architecture.md) — developer doc: how the three libraries fit together.
- The command line is powered by `ApplicationBuilderHelpers`.
