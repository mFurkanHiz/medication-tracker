# Medication Tracker agent guide

## Scope and isolation

- Work only inside this repository unless the user explicitly authorizes another target.
- Do not read or change sibling repositories by default.
- Do not change VPS services, GitHub settings, Notion records, DNS, or secrets unless the current request explicitly includes that action.
- Treat `PROJECT.md` and `docs/` as the durable project context. Record material decisions as ADRs.

## V1 execution contract

- For any V1 work, treat `docs/v1-acceptance.md` as the authoritative owner-defined V1 scope and `docs/v1-progress.md` as the resumable execution checkpoint.
- A tag, deploy, green CI run, smoke test, sprint closure, or partial vertical slice does **not** by itself mean V1 is complete.
- Do not move a required V1 criterion to a later release, narrow it, or mark it optional unless the owner explicitly approves that scope change.
- When the owner says `continue` / `kaldığın yerden devam et`, resume from the current working tree plus `docs/v1-progress.md`; do not reconstruct or redesign the whole project from chat history.
- Read only the code and documentation needed for the current V1 slice. Avoid broad repeated repository audits, repeated external research, and repeated architecture analysis unless new evidence makes them necessary.
- Use targeted tests while editing. Run the full quality gate once a coherent slice is ready for checkpoint/merge/release instead of repeatedly after every tiny change.
- Create safe coherent commits during long work. Before ending a long session or when usage is constrained, leave a clean checkpoint and update `docs/v1-progress.md` with evidence and the exact next action.
- Completing one task does not complete the project. After each slice, continue through the remaining `OPEN`/`PARTIAL` rows in `docs/v1-acceptance.md` until all required rows are `DONE`.
- Never declare owner-accepted V1 complete until the acceptance document is fully satisfied and the owner explicitly accepts the release.

## Product boundary

- This is a medication organization and adherence product, not a diagnostic or prescribing system.
- Never implement automatic dose recommendations or medical conclusions.
- Use synthetic data in source control, tests, screenshots, and demos. Never commit real health data.
- Preserve an audit trail for treatment, administration, inventory, and permissions changes.

## Architecture rules

- Keep the server as a modular monolith until measured needs justify separation.
- Mobile is offline-first. User actions write locally first and sync later.
- Inventory is ledger-based. Never silently overwrite stock history.
- Inventory counts create count sessions and reconciliation entries. Corrections create a new revision; they do not rewrite an accepted historical event.
- Treatment schedules are effective-dated versions. Editing a schedule must not alter its historical period.
- Store quantities exactly. Do not use binary floating-point for medication amounts.
- Model household membership and authorization from the beginning, even while the initial UI defaults to one household.
- User identity, household membership, and future paid subscription/entitlement are separate concepts.
- All user-facing text must use translation keys. Turkish and English are the initial locales.

## Quality gates

Run the relevant checks before proposing a commit:

```text
dotnet test MedicationTracker.slnx
pnpm lint
pnpm typecheck:mobile
pnpm build:web
```

Add tests for dose generation, fractional quantities, inventory conservation, count reconciliation, sync idempotency, authorization, time zones, daylight-saving transitions, and reminder rescheduling.

## Generated framework guidance

- Follow more specific `AGENTS.md` files under an app directory.
- For Next.js, read the installed version's guidance under `apps/web/node_modules/next/dist/docs/` before changing web code.
- For Expo, consult the exact installed SDK documentation before changing native configuration.

## Git and documentation

- Use small, coherent commits with imperative messages.
- Do not commit secrets, local databases, build artifacts, certificates, keystores, or real medical documents.
- Update `PROJECT.md`, ADRs, and the relevant Notion task when scope or architecture changes materially.
