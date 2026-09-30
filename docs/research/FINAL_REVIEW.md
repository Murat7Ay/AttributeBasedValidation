# Final Review

## What We Found

The 2019 repository is a ~230-line validation helper. It is worse than it looks:

- Its own README example fails its own validation (`"NestedChild null or empty"`).
- Its `Child` model is unsatisfiable: every child requires another child.
- It crashes the process on cycles.
- It mutates input *before* the checks that then reject it, and reports regex failures as length errors.
- It overwrites dates unconditionally under a flag called `changeIfDefault`.
- Its "ignore" scenario flag silently stops working one level down.

All of this is pinned by 16 characterization tests against the unmodified sources.

Constraint checking accounts for about 70% of its surface: required, length, regex, date, child recursion and
collection count. That part is **obsolete**. DataAnnotations covers the rules, FluentValidation covers code-first
graphs, and .NET 10's source-generated `Microsoft.Extensions.Validation` walks nested DataAnnotations models out of
the box.

One idea survives. It is hidden in `SmartString(onlyDigit, subStringIndex, changeProperty)`: **judge a value by its
canonical form, and treat committing that form as a separate decision.** `changeProperty = false` is the tell. The
author needed to validate "as digits" without storing digits. This is "parse, don't validate", as done by Pydantic
and Zod, applied in place to an existing .NET object graph. The mainstream .NET validators do not model it.

## What We Built

`Conform` is a contract interpreter over object graphs.

- **`src/Conform`** (dependency-free, AOT-compatible): about 1,370 lines including XML docs. That is 3.3× the
  legacy line count. It is simpler per concept, not smaller.
  - rules and normalizers as immutable values
  - a typed builder
  - an inspectable metadata model (`TypeContract`, `MemberContract`)
  - a registry with pluggable sources
  - an explicit-stack, identity-tracking engine
  - an immutable report of findings and proposed changes, with conflict-checked `Apply()`
- **`src/Conform.Annotations`**: an adapter that reads BCL DataAnnotations. `[Required]` becomes presence, and any
  other `ValidationAttribute` becomes a rule judged against the canonical value. It adds normalization, traversal and
  scenario attributes.
- **Tests**: 104 behavioral tests and 16 (+1 skipped) characterization tests.
- **Also**: a realistic example, BenchmarkDotNet comparisons, and three ADRs.

## What We Deliberately Did Not Preserve

- the `Smart*` attributes and their sentinel/boolean parameter bags
- rule selection by CLR type
- presence-gating of all other checks
- mutation during evaluation
- the unconditional date overwrite
- `IsCollection`
- non-generic `ICollection` detection
- the first-error-only string result
- hard-coded domain messages
- the unsatisfiable sample model

There is also no compatibility adapter. Its rationale is in `docs/MIGRATION.md`: it would either reproduce the bugs
or silently change behavior under the old names.

## Why The New Architecture Is Different

The legacy code was one function that interleaved five concerns: discovery, reading, canonicalizing, judging and
writing. The new architecture separates them along the seams the archaeology found:

1. **Metadata source ≠ engine** (ADR-002). Attributes are one `IContractSource`. The core assembly contains no
   attribute types and references no annotation assembly; a test asserts this.
2. **Evaluation ≠ mutation** (ADR-001). Normalization output is data (`ProposedChange`). Writing is an explicit act
   that checks for conflicts.
3. **Graph ≠ tree** (ADR-003). Traversal is identity-visited and stack-safe.
4. **Normalize ≠ lens ≠ constrain.** These are three step kinds with distinct semantics. A failed normalization is
   its own finding kind.
5. **Violation ≠ fault.** "The data is wrong" and "the engine could not decide" are never conflated.

**Did we discover a better abstraction, or just rewrite a validation library?**

Honestly, the constraint half *is* a rewrite of a validation library. Its value comes only from better semantics:
structured findings, paths, cycles and a deterministic clock.

The canonical-form half is the discovery:

- the proposed-change model
- the lens distinction
- normalization failure as a kind of its own
- one declaration that yields both the verdict and the fix

Without that half this project should not exist. With it, it is a small but real addition to the .NET ecosystem.

## Legacy Compatibility

- **Source:** none, by decision. The migration table covers every legacy parameter.
- **Semantics:** preserved where intentional:
  - normalize-then-judge
  - suffix failure as a distinct failure
  - absence defaulting
  - scenario skipping
  - a null root as a finding
  - whitespace treated as missing
  - an unanchored regex in `Rules.Matches`
- **History:** the legacy sources are untouched in the repository root and compiled by link (`legacy/Legacy.csproj`)
  for characterization tests and benchmarks.

## Test Coverage

Test categories (`tests/Conform.Tests`, 104 tests):

| Area | What is covered |
|---|---|
| Legacy semantics | The README model re-expressed. Each legacy bug is shown fixed; each intended behavior is shown kept. |
| Boundaries | null / empty / whitespace; inclusive min/max for length, range and count; default values; clock-relative dates at the day boundary; malformed definitions failing at definition time. |
| Graphs | nested objects; indexed paths; nested collections (`Grid[1][1]`); empty and null collections; null elements; `HashSet`; shared references (evaluated once); cycles; self-containing lists; a 200,000-deep chain; polymorphic contract resolution; inherited members; undeclared members not traversed; throwing getters and enumerators. |
| Rules | aggregation and order; conflicting rules; conditional and scenario activation; cross-property object and member rules; warning and info severities; throwing rules and conditions as faults. |
| Mutation | no mutation during evaluation; determinism; `Apply` idempotency; conflict detection; the canonical value as a fixed point; lenses; normalization failure; read-only, init-only and struct owners. |
| Diagnostics | every finding field; ModelState-shaped dictionary; text output. |
| Extensibility | a Luhn rule, a custom normalizer, a custom contract source and custom attributes of all three kinds, all defined only in tests; introspection used to generate docs; core free of attributes. |
| Annotations | BCL attributes against canonical values with BCL messages; lens attributes; normalizer ordering enforcement; unparseable defaults; misapplied normalizers; class-level attributes; inherited attributes; `DateOnly`; attribute severity. |

