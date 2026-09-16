# V1 execution checkpoint

This file is intentionally short. It exists so a new Codex session can continue the V1 program without reconstructing the full product history from a long chat.

## Current state

- Owner-accepted V1: **NOT COMPLETE**.
- Last verified production baseline: `v1.0.0` / `e82593144541c15f9c246014fe42ae45c7f35b02`. Do not infer that later merged changes are deployed.
- The production baseline is not the final V1 acceptance point; see `docs/v1-acceptance.md`.
- P0 medication CRUD/soft-delete, regimen versioning, atomic insufficient-stock rejection and unified activity/history were merged in PR #2.
- Revisioned bulk inventory counts were merged in PR #5; whole-package lending/returns were merged in PR #6 (`dc878cb`). **Do not attempt to merge PR #6 again.**
- Selected-weekday and N-day interval treatment schedules are committed on `codex/v1-schedule-patterns` and proposed in PR #9. The PR is open, not merged or deployed; the `Treatment schedules` acceptance row remains `PARTIAL` until the branch is reviewed and integrated.
- Final V1 still has additional acceptance gaps; do not stop or declare V1 complete because a subset is merged.

## Resume protocol

When the owner says **"continue" / "kaldığın yerden devam et"**:

1. Read `AGENTS.md`, `docs/v1-acceptance.md`, and this file.
2. Inspect `git status` and the last few commits. Preserve any existing work; do not restart from scratch.
3. Resume the current incomplete V1 slice. Do not re-plan or re-audit the whole repository unless evidence shows the checkpoint is stale.
4. Read only the code/docs needed for the current slice. Avoid broad repository scans and repeated architecture research.
5. Run targeted tests during implementation; run full quality gates for a coherent slice before merge/release, not after every tiny edit.
6. Create a safe, coherent commit when a slice reaches a working checkpoint.
7. Update this file with evidence and an exact next action, then **stop the current Codex turn** as instructed in `AGENTS.md`. The overall project remains full V1 and continues on separately authorized turns.
8. Never redefine a required V1 item as "later" without explicit owner approval. V1 is complete only after every required acceptance row is `DONE` and the owner explicitly accepts it.

## Next exact action

- Review PR #9 (`codex/v1-schedule-patterns`) and its synthetic weekday/interval/DST flow in the web UI. If review finds a schedule defect, fix it on that PR and rerun CI; otherwise merge the passing PR, then record the merge evidence and update the `Treatment schedules` acceptance row. Do not treat an open PR as a deployed or owner-accepted release.
- Stop this turn at the schedule checkpoint. A subsequent separately authorized turn resumes from the current tree and acceptance contract; no other V1 feature starts during this turn.
- Deployment is separately blocked by unavailable project-scoped VPS SSH authentication in the Codex host (`Permission denied (publickey,password)`). Do **not** repeatedly attempt the same SSH connection or change server-wide settings. Preserve the evidence and request a scoped access fix from the owner when deployment is the next needed step. No recent production deployment has been verified here.

## Evidence checkpoint

- 2026-09-15: Original owner-approved V1 scope restored; earlier `v1.0.0` remains only a technical baseline.
- 2026-09-15: PR #2 merged after PostgreSQL CI run `34984202695` (16 API tests), web/mobile checks and Docker builds; deterministic follow-ups #3/#4 merged. Main CI `35019127489` passed.
- 2026-09-15: PR #5 revisioned bulk inventory counts merged; PostgreSQL CI `35020426322` passed 17 API tests and web/mobile/Docker checks.
- 2026-09-16: PR #6 whole-package lending/returns merged (`dc878cb`); PostgreSQL API and web/mobile/Docker CI `35084942604` passed on the PR. Consult its exact CI evidence for new merge commit if necessary.
- 2026-09-16: Corrected an outdated instruction to merge PR #6 and added an explicit bounded-turn handoff. The original V1 scope and required acceptance criteria are unchanged.
- 2026-09-17: Schedule checkpoint `afb7d02` adds daily/weekday/interval recurrence, effective-date and administration validation, recurrence-aware forecast, TR/EN web management, EF migration, and synthetic tests. Main's newer instructions/checkpoint were integrated without discarding local work in merge `f9862fc`. Local gate: `.NET` 16 passed, 10 PostgreSQL-dependent skipped; web ESLint, mobile TypeScript, and web webpack production build passed. PR #9 CI run `35151804861` passed PostgreSQL API tests, standard web/mobile checks and both Docker builds. No VPS deploy was attempted; PR remains open and V1 remains incomplete.
