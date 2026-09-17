# V1 execution checkpoint

This file is intentionally short. It exists so a new Codex session can continue the V1 program without reconstructing the full product history from a long chat.

## Current state

- Owner-accepted V1: **NOT COMPLETE**.
- Last verified production baseline: `v1.0.0` / `e82593144541c15f9c246014fe42ae45c7f35b02`. Do not infer that later merged changes are deployed.
- The production baseline is not the final V1 acceptance point; see `docs/v1-acceptance.md`.
- P0 medication CRUD/soft-delete, regimen versioning, atomic insufficient-stock rejection and unified activity/history were merged in PR #2.
- Revisioned bulk inventory counts were merged in PR #5; whole-package lending/returns were merged in PR #6 (`dc878cb`). **Do not attempt to merge PR #6 again.**
- Schedule work remains on open PR #9, outside the main-branch deployment candidate. Production still runs `e825931`; no newer main commit has been deployed.
- PR #10 fixes the malformed package-loan migration SQL and gates the built API image's actual migration script against isolated PostgreSQL. Production migration and deployment remain pending separate owner approval.
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

- Verify the merged main CI artifact checksum, image commit labels, and packaged migration SQL against the successful isolated-PostgreSQL CI gate. Request the owner's separate approval before any production migration or deployment. Keep PR #9 out of that release.
- After the separately authorized deployment turn, resume the remaining `OPEN`/`PARTIAL` V1 rows from this file and `docs/v1-acceptance.md`; this does **not** shrink owner-defined V1.

## Evidence checkpoint

- 2026-09-15: Original owner-approved V1 scope restored; earlier `v1.0.0` remains only a technical baseline.
- 2026-09-15: PR #2 merged after PostgreSQL CI run `34984202695` (16 API tests), web/mobile checks and Docker builds; deterministic follow-ups #3/#4 merged. Main CI `35019127489` passed.
- 2026-09-15: PR #5 revisioned bulk inventory counts merged; PostgreSQL CI `35020426322` passed 17 API tests and web/mobile/Docker checks.
- 2026-09-16: PR #6 whole-package lending/returns merged (`dc878cb`); PostgreSQL API and web/mobile/Docker CI `35084942604` passed on the PR. Consult its exact CI evidence for new merge commit if necessary.
- 2026-09-16: Corrected an outdated instruction to merge PR #6 and added an explicit bounded-turn handoff. The original V1 scope and required acceptance criteria are unchanged.
- 2026-09-17: Production preflight for main `33056dd` verified CI artifact ZIP SHA-256 `5da9591c...` separately from image tar.gz SHA-256 `d2e850af...`. The packaged `migrations.sql` was malformed at the package-ownership backfill; deployment stopped before loading images or applying migrations. A fresh backup `.deploy/backups/pre-deploy-33056dd-20260916T221017Z.dump` passed `pg_restore -l`. Production checkout/images and seven applied migrations remained unchanged; other projects' container states were unchanged apart from elapsed time on an already-restarting container.
- 2026-09-17: PR #10 corrects the migration source SQL terminator and adds CI coverage that executes the exact SQL extracted from the built API image twice on blank PostgreSQL 18 and twice over a synthetic seven-migration baseline, checking package ownership, count sessions, and ledger preservation. PR CI run `35210867794` passed API, web/mobile, Docker, and packaged-SQL PostgreSQL checks. No production migration or deployment was attempted.
