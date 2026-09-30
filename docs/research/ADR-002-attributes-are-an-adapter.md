# ADR-002: Attributes are one metadata adapter, and it reuses DataAnnotations

**Status:** Accepted

## Context
The legacy system can only be configured through three custom attributes: `SmartString`,
`SmartDateTime` and `SmartChildClass`.

- They encode an ordered pipeline implicitly, through the order of `if` statements in the engine.
- They cannot express cross-property rules.
- They cannot express relative dates, except through one hard-coded flag.
- They cannot express custom checks.
- Adding a rule requires editing the engine.

Meanwhile, the BCL already has a stable, widely known constraint vocabulary
(`System.ComponentModel.DataAnnotations`), and .NET 10 validates it recursively out of the box.

## Decision
The core (`Conform`) knows nothing about attributes. It consumes a `TypeContract` produced by one
of two things:

1. The typed code builder (`ContractBuilder<T>`). This is the primary authoring surface.
2. Any `IContractSource`.

`Conform.Annotations` is an `IContractSource` that reads two kinds of attribute.

**BCL DataAnnotations:**
- `[Required]` becomes the member's presence requirement.
- Every other `ValidationAttribute` (built-in or custom) is wrapped as a rule. It is evaluated
  against the *canonical* value.

**Additions for what DataAnnotations lacks:**
- Normalization attributes (`[Trim]`, `[DigitsOnly]`, `[KeepLast]`, `[DefaultIfMissing]`, or any
  `NormalizeAttribute` subclass). Any of them can be marked as a lens.
- `[Descend]` for traversal edges.
- `[SkipIn("scenario")]` for scenario-based activation.
- `RuleAttribute` subclasses, for native structured findings.

Reflection does not guarantee attribute order. So **a member with more than one normalizer must
give them distinct `Order` values**. Otherwise contract construction fails with a
`ContractDefinitionException`. Silently guessing the order is not acceptable.

## Alternatives considered
The models considered were A (attributes only), B (fluent only), C (external JSON), E (expression
trees) and F (source generation). They are evaluated in ARCHAEOLOGY §8.

## Consequences
- Existing DataAnnotations models work immediately, and gain normalization and graph traversal.
- A future source generator or JSON source can produce the same `TypeContract` without engine
  changes.
- There are two authoring styles to document.
- DataAnnotations rules keep the DataAnnotations message format.

## Rejected alternatives
- **Reviving the `Smart*` attributes.** They are bags of sentinel values with implicit ordering.
- **Inventing a new `[Required]`.** It would needlessly collide with the BCL type name.
- **A legacy-compatible `Smart*` adapter.** Most legacy semantics are bugs, so the adapter would
  preserve them. See `docs/MIGRATION.md`.

## Evidence from the legacy repository
- `ValidationHelper.cs:34` and `ValidationHelper.cs:122` select rules by CLR type.
- `ValidationHelper.cs:46` gates every string check on presence.
- `-1` sentinel parameters.
- `AttributeTargets.Field` is declared, but fields are never scanned.
- There is no way to express "EndDate ≥ StartDate".
