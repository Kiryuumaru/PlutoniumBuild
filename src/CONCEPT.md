# CONCEPT — PlutoniumBuild (1 page)

> Phase-1 landed (`src/Plutonium.Core/`, `src/Plutonium.Helpers/`);
> `tests/`, `samples/` remain planned. Code is source of truth.

Recreate the `NukeBuild` + `NukeBuildHelpers` build-author UX as
PlutoniumBuild: named targets with dependencies, Helpers-style entries
(`BuildEntry` / `TestEntry` / `PublishEntry`), `AppId`-keyed multi-app
builds, versioning (`version` / `bump` / status), and GitHub Actions +
Azure Pipelines workflow generation — on a standalone runner with **no
`Nuke.Common` dependency**. Audit `.inspirations/` for behavior; write
clean APIs. Authoritative scope: `docs/concepts/scope.md`.
