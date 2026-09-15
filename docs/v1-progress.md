# V1 execution checkpoint

This file is intentionally short. It exists so a new Codex session can continue the V1 program without reconstructing the full product history from a long chat.

## Current state

- Owner-accepted V1: **NOT COMPLETE**.
- Last production baseline: `v1.0.0` / `e82593144541c15f9c246014fe42ae45c7f35b02`.
- The production baseline is not the final V1 acceptance point; see `docs/v1-acceptance.md`.
- The P0 correction package is implemented on PR #2: medication CRUD/soft-delete, regimen CRUD with effective-dated versioning, atomic insufficient-stock rejection, and the unified activity/history surface.
- Final V1 still has additional acceptance gaps after that P0 package; do not stop after the P0 package.

## Resume protocol

When the owner says **"continue" / "kaldığın yerden devam et"**:

1. Read `AGENTS.md`, `docs/v1-acceptance.md`, and this file.
2. Inspect `git status` and the last few commits. Preserve any existing work; do not restart the implementation from scratch.
3. Resume the current incomplete V1 slice. Do not re-plan or re-audit the whole repository unless evidence shows the plan is stale.
4. Read only the code/docs needed for that slice. Avoid broad repository scans and repeated architecture research.
5. During implementation run targeted tests first. Run the full quality gate once the slice is coherent and before merge/release, not after every tiny edit.
6. Create a safe, coherent commit whenever a slice reaches a working checkpoint.
7. Update this file with the completed slice, evidence and exact next action before ending a long session or when usage is becoming constrained.
8. After a slice is complete, move to the next `OPEN`/`PARTIAL` V1 acceptance item. The project goal remains full V1, not completion of one task.
9. Never redefine an original V1 requirement as "later" merely to close the milestone. That requires explicit owner approval.
10. V1 may be declared complete only after all required rows in `docs/v1-acceptance.md` are `DONE` and the owner explicitly accepts the release.

## Current execution order

This is an execution order, not a scope reduction:

1. Finish the active P0 production-correction package without losing history or household authorization.
2. Verify medication full CRUD + soft-delete.
3. Verify regimen full CRUD + effective-dated versioning + historical administration integrity.
4. Enforce atomic no-negative-stock behavior.
5. Complete the unified medication/regimen/inventory/administration history view.
6. Reconcile `docs/v1-acceptance.md` against the implementation and implement the remaining original V1 gaps, prioritizing user-visible daily use and safety/reliability.
7. Complete Android physical-device offline/reminder acceptance, including reboot/time-zone/DST/permission recovery.
8. Run final full CI, production deployment and smoke tests.
9. Run the owner acceptance flow; only then mark V1 complete.

## Last checkpoint

- 2026-09-15: V1 scope was restored from the approved original product/MVP definition. The prior `v1.0.0` release remains a technical baseline, not owner-accepted V1.
- 2026-09-15: P0 correction implementation is on PR #2. Local evidence: `.NET` 9 passed/7 Docker-dependent skipped, web lint passed, mobile typecheck passed, web production build passed. GitHub Actions run `34984202695` passed all 16 API tests against PostgreSQL plus web/mobile checks and both Docker builds.
- 2026-09-15: PR #2 and deterministic package-order follow-ups #3/#4 are merged. Main CI run `35019127489` passed 16 API tests against PostgreSQL plus web/mobile checks, Docker builds and artifact upload.
- 2026-09-15: revision-preserving bulk inventory count/reconciliation is implemented on PR #5. CI run `35020426322` passed all 17 API tests against PostgreSQL plus web/mobile checks and Docker builds. The acceptance row is `DONE`.
- P0 production deployment remains operationally blocked because the current Codex host has no SSH agent identity (`Permission denied (publickey,password)`). Artifact SHA-256 `C3BC75716E21C5C0B0EF11998B691D597A6B0CE71265B74D525B71C416192E38` is staged locally; no VPS change was made.
- Next action: merge PR #5, then implement the `Lending / return` acceptance row with explicit ownership/allocation transitions and immutable audit/ledger evidence. Deploy the accumulated safe release and run production smoke as soon as project-scoped VPS SSH credentials are available.
