# ADR-001: Evaluation is pure and proposes changes; applying them is a separate act

**Status:** Accepted

## Context
The legacy `CheckModel` mutates the object *during* validation:

- `ChangeProperty` writes the normalized GSM number **before** the length and regex checks, so a
  rejected input is still rewritten. For example, `"+90 488 888 88 88"` becomes `"4888888888"` and
  is then rejected.
- `ChangeIfDefault` overwrites dates unconditionally.
- A caller cannot ask "what *would* change?" without the change happening.

Yet the normalization intent is real: 4 of the 14 attribute parameters exist to canonicalize
values.

## Decision
`ConformanceEngine.Evaluate(root)` never writes to the graph.

- For each member, normalizers compute the **canonical** value, and rules judge that canonical
  value.
- If the canonical value differs from the raw value, a
  `ProposedChange(path, before, after, steps)` is recorded.
- `ConformanceReport.Apply()` writes the proposed changes, but only if the member still holds
  `before` (optimistic concurrency). It returns an `ApplyResult` listing applied and skipped
  changes.

Evaluation-only normalization is a separate step kind called a **lens** (legacy
`ChangeProperty=false`). It shapes the value the rules see but never proposes a change.

A normalizer may fail. Failure produces a `NormalizationFailed` finding, and the member's rules are
skipped.

## Alternatives considered
- **Mutate in place during evaluation** (legacy). Rejected: it is order-dependent, not
  reproducible, surprising, and impossible for immutable inputs.
- **Return a normalized deep copy of the graph.** Rejected: this needs a generic deep clone of
  arbitrary graphs (constructors, init-only members, cycles). That is a mapper's job.
- **Separate `Normalize()` and `Validate()` passes.** Rejected: rules must judge the canonical
  value, so both passes would compute normalization and could disagree.

## Consequences
- Evaluation is deterministic and has no side effects. Reports can be logged, diffed, or shown to
  users before anything changes.
- `Apply()` is explicit ceremony. If callers always apply unconditionally, the split has no value
  (kill criterion #3).
- Members without setters still get proposed changes (`CanApply == false`). This is useful for
  immutable models.

## Rejected alternatives
A mutating `Normalize(obj)` shortcut is rejected for now. It is exactly `Evaluate(x).Apply()`, so
it adds no concept.

## Evidence from the legacy repository
- `SmartStringAttribute.ChangeProperty`, `OnlyDigit` and `SubStringIndex`
- `SmartDateTimeAttribute.ChangeIfDefault`
- the write at `ValidationHelper.cs:83`, which happens before the checks at lines 87–115
- the unconditional write at `ValidationHelper.cs:156`
