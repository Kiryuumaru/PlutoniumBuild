# AGENTS.md — PlutoniumBuild Agent Rules

> Status: Phase-1 landed. `src/Plutonium.Core`,
> `src/Plutonium.Helpers` exist with Phase-1 code; `tests/`, `samples/`
> remain **planned**.
> Pins: `global.json` SDK `10.0.100` / `src/Directory.Build.props` version
> `0.1.0` — do not invent release status.

On conflict, **Section 9 Safe-agent overrides win**. On violation, fail fast
and state it.

## 1. Project map

```text
/
├── AGENTS.md               # this file — agent working agreements
├── README.md               # purpose, inspirations, planned packages, status
├── PlutoniumBuild.sln     # solution (Phase-1 landed)
├── global.json             # SDK pin only: 10.0.100, rollForward latestMajor
├── src/
│   ├── CONCEPT.md          # 1-page recreate intent
│   ├── Directory.Build.props # version/LangVersion/TFM settings
│   ├── Plutonium.Core/     # LANDED Phase-1 — runner, CLI, logging, path, process
│   └── Plutonium.Helpers/  # LANDED Phase-1 — entry stubs, AppId, run contexts
├── tests/                  # PLANNED
├── samples/                # PLANNED
├── docs/
│   ├── index.md            # docs entry point
│   ├── packages.md         # Phase-1 package status
│   ├── agents/             # workflow config (issue-tracker, triage-labels, domain)
│   └── concepts/
│       └── scope.md        # AUTHORITATIVE in/out scope for 0.1.0
└── .inspirations/
    ├── NukeBuildHelpers/   # read-only audit ref (main @ 5c17b96)
    └── NukeBuild/          # read-only audit ref (develop @ 27e8077)
```

## 2. Scope / doc authority

- Authoritative scope: `docs/concepts/scope.md`. It defines what is in
  (target runner, entries, AppId versioning, GitHub/Azure workflow gen) vs out
  (`Nuke.Common` dependency, copy-paste). Mark open questions there; do not
  invent scope elsewhere.
- `src/CONCEPT.md` states recreate intent (1 page).
- `docs/packages.md` states Phase-1 package status (Core vs Helpers vs future).
- Code is source of truth; docs follow code. Never
  document an unbuilt feature as built.
- Inspirations under `.inspirations/` are read-only audit references. Audited
  recreation only — no verbatim copy.

## 3. Commands

- `dotnet build PlutoniumBuild.sln` — build landed Phase-1 `src/` projects.
- `dotnet test` — run `tests/` once they land (still planned — do not claim
  they pass today).

Docs tooling: none yet. Record verification as `Not run (docs-only)` unless
project docs tooling is added.

## 4. Quality gate

Applies to landed code; state as gate, not current status:

- `0 warnings / 0 errors` on build.
- `100%` tests pass.
- `tests/` still planned: gate is aspirational until they land — do not claim it is met.

## 5. C# standards

- Null-gating: guard public entry boundaries; no unchecked null dereference.
- Primary constructors where idiomatic; keep DI-friendly.
- XML docs on all public cross-layer surface (Core ↔ Helpers ↔ consumers).
- LangVersion / TFM placeholders live in `src/Directory.Build.props`.

## 6. Commenting

Banned in code and docs unless explicitly requested:

- `TODO`, `HACK`, `FIXME`, `#pragma` (disable/warning) — do not add.
- Explain *why* where non-obvious; never narrate *what* the code obviously does.

## 7. Working agreements

- Tools over memory — verify every claim against the repo / inspirations via
  reads, not recollection.
- Verify before claiming — check the file/line before asserting behavior.
- Docs follow code — once code lands, code wins; fix docs, never silently
  change code to match docs.
- Never commit `investigate/` scratch output; keep the tree clean.
- One file at a time; docs only unless explicitly tasked otherwise.
- No git writes by default (see §9).

## 8. Skills index

- Agent workflow config lives in `docs/agents/` (`issue-tracker.md`,
  `triage-labels.md`, `domain.md`); see `## Agent skills` below.
- No skills index file exists yet in this repo. When one is added (e.g.
  `SKILLS.md` or `docs/skills.md`), point to it here and follow it.
- Until then, this AGENTS.md is the sole agent ruleset plus §9 overrides.

## Agent skills

### Issue tracker

Issues live as GitHub issues via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary (`needs-triage` / `needs-info` / `ready-for-agent` / `ready-for-human` / `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout (`CONTEXT.md` + `docs/adr/` created lazily). See `docs/agents/domain.md`.

## 9. Safe-agent overrides (win on conflict)

These override §§1–8 on any conflict. Violations fail fast with an explicit
statement.

1. **Scout-if-unfamiliar → echo → wait → execute.** If unfamiliar, scout
   read-only first, echo understanding with file/doc refs, wait for explicit
   confirmation (`yes`/`correct`/`go ahead`), then execute. Never self-confirm.
2. **Questions are questions, not actions.** A `?` asks; do not act on it as
   an instruction.
3. **Own mistakes first.** When something fails, check your own steps before
   blaming tools/repo/user; state what you got wrong.
4. **Read project docs before acting.** Read `README.md`, this file,
   `docs/concepts/scope.md`, and relevant `docs/` before modifying anything.
5. **Verify after every action.** Re-read edited files; confirm claims and
   links resolve. Docs builds/link-checks when tooling exists, else record
   `Not run (docs-only)`.
6. **Keep docs in sync.** Every behavior-affecting change updates the
   corresponding doc in the same task; never leave docs stale.
7. **Debug honestly.** Report what you actually observed; do not invent
   passing results or hide failures.
8. **Always respond visibly.** Every turn produces a visible summary;
   no silent no-ops.
9. **No git writes by default.** Read-only git (`status`, `diff`, `log`) is
   allowed. No `commit` / `push` / `amend` / PR writes unless explicitly
   requested.
