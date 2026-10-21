# Packages — PlutoniumBuild (0.1.0)

> Developer documentation — for maintainers, not end users.
>
> Status: **0.1.0 distribution landed.** `src/Plutonium.Core/`,
> `src/Plutonium.Helpers/`, and `src/Plutonium.Shell/` exist with landed
> code. `src/PlutoniumBuild.Package/` packs them into one public package.
> Code is source of truth.

## What ships

One package: `PlutoniumBuild`, version `0.1.0` from
`src/Directory.Build.props`. It is MIT licensed and targets `net10.0` only.

| Artifact | Path | Role |
| -------- | ---- | ---- |
| `PlutoniumBuild` package | `src/PlutoniumBuild.Package/` | The only packable project. Package ID `PlutoniumBuild`; packs the three libraries and the package readme. |
| `Plutonium.Core` | `src/Plutonium.Core/` | Engine: target runner, plan-then-execute, logging with secret masking, process runner plus terminal façade, path utilities, GitHub and Azure workflow emitters. `IsPackable=false`. |
| `Plutonium.Helpers` | `src/Plutonium.Helpers/` | Author layer: entries DSL, `AppId`, run contexts, versioning UX, and GitHub / Azure / local pipelines. `IsPackable=false`. |
| `Plutonium.Shell` | `src/Plutonium.Shell/` | CLI/app shell on `ApplicationBuilderHelpers` `4.1.160`: 16 verbs plus aliases, shared options, per-invocation state, exit mapping. `IsPackable=false`. |
| Future `PlutoniumBuild.*` integration packages | TBD | Reserved for transports and pipelines beyond GitHub + Azure. Not built in 0.1.0. |

The libraries remain multi-target (`net8.0` / `net9.0` / `net10.0`) for the
internal test matrix. The package is `net10.0` only.

## Package contents

`src/PlutoniumBuild.Package/PlutoniumBuild.Package.csproj` sets
`IncludeBuildOutput=false` and collects the library outputs into
`lib/net10.0`:

- `Plutonium.Core.dll`, `Plutonium.Core.xml`, `Plutonium.Core.pdb`
- `Plutonium.Helpers.dll`, `Plutonium.Helpers.xml`, `Plutonium.Helpers.pdb`
- `Plutonium.Shell.dll`, `Plutonium.Shell.xml`, `Plutonium.Shell.pdb`

The package also carries `README.md`, copied from `docs/package-readme.md`.
It carries no source files, no `PlutoniumBuild.dll`, and no separate
`Plutonium.*` package references. `IncludeSymbols` produces a `.snupkg`;
the publish workflow pushes the `.nupkg` with `--no-symbols`.

## Dependency rule

- `Plutonium.Core` is BCL-only.
- `Plutonium.Helpers` references `Plutonium.Core` only.
- `Plutonium.Shell` references `Plutonium.Core`, `Plutonium.Helpers`, and
  `ApplicationBuilderHelpers` `4.1.160`.
- The package project references all three libraries with
  `PrivateAssets="all"` and declares `ApplicationBuilderHelpers` `4.1.160`.
  That is the only dependency in the package manifest.

Consumers add one `PackageReference`:

```xml
<PackageReference Include="PlutoniumBuild" Version="0.1.0" />
```

## Builder version

`src/Directory.Build.props` sets `Version` to `0.1.0` and
`IsPackable=false`. The package project overrides `IsPackable=true`, so the
solution produces one distributable package.

That builder version is separate from the consumer app versions. Consumer
apps use per-app and per-environment tags such as `myapp/1.2.3`, or the
optional `versions.json` mode. The scaffold pins the builder version in
`build/build.csproj`; `build/update.*` moves that pin when the scaffold
points at a newer release.

## Publishing

`.github/workflows/publish.yml` is manual. Dispatch it with `confirm` set to
true from `main`; the job is skipped otherwise and on any other ref. It checks
out `refs/heads/main`, re-verifies the scaffold lock, runs the full build and
tests, packs the package, refuses to republish a version that already exists on
nuget.org, and pushes with the `NUGET_AUTH_TOKEN` repository secret scoped to
the push step. A fixed `publish-nuget` concurrency group serializes runs and
protects an in-progress run from cancellation, though a newer queued run can
still cancel an older pending run. The workflow uses no OIDC, no deployment
environment, and no GitHub Packages, tag, or release steps.

Publishing is release-gated: the 0.1.0 package must be pushed to nuget.org
before `dotnet add package PlutoniumBuild` resolves. The raw installer has a
separate gate: the repository must be public and the release commit must exist.
Publishing does not require a public repository.

See the authoritative scope in `docs/concepts/scope.md`.
