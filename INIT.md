# INIT — install PlutoniumBuild into this repository

This file is the entry point for an AI coding agent asked to install PlutoniumBuild. Read it top to bottom, run the commands from the target repository root, and stop to ask the user when a precondition fails.

If this repository has its own `AGENTS.md` or `CLAUDE.md`, follow those where they conflict with this file.

## What PlutoniumBuild is

PlutoniumBuild is a build-automation scaffold. You describe Test, Build, and Publish steps as C# entries, run them locally with one launcher, and generate GitHub Actions and Azure Pipelines files from the same definitions. It ships a one-line installer that creates a `build/` folder plus thin root wrappers.

## Preconditions

Run the installer from the root of the repository you want to build. Check each item before writing anything:

- `git` is on `PATH`; the installer fetches the scaffold with it.
- The PlutoniumBuild source repository is public and reachable. The installer resolves the default branch (`main`) unless you pass `--ref` or `--sha`.
- The repository has at least one commit. The sample Build and Publish steps call `git rev-parse` and `git describe` against `HEAD`.
- No `build/` directory exists. The installer refuses an existing scaffold; use `build/update.sh` to update one.
- No root `build.sh`, `build.ps1`, or `build.cmd` exists. Move existing files aside first.
- .NET SDK `10.0.402` is pinned. The launcher bootstraps it under `build/temp` when no usable `dotnet` is on `PATH`.
- The `PlutoniumBuild` package is published on nuget.org. The build app references it by version.

## Install

### Linux and macOS

```bash
curl -fsSL -o init.sh https://raw.githubusercontent.com/Kiryuumaru/PlutoniumBuild/main/init.sh && bash init.sh
```

The download and the run are joined with `&&`, so a failed download stops before anything executes.

To pin a revision, pass `--ref <tag-or-branch>` or `--sha <full-commit>`. They are mutually exclusive:

```bash
bash init.sh --ref <tag-or-branch>
bash init.sh --ref v1.0
bash init.sh --ref refs/tags/v1.0
bash init.sh --sha <full-commit>
```

`--ref` accepts a bare branch or tag name, a fully qualified `refs/heads/...` or `refs/tags/...`, or a full 40-hex commit SHA. A bare name such as `v1.0` or `release/1.0` resolves a tag or branch. A bare name that matches both a branch and a tag is rejected as ambiguous; pass the fully qualified form. `--ref` resolves the name once at install time, so a branch or tag can move later. Use `--sha` for an exact immutable pin.

Prefer to inspect the script before running it? Download it to `init.sh.dl` first:

```bash
rm -f init.sh.dl && curl -fsSL -o init.sh.dl https://raw.githubusercontent.com/Kiryuumaru/PlutoniumBuild/main/init.sh
```

Download to a fresh file. Do not run a leftover `init.sh.dl` from an earlier attempt.

Inspect `init.sh.dl`, then run it:

```bash
mv init.sh.dl init.sh && bash init.sh
```

### Windows

Download the installer with `-ErrorAction Stop`, inspect the file, then run it from PowerShell:

```powershell
Invoke-WebRequest https://raw.githubusercontent.com/Kiryuumaru/PlutoniumBuild/main/init.ps1 -OutFile init.ps1 -ErrorAction Stop
powershell -NoProfile -ExecutionPolicy Bypass -File .\init.ps1
```

Download to a fresh file. Do not run an `init.ps1` left over from an earlier attempt.

To pin a revision on Windows, add `-Ref <tag-or-branch>` or `-Sha <full-commit>`.

With no arguments, the install commands resolve the default branch (`main`) to a full commit SHA before fetching. `--ref` accepts a bare branch or tag name, a fully qualified `refs/heads/...` or `refs/tags/...`, or a full 40-hex commit SHA; a bare name that matches both a branch and a tag is rejected as ambiguous. `--sha` accepts only a full commit SHA. The two are mutually exclusive. The resolved commit is recorded as `scaffold_commit` in `build/plutonium.lock`; that commit is traceability, not authentication. Branch-latest is trust-on-first-use; a compromised default branch runs code. The same-payload lock hashes detect corruption or mismatch, not upstream compromise.

The lock's `source` is security-sensitive: a no-arg update fetches from that source. An explicit `--source` re-homes the lock to the fetched source. The lock must use canonical reserved keys with no duplicates; a non-canonical or duplicate-key lock is rejected. The installed `build/README.md` is not changed because its links point at `main`.

The installer resolves the requested revision to a full commit SHA before fetching and verifies every managed file against `build/plutonium.lock`. It refuses a credential-bearing `--source` (`-Source` on Windows), installs atomically, and never edits your solution or adds projects to it.

## What install creates

```text
build/                          # the build app
build.sh build.ps1 build.cmd    # thin root wrappers that forward into build/
```

The root wrappers exist because generated CI runs `./build.sh` on Linux and `.\build.cmd` on Windows. Keep them at the repository root.

## After install

1. Read `build/README.md`. It maps every file in `build/` and shows how to define steps.
2. Edit `build/build.cs`. Replace the sample Test, Build, and Publish entries with the tools this repository uses.
3. Run `./build.sh --list` to list the available verbs and targets.

Do not edit these managed files; `build/update.sh` replaces them:

