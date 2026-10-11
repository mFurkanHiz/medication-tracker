# ADR 0018 — The one deployed environment is a test environment

- Status: accepted, 2026-10-10
- Deciders: owner ("Burada canlı bir ortam yok, biz test ortamında çalışıyoruz bu
  session'da ve bu projede. Şimdilik böyle. Bu nedenle bana sormadan canlıya al."), agent
- Relates to: ADR 0017 (versioning), `docs/v1-progress.md` ("How the owner wants this
  project run")

## Context

The shared Notion rules (Mimari ve Standartlar, Proje Çalışma Prosedürleri) describe a
`dev → SERVER_TEST → SERVER_PRODUCTION` branch model in which production changes only when
the owner says "canlıya al". This project has one deployed environment, at
`medicationtracker.rapidconfigs.com`, that every merge to `main` updates by itself. A
separate test subdomain is reserved but not set up.

## Decision

1. The one deployed environment is treated as a **test environment**. There is no live
   environment for this project yet.
2. Merging to `main` therefore deploys to it without asking the owner. The "canlıya al"
   gate does not apply until a real production environment exists.
3. This is a project-specific exception to the shared branch model, recorded here and on
   the Notion project page. Everything else in the shared rules keeps applying: nothing is
   done to other projects on the shared server, secrets live only in the HASSAS page,
   synthetic data only in source control, and the deploy keeps its backup, health check
   and outsider-container check.
4. When a real production environment is wanted, a new ADR supersedes this one and the
   shared model (separate test deploy, production only on "canlıya al") applies from then.

## Consequences

- The Notion Backlog task asking for a branch-model decision is closed by this ADR.
- Real health data must not be entered while the environment is a test one; the product
  rule about synthetic data in source control is unchanged.