**Not covered:**

- concurrency stress
- trimming/AOT publish verification (the core is marked `IsAotCompatible`, but this was not published)
- property-based / fuzz tests

## Performance Findings

See the table below, measured with BenchmarkDotNet (`--job short`) on the development machine. The graph is
1 order + N lines + 2N leaf lines, all valid, so the legacy code walks the whole graph.

| Method | Lines (objects) | Mean | Allocated | vs legacy |
|---|---|---:|---:|---:|
| Legacy2019 | 10 (31) | 360.5 µs | 37.9 KB | 1.00 |
| Conform_CodeContracts | 10 (31) | 24.5 µs | 12.1 KB | 0.07 |
| Conform_Annotations | 10 (31) | 25.1 µs | 16.6 KB | 0.07 |
| Conform_Annotations_ColdRegistry | 10 (31) | 126.9 µs | 35.6 KB | 0.35 |
| Legacy2019 | 200 (601) | 6,739.6 µs | 726.9 KB | 1.00 |
| Conform_CodeContracts | 200 (601) | 466.2 µs | 226.2 KB | 0.07 |
| Conform_Annotations | 200 (601) | 482.9 µs | 310.8 KB | 0.07 |
| Conform_Annotations_ColdRegistry | 200 (601) | 778.0 µs | 319.8 KB | 0.12 |

The measurements support these readings:

- **Where legacy cost comes from.** `GetProperties()` and `GetCustomAttributes()` run on every object, every call.
  That costs roughly 11 µs per object. Caching contracts per type is responsible for almost all of the ~14×
  speed-up. This is an implementation fix, not a conceptual one.
- **Code contracts vs. annotations are nearly equal in time.** Annotations allocate about 35% more: they build a
  `ValidationContext` per BCL attribute check and read members through reflection rather than bound delegates. Good
  enough; a source generator would close the gap.
- **Cold start** (building contracts through reflection) costs about 100 µs for two types. It is paid once per type
  per registry.
- **Steady state** is about 0.8 µs and about 380 B per object. The allocations are path nodes, the visited set, and
  boxed values. There is no evidence that further optimization matters for request-sized graphs, so none was done.
- **Caveat:** these come from a `--job short` run (3 iterations) on a heavily loaded development machine. The ratios
  are stable; the absolute numbers are indicative only.

## Remaining Weaknesses

1. **The constraint vocabulary is thin** compared with FluentValidation: no `Email`, no `NotEmpty` on collections,
   no localization. Adding them is mechanical, but it is also where this project competes head-on with mature
   tools. It should not try to win there.
2. **Normalization is same-type only (T → T).** Type-changing parsing (string → `PhoneNumber` value object) is
   out of scope. That is where many teams actually want to end up.
3. **No element rules for primitive collections.** For example, "each tag in `List<string>` is lowercase" needs a
   `Must` on the collection.
4. **Contracts do not merge.** An explicit `Define<T>` replaces the attribute contract for `T`, and a derived
   type's contract replaces its base's.
5. **The attribute adapter is reflection-based** (`RequiresUnreferencedCode`). A source generator emitting
   `TypeContract`s is the obvious AOT path, but it was deliberately not built.
6. **The first-path rule for shared instances** is correct, but it can surprise users who expect a finding at
   every path.
7. **`Apply()` is not transactional.** A failure midway leaves earlier writes in place. Each change is individually
   conflict-checked, but there is no rollback.
8. **Messages are English strings.** Codes are stable, so localization is possible, but it is not provided.

## What Could Be Built On Top

In order of how naturally the abstraction supports them:

- An ASP.NET Core endpoint filter that returns `ValidationProblemDetails` from `ToErrorDictionary()` and optionally
  applies changes before the handler runs.
- A source generator producing `TypeContract`s from attributes, for AOT.
- A library of well-known canonicalizers: phone (E.164), IBAN, national IDs, postal codes, e-mail casing, Unicode
  normalization. This, not the engine, is where most of the user value would be.
- Documentation and OpenAPI generation from contracts. The example already prints contract docs from the metadata.
- A "dry-run import" tool: evaluate a batch, show findings and diffs, then apply.

## What Would Make This Project Not Worth Continuing

- If real users only use constraints. Then `Microsoft.Extensions.Validation` or FluentValidation is strictly
  better: supported, source-generated, and familiar.
- If users always call `Evaluate(x).Apply()` unconditionally. Then the pure/propose split is ceremony, and a
  mutating normalizer in a model binder is simpler.
- If the demand is really for type-changing parsing into value objects. Then this is the wrong shape entirely.
- If nobody needs graph-wide canonicalization beyond `Trim`. Then ten lines per endpoint beat a dependency.

**Bottom line:** the old repository did contain a valuable idea, but a small one. The 2026 implementation is
technically coherent and honest about its size. It is a focused library, not a platform. Whether it deserves to
exist depends on one empirical question the repository cannot answer: **do enough boundaries need canonicalization,
not just validation?**