- `build/README.md`
- `build/Program.cs`
- `build/build.csproj`
- `build/global.json`
- `build/plutonium.lock`
- `build/build.sh`, `build/build.ps1`, `build/build.cmd`
- `build/update.sh`, `build/update.ps1`, `build/update.cmd`
- `build/bootstrap.sh`, `build/bootstrap.ps1`
- `build/.gitignore`
- root `build.sh`, `build.ps1`, `build.cmd`

The scaffold also includes `build/Directory.Build.props`, `build/Directory.Build.targets`, and `build/build.slnx`. Updates preserve them. Leave them alone unless you need to change the build app's project settings.

## Run the build locally

From the repository root on Linux or macOS:

```bash
./build.sh Test
./build.sh Build
./build.sh Publish
./build.sh            # interactive picker
./build.sh --list     # list available verbs and targets
```

On Windows:

```text
.\build.cmd Test
.\build.cmd Build
.\build.cmd Publish
.\build.cmd --list
```

`Test`, `Build`, and `Publish` are the local run verbs. The managed `build/Program.cs` prepends the `shell` verb when the first argument is a verb; the root wrapper only forwards arguments. So `./build.sh Test` runs the same command as `./build.sh shell Test`.

## Generate CI

Generate the files after changing your steps, then commit the generated file:

```bash
./build.sh github-workflow   # writes .github/workflows/<name>.yml
./build.sh azure-workflow    # writes azure-pipelines.yml
```

On Windows, use `.\build.cmd github-workflow` or `.\build.cmd azure-workflow`.

The GitHub file name is a slug of the `WorkflowConfig` name in `build/build.cs`; the YAML `name:` keeps your display name. Each generated file runs one job per entry (one stage per entry on Azure), plus pre-setup and post-setup jobs.

## Secrets

Declare a secret on the build class and create the same name in the repository:

```csharp
using Plutonium.Helpers.App;

[PlutoniumSecretVariable("NUGET_KEY", EntryIds = new[] { "publish" })]
public string? NugetKey { get; private set; }
```

- GitHub: add a repository secret named `NUGET_KEY`.
- Azure: add a secret pipeline variable named `NUGET_KEY`.

On GitHub, `EntryIds` limits the secret to the named entry jobs; `publish` matches the `Publish` entry because entry ids are case-insensitive. An unscoped secret reaches every entry job, including Test and Build jobs triggered by pull requests. Pre-setup and post-setup receive every declared secret and `GITHUB_TOKEN` with `contents: write`. Log masking covers the run log and captured tool output, not files or artifacts your steps write. Regenerate the workflow files after adding, renaming, or rescoping a secret.

`EntryIds` scoping applies to GitHub only. On Azure, every declared secret is available to every stage, including Test and Build stages triggered by pull requests. Do not declare a publish secret for Azure unless the pipeline is trusted. See [Secrets](docs/secrets.md).

## Update

```bash
build/update.sh
```

On Windows:

```text
build\update.cmd
```

With no arguments, the updater fetches from the `source` recorded in `build/plutonium.lock`, which defaults to the default branch (`main`) tip. Pass `--ref <tag-or-branch>` or `--sha <full-commit>` to select a revision; `--ref` resolves once and can move, while `--sha` is an exact pin. An explicit `--source` re-homes the lock to the fetched source. An install created by the 0.1.0 installer must bootstrap once with `build/update.sh --sha <full-commit>`; the old updater has no `--ref`. After that, no-arg updates follow the lock's source at the default branch.

The updater replaces managed files, runs the fetched bootstrap before committing the update, and bumps the `PlutoniumBuild` package pin. It preserves `build/build.cs`, `build/Directory.Build.props`, `build/Directory.Build.targets`, and `build/build.slnx`. A failed update, including a failed bootstrap build, restores the previous files.

## Agent guardrails

- Inspect the repository before installing: confirm the preconditions and that the target is the repository root.
- If `build/` exists, stop and ask. Do not delete it or merge into it.
- Edit only `build/build.cs` and the preserved files. Never edit a managed file.
- Do not change the user's solution, projects, or existing CI files.
- Do not run `./build.sh Publish` (`.\build.cmd Publish` on Windows) without explicit user approval; it may push packages or tags.
- Run `./build.sh --list` (`.\build.cmd --list` on Windows) to list the available verbs and targets before changing steps, and `./build.sh Test` (`.\build.cmd Test` on Windows) after.
- Do not commit or push unless the user asks.

## Troubleshooting and where to read next

- The installer refuses because `build/` exists: run `build/update.sh` instead.
- The fetch fails: check that the source repository is reachable. If you passed `--ref`, check that the name exists and is unambiguous; if you passed `--sha`, check that it is a full 40-character commit SHA.
- Restore fails: check that the `PlutoniumBuild` package version in `build/build.csproj` is available on nuget.org.
- Workflow generation fails: check the `WorkflowConfig` name (lowercase letters, digits, and hyphens) and each entry id (ASCII letters, digits, `_`, and `-`, starting with a letter or `_`).

Read next:

- [README](README.md) — package, scaffold, and CLI overview.
- [Workflows](docs/workflows.md) — generated CI files, security, and release results.
- [CLI reference](docs/cli.md) — every verb, option, and exit code.
- [Secrets](docs/secrets.md) — declaration, scoping, environment keys, and masking.
