# CONCEPT — PlutoniumBuild (1 page)

> Developer documentation — for maintainers, not end users.
>
> Phase-B landed (`src/Plutonium.Core/`, `src/Plutonium.Helpers/`,
> `src/Plutonium.Shell/`); `tests/` landed, `samples/` remains planned.
> Code is source of truth.

Recreate the `NukeBuild` + `NukeBuildHelpers` build-author UX as
PlutoniumBuild: named targets with dependencies, Helpers-style entries
(`BuildEntry` / `TestEntry` / `PublishEntry`), `AppId`-keyed multi-app
builds, versioning (`version` / `bump` / status), and GitHub Actions +
Azure Pipelines workflow generation — on a standalone runner with **no
`Nuke.Common` dependency**. The CLI/app shell is built on
`ApplicationBuilderHelpers` (`4.1.160`) and owned by `Plutonium.Shell`;
`Plutonium.Core` stays BCL-only and `Plutonium.Helpers` stays
library-only. Audit `.inspirations/` for behavior; write
clean APIs. Authoritative scope: `docs/concepts/scope.md`.
