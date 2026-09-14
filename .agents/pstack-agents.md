# pstack agents — project-local routing (opencode)

> Adapted from `.agents/skills/setup-pstack/SKILL.md` (74 lines).
> Budget: unspecified / inherit (no budget chosen).
> Models replaced by fit-based routing: each role runs on whoever fits the task or question.
> Scope: project-local only (`.agents/pstack-agents.md`). Never `~/.cursor/` or `~/.config/`.
> No project `opencode.json` exists; variety lives in personal agents.

## Routing rule

For every role below, route to whoever fits the task or question.

## Roles

- `feature, refactoring` — implement features and small refactors — whoever fits the task or question.
- `bug-fix` — diagnose and fix a reported failure — whoever fits the task or question.
- `perf-issue` — investigate and improve a performance bottleneck — whoever fits the task or question.
- `hillclimb` — iterate incrementally toward a better solution — whoever fits the task or question.
- `judgment and prose` — exercise judgment and write clear prose — whoever fits the task or question.
- `hardest tasks` — take on the most demanding work — whoever fits the task or question.
- `how explorer` — explore how the system works — whoever fits the task or question.
- `how explainer` — explain how the system works — whoever fits the task or question.
- `why investigators` — investigate why behavior occurs — whoever fits the task or question.
- `why synthesizer` — synthesize investigation findings into an answer — whoever fits the task or question.
- `reflect tooling` — run reflection tooling over prior work — whoever fits the task or question.
- `reflect judgment, divergent, synthesizer` — judge, diverge, then synthesize on reflection — whoever fits the task or question.
- `arena runners` — run competing approaches in parallel — whoever fits the task or question.
- `arena cross-judge pool` — cross-judge arena outputs from a differing perspective — whoever fits the task or question.
- `swarm workers` — fan out as parallel workers on the task — whoever fits the task or question.
- `architect runners` — run architecture-level design approaches — whoever fits the task or question.
- `interrogate reviewers` — review by interrogating assumptions and gaps — whoever fits the task or question.
