# ADR 0017 — One version line for the server and the web, a visible lag for mobile

- Status: accepted, 2026-10-05
- Deciders: owner ("Web ve mobilin karışmaması için her şeyin eşitlenmiş olduğu
  versiyonlar aynı isimde olsun"), agent
- Relates to: ADR 0015 (mobile deferred past V1), `docs/mobile-resume-plan.md`

## Context

The repository is one monorepo with three deployables: the API and the web client,
which ship together from one compose file on every merge to `main`, and the Expo mobile
client, which is deferred past V1 and has no release channel yet. Today the API declares
no version, the web says `0.1.0`, the mobile says `1.0.0`, and the only tag — `v1.0.0`
from 2026-09-14 — marks a technical baseline the owner rejected, not an accepted release.

The owner's instruction for the time after V1: web development may run ahead of mobile,
that must stay visible, and the versions where everything is at parity must share one
name — `v1.0.0`, `v1.0.1`, `v1.1.0`, `v1.1.1` and so on.

## Decision

1. **The server line.** The API and the web share one semantic version
   (`MAJOR.MINOR.PATCH`), kept in a root `VERSION` file, surfaced in the web footer and on
   `GET /api/version`, and tagged `vX.Y.Z` on `main` at the commit that is released
   under that number. They cannot be released apart: one compose file, one deploy.
2. **Mobile has its own number, which names the server version it is at parity with.**
   `apps/mobile/app.json` carries `X.Y.Z` only when the phone understands everything the
   server at `X.Y.Z` sends and offers the same user-facing behaviour for the surfaces it
   has. Its own fixes between parity points are `X.Y.Z+1` patches of that line. Mobile
   releases are tagged `mobile-vX.Y.Z`.
3. **Parity is a stated fact, not an assumption.** `MobileContractTests` pins the fields
   the phone reads; a mobile release at `X.Y.Z` must pass the contract of server
   `X.Y.Z`. `docs/mobile-resume-plan.md` lists what the phone still lacks against the
   current server; a mobile release claims parity only when that list is empty for the
   surfaces the phone has.
4. **The lag is visible.** While the web is at `v1.2.0` and the phone at `mobile-v1.1.0`,
   both say so in their About and both tags exist. Nothing is renamed to hide it.
5. **Semantics of the numbers.** PATCH: a fix with no new user-facing behaviour and no
   migration a client must know about. MINOR: new user-facing behaviour or a new field in
   the contract (additive). MAJOR: a change a client cannot ignore — a removed or
   renamed contract field, or a rule change the phone must mirror (the governing-version
   rule of Sprint 7 would have been one, had the phone been released).

## The first number

The accepted web V1 is the first real release and the owner calls it `v1.0.0`. The tag
`v1.0.0` already exists on the rejected 2026-09-14 baseline. Two honest ways to resolve
this, for the owner to choose when accepting the release:

- Retire the old tag and its GitHub release (they were never distributed to anyone) and
  create `v1.0.0` on the accepted commit, recording the retirement in the release notes.
- Keep history untouched and call the accepted release `v1.0.1`, noting that `v1.0.0`
  was the retired baseline.

**Decided by the owner on 2026-10-05, with the acceptance:** history stays untouched.
`v1.0.0` remains the retired 2026-09-14 baseline and the accepted release is **`v1.0.1`**
("eskiler v1.0.0, bundan sonra yapacaklarımız v1.0.1"). The mobile client therefore aims
at `mobile-v1.0.1` parity first and then follows the server line.

## Consequences

- Done at the `v1.0.1` cut (2026-10-08): the root `VERSION` file is the one source; the
  API project bakes it into the assembly and `GET /api/version` reports it with the commit
  the image was built from; `next.config.ts` bakes it into the web footer; the phone shows
  the server version it is at parity with from its `app.json`. Release notes live under
  `docs/releases/`.
- Release notes per tag list the migrations included, because a self-hosted operator
  reads them before upgrading.
- The Notion task property `Version` follows the server line for web work and the
  `mobile-v` line for mobile work.
