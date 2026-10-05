# ADR 0016 — "Do not take with" tags, matched among a person's medicines

- Status: accepted, 2026-10-05
- Deciders: owner (decision D1 of the 2026-10-05 planning round), agent
- Relates to: ADR 0014 (package-first model), `docs/domain-model.md` product boundary,
  `docs/owner-feedback-2026-10-05.md`

## Context

The household can write prose caution notes on a medicine — what not to take it with,
what to avoid eating, what to do and not do, and a warning. Those notes are stored and
shown and never evaluated: the code's own contract says they are not parsed, not matched
against a drug database, not cross-referenced with another medicine, and not checked
before a dose is recorded. That restraint is the product boundary, not a shortcut.

Testing the live site, the owner asked for something more structured beside the prose:
a tag list of medicine names and active ingredients not to combine with this one, such
as `ligone, parol, paracetamol, cvitamine`, and a red warning on the Today screen when
the same person has one of those on the same day — on Allerset's row "Parol ile beraber
almayınız, nedeni: Parol, Paracetamol", on Parol's row the same with "Allerset" as the
reason. If the person takes it anyway, "alırsa alır, ama biz uyarırız".

This touches the boundary and therefore needed an explicit decision rather than a quiet
implementation.

## Decision

The product gains a structured **do-not-take-with tag list** on the medication
definition, beside the prose notes, and a **person-scoped, day-scoped warning** on the
Today row. The boundary wording becomes: the product never invents a drug interaction;
it may remind the household of its own words.

The constraints that keep this inside the boundary are part of the decision:

1. **Both halves of every match are the household's own record.** A tag on one
   medicine is matched against the name, brand and active ingredients the household
   typed on another. The software supplies no vocabulary of its own.
2. **Matching is plain equality** on a normalised form: case folded, diacritics
   removed, spaces and punctuation dropped, the Turkish dotted and dotless i treated
   as one letter. No synonyms, no stemming, no drug database, no inference. A word the
   household did not write cannot match: `cvitamine` does not match `C Vitamini`.
3. **Scope is the person and the day.** The other medicine must be due for the same
   person on the same day. A household cabinet holds other people's medicines, and
   the owner's question was about medicines that fall together.
4. **The warning attributes the claim to the household.** It names the other medicine,
   the words that matched, and whose tag it came from. It never says "these interact".
5. **It never blocks.** Recording a dose stays possible with the warning on the row,
   for the reason the minimum-gap warning gives: a dose somebody took must be
   recordable, or the ledger lies about the one thing it exists to remember.
6. **Silence is not a safety claim.** Every warning and the tag field's hint say that
   no match means only that nothing the household wrote matched. The footer disclaimer
   says the app assesses no interactions and only reminds the household of its own
   tags.
7. **The prose notes stay unevaluated.** The tag list is the one structured field that
   is matched; it lives beside the prose, not inside it, so the prose contract from
   Sprint 3 holds unchanged.

Matching runs live inside the Today projection (`DoNotTakeWithMatcher`), where the
person's rows for the day already are; it is a handful of string comparisons per
request and needs no "analyse" button, which the owner had offered as a fallback.

## Consequences

- One migration (`do_not_take_with_tags text[]` on the definition, defaulting to an
  empty list). Workspace, export and the audit snapshot carry the tags.
- The Today row gains `conflicts`, an array that is empty when nothing matched. The
  mobile contract test pins existing fields only, so the deferred client is unaffected
  until it chooses to render the warning.
- The boundary paragraphs in `docs/domain-model.md`, `docs/v1-acceptance.md` and
  `PROJECT.md` are amended as above, in one sentence each.
- The risk the original restraint named remains and is accepted knowingly: a household
  that sees the warning work once may trust its absence. The mitigations are the
  attributed wording, the hint under the tag field and the footer disclaimer, and they
  are the reason this is a reminder of the household's own note and nothing more.
