# Architectural Archaeology: AttributeBasedValidation (2019)

> Scope: the entire legacy repository is 8 files and roughly 400 lines. It consists of one
> 227-line static method (`ValidationHelper.CheckModel`), three attribute classes, two sample models,
> a two-field result class and a console `Main`. There is no project file, no test and no package
> reference. The code was written against .NET Framework-era C# (Turkish comments, Turkish GSM
> numbers, e-commerce vocabulary such as "Barcode" and "AppointmentDate").
>
> Every claim below comes from reading the code. The few runtime claims that surprised me were
> confirmed by compiling the untouched legacy files in a scratch project. Characterization tests pinned
> them during the rebuild; those tests and the legacy sources now live at the [`legacy-2019` tag](https://github.com/Murat7Ay/Conform/tree/legacy-2019)
> and in the history of `master`. File and line references below refer to that tag.

---

## 1. Executive Finding

**This is a small, buggy validation helper. Underneath it is one idea worth keeping. The idea is
not "attribute-based validation".**

That idea sits inside `SmartStringAttribute`:

```
raw value ──► normalize (digits only, keep last N) ──► check the *normalized* value ──► optionally write it back
```

The author was not validating *input*. They were validating the **canonical form** of the input,
and optionally committing that canonical form. Today this is known as *"parse, don't validate"*.
It is how Pydantic and Zod work, and mainstream .NET validation (DataAnnotations and
FluentValidation) still does not model it as a first-class concept.

Everything else in the repository was already commodity in 2019 and is standard-library in 2026:

- required, length, regex and date rules
- recursion into children
- a collection count check
- "ignore in some scenario" flags

.NET 10 ships source-generated recursive DataAnnotations validation (`Microsoft.Extensions.Validation`).

The hidden abstraction is therefore:

> **A contract interpreter over an object graph.** Each member declares a *presence* requirement, a
> *canonicalization* pipeline, *constraints over the canonical value* and *edges* to follow. The
> interpreter visits the graph once and returns **findings** plus a **proposed patch**. Applying
> the patch is a separate, explicit act.

This abstraction is coherent and small, and its core is modestly differentiated in .NET. It is
**not** an "object governance runtime". The evidence does not support that expansion (see §11–12).

---

## 2. Original System: what it actually does

### 2.1 Runtime flow of `CheckModel<T>(T obj, bool ignoreWhenPrompted = false)`

1. If `obj == null`, return `{IsValid=false, Error="Model null"}`.
2. `obj.GetType().GetProperties()` returns public instance properties, *including inherited ones*.
   Reflection runs on every call and nothing is cached.
3. **Pass 1, strings.** Only properties whose type is exactly `string`. For each `SmartString`
   attribute:
   - If `IgnoreWhenPrompted && ignoreWhenPrompted`, skip the attribute.
   - **Everything else runs only if `NotEmptyOrNullOrWhiteSpace` is true** (the default).
   - Null, empty or whitespace → error `"<Prop> null or empty"`, return immediately.
   - `OnlyDigit` → strip every non-digit character (a *normalization*).
   - `SubStringIndex > 0` → keep the **last** N characters. The name is wrong: this is a suffix
     length, not an index. If the value is too short, return `"<Prop> invalid gsm number"`. A
     domain-specific message is hard-coded into a generic engine.
   - `ChangeProperty` → **write the normalized value back to the object**, *before* the length and
     regex checks.
   - MinLength / MaxLength → `"<Prop> exceed length"`. The same message is used for both
     directions.
   - Regex (unanchored `Regex.Match`) → on failure, **also** `"<Prop> exceed length"`
     (copy-paste bug).
4. **Pass 2, dates.** Properties of type `DateTime` or `DateTime?` with `SmartDateTime`:
   - `NotNullOrDefault` → null or `default(DateTime)` is an error.
   - `GreaterOrEqualThanTomorrow` is checked **only inside the `NotNullOrDefault` branch**. On
     `Master.AppointmentDate` (`notNullOrDefault:false, greaterOrEqualThanTomorrow:true`) the date
     rule therefore *never runs*. The README example passes a date from yesterday.
   - The check uses `DateTime.Today`, a hidden dependency on the ambient clock.
   - The error message is missing a space: `"AppointmentDateinvalid date"`.
   - `ChangeIfDefault` → **unconditionally** overwrites the value with `new DateTime(Y,M,D)`. It
     does this whether or not the value is default. Probe: `EndDate = 2030-01-01` becomes
     `2020-12-31`.
   - There is no `IgnoreWhenPrompted` for dates.
5. **Pass 3, children.** Non-string properties with `SmartChildClass`:
   - `IgnoreWhenPrompted` is honoured, **but only at the root**. The recursive calls use the
     default `false`, so the flag silently stops working one level down.
   - **Everything is gated by `NotNull`.** With `notNull:false`, a *present* child is never
     traversed at all.
   - A null child → `"<Prop> null or empty"`.
   - `IsCollection` → cast to **non-generic `ICollection`**. A `HashSet<T>` or a lazy
     `IEnumerable<T>` is not an `ICollection`, so it silently passes with no count check and no
     recursion.
   - Count < `MinCollectionCount` → `"<Prop> at least N"`.
   - Each element → `CheckModel(element)`. A null element → `"Model null"`.
6. The first failure returns. There is exactly **one error string** and **no path**. The message
   says `NestedChild`, not `CantBeNull[0].NestedChild`.

### 2.2 Observable consequences

| Scenario | Legacy result |
|---|---|
| The README / `Program.cs` example | **Invalid**: `"NestedChild null or empty"`. The showcase fails its own validation. |
| Any finite `Child` tree | **Always invalid.** `[SmartChildClass(false)]` on `NestedChild` defaults `notNull:true`, so each child requires another child. The model is **unsatisfiable**. |
| `Child` with `c.NestedChild = c` | **Stack overflow.** The process dies and cannot be caught. |
| `GsmNumber = "+90 488 888 88 88"` | The object is mutated to `"4888888888"`, *then* rejected as `"exceed length"` (really a regex failure). |
| `CheckModel(m, false)` | Fails on `IgnoreAbleProperty`. This shows the flag is a *scenario switch*. |
| `EndDate = 2030-01-01` | Overwritten to 2020-12-31. |

### 2.3 Coupling and assumptions

- **Rule kind is coupled to CLR type.** Strings get string rules; `DateTime`/`DateTime?` get date
  rules. `SmartString` on an `int`, or `SmartDateTime` on a `DateTimeOffset`, is silently ignored.
- `AttributeTargets.Field` is declared but fields are never scanned. This is silently ignored
  metadata.
- **Closed rule set.** Adding a rule means editing a 227-line method. There is no extension point
  at all.
- Each attribute is a *bag of optional features* that uses `-1` sentinels and boolean flags. It
  encodes an ordered pipeline implicitly through the order of `if` blocks.
- `CheckModel<T>` is generic in name only; it immediately uses `obj.GetType()`.
- The result type is named `ValidationResult`, which collides with
  `System.ComponentModel.DataAnnotations.ValidationResult`.
- The code assumes object graphs are trees and are small.
- It assumes evaluation may mutate the input.
- It assumes the caller only needs the first error.

---

## 3. Reconstructed Design Intent

What was the author abstracting away? Reading `Master` as a request DTO for a Turkish e-commerce
or appointment service (GSM number, barcode, appointment date), the code they no longer wanted to
write by hand in every endpoint was:

```csharp
if (string.IsNullOrWhiteSpace(req.Name)) return Error("Name null or empty");
var gsm = new string(req.Gsm.Where(char.IsDigit).ToArray());
if (gsm.Length < 10) return Error("invalid gsm");
gsm = gsm.Substring(gsm.Length - 10);
req.Gsm = gsm;                      // store the canonical form
if (!Regex.IsMatch(gsm, "5\\d{9}")) return Error(...);
if (req.EndDate == null) req.EndDate = new DateTime(2020,12,31); // (intended)
foreach (var c in req.Items) { ...same again... }
```

So the problem mixes **three concerns at the trust boundary**:

1. **Accept messy input.** "+90 588 888 88 88", "0588…", "588 888 88 88" are all the same phone
   number.
2. **Reduce it to a canonical form** and store that form.
3. **Reject what cannot be canonicalized** or violates constraints in canonical form.

The attributes are an ingress contract. In its own vocabulary the system is a mix of
*normalization* and *validation* for *inbound data contracts*. It is not general object processing.
The evidence for normalization is concrete: `OnlyDigit`, `SubStringIndex`, `ChangeProperty` and
`ChangeIfDefault` together make up 4 of the 14 attribute parameters.

---

## 4. The Hidden Abstraction: competing conceptual models

I tried five models against the evidence.

**M1: Predicate list per property** (`property → [bool rules]`)
- Explains: Required, Length, Regex, Count.
- Fails to explain: `OnlyDigit` and `SubStringIndex`, which change *what* is checked;
  `ChangeProperty`; `ChangeIfDefault`; and the scenario flag.
- This is DataAnnotations. It is **too small** for the evidence.

**M2: Rule engine** (facts + condition/action rules + agenda)
- Explains: mutation looks like an "action".
- Fails to explain: there is no rule chaining, no fact base and no inference. Actions are
  value-local.
- **Too big.** Nothing in the repository wants forward chaining.

**M3: Transformation pipeline** (`input → f1 → f2 → output`)
- Explains: the `OnlyDigit → Suffix → write` order.
- Fails to explain: the constraints and collection cardinality; the fact that validation can
  happen *without* writing (`ChangeProperty=false`); and graph traversal.

**M4: Schema / parser** (`raw → Result<Canonical, Errors>`), i.e. "parse, don't validate"
- Explains: normalize-then-check, write-back as "take the parsed output", and required as a
  schema property.
- Partially fails: the legacy code parses *in place* (T → T; it never changes the type). It walks
  a pre-existing object graph instead of building one. The traversal edges are explicit, not
  inferred.

**M5: Contract interpreter over an object graph** (the selected model)

```
             ┌──────────── Contract (per type, declarative metadata) ─────────────┐
             │ member:  presence · canonicalize[] · lens[] · constraints[] · edge │
             │ object:  cross-member constraints                                  │
             │ activation: condition / scenario                                   │
             └────────────────────────────────────────────────────────────────────┘
Object graph ──► Interpreter (single visit per instance, explicit edges only)
                    │
                    ├─► Findings (path, code, severity, expected, observed)
                    └─► Proposed patch (path, before, after, steps)   ──► Apply (explicit, separate)
```

M5 explains every one of the 14 attribute parameters:

| Legacy parameter | M5 concept |
|---|---|
| `NotEmptyOrNullOrWhiteSpace`, `NotNull`, `NotNullOrDefault` | presence |
| `OnlyDigit`, `SubStringIndex` | canonicalization steps (one of them partial, so it can fail) |
| `ChangeProperty` | whether canonicalization is *proposed as a change* or only used as a *lens* |
| `ChangeIfDefault` + Y/M/D | canonicalization of *absence* (default a missing value) |
| `MinLength`, `MaxLength`, `RegexPattern`, `GreaterOrEqualThanTomorrow`, `MinCollectionCount` | constraints over the canonical value |
| `SmartChildClass`, `IsCollection` | edges (descend into a value or its elements) |
| `IgnoreWhenPrompted` + the `ignoreWhenPrompted` argument | activation by evaluation scenario |

M5 is M4 (parser) applied in place to an existing graph, with the side effects reified as data (the
patch) instead of performed. That reification is the one real design move the original author did
not make. Making it removes every mutation bug in §2.

---

## 5. Challenging the word "Validation"

| Candidate framing | Evidence for | Evidence against | Verdict |
|---|---|---|---|
| Validation library | Most parameters are constraints | 4/14 parameters mutate or reshape values | Too narrow |
| Normalization / canonicalization engine | OnlyDigit, Suffix, ChangeProperty, ChangeIfDefault | Constraints dominate | Half the story |
| **Contract conformance** (validation + canonicalization at a boundary) | Explains everything in §4 | — | **Chosen** |
| Object policy engine | Scenario flag ≈ policy | No subjects, actions, obligations or decisions (unlike OPA/Cedar) | Unsupported |
| Metadata-driven execution engine | Attributes drive behaviour | Behaviour is limited to value-local checks and changes | Over-general |
| Data quality engine | Findings over records | No datasets, statistics or profiling | Unsupported |
| Object governance runtime | — | No evidence at all | Marketing |

"Validation" is too narrow. The honest replacement is **conformance to a data contract,
including canonical form**. "Conform" is also what the engine tells you to do: *here is how this
object differs from its contract, and here is the patch that would make its values canonical.*

---

## 6. The 2010s (and 2019) Accidents

| Accident | Kind | Survives? |
|---|---|---|
| Runtime reflection on every call, no caching | implementation | No. Build a contract once per type and cache it. |
| One god-method with a hard-coded rule set | implementation, and it blocks the concept | No. Rules must be values. |
| Rule kind chosen by CLR type (string/DateTime) | conceptual mistake | No. Rules declare what they accept, and mismatches are reported. |
| Attributes as the *only* definition mechanism | conceptual limitation (constants only, no lambdas, no ordering guarantee, no cross-property logic) | Attributes become an adapter. |
| Boolean/sentinel bags (`-1`, `IsCollection`) | implementation | No. |
| Presence gates all other checks | bug that looks like design | No. Presence is one rule; the other rules skip absent values. |
| Mutation during evaluation, before checks | conceptual mistake | No. Evaluation proposes a patch; applying it is explicit. |
| First-error short-circuit, single string, no path | weak result model | No. Aggregate structured findings with paths. |
| Ambient `DateTime.Today` | implementation | No. Use `TimeProvider` in the evaluation context. |
| Tree assumption (no cycle handling) | conceptual mistake | No. Use identity-based visitation. |
| Non-generic `ICollection` for collections | old-.NET accident | No. Any `IEnumerable` except `string`. |
| `IsCollection` flag | accident | No. The runtime value decides. |
| Scenario flag lost in recursion | bug | No. Scenario is part of the evaluation context. |
| Domain messages in the engine ("invalid gsm number") | coupling | No. |
| Synchronous only | **not** an accident | Stays synchronous (see §9). |

**Conceptual improvement vs implementation modernization.** Of the above, only these are
*conceptual* changes:

- the patch/apply split
- rules as values
- attributes as an adapter
- structured findings
- identity-aware traversal
- scenario-in-context

Caching, `TimeProvider`, `IEnumerable` support and delegate creation are modernization. Records,
source generators and DI are not needed to express the concept at all.

---

## 7. Generalization Analysis

How far does M5 extend *naturally* (without new core concepts)?

| Target | Natural? | Why |
|---|---|---|
| DTOs / API request models | Yes | This is the original use. |
| Command and message payloads | Yes | Same shape as a request. |
| Configuration objects | Yes | Includes defaulting of absent values (the `ChangeIfDefault` intent). |
| Domain objects | Partially | Invariants belong in constructors. Using this there is a smell. |
| Database entities | Partially | Graph traversal must not trigger lazy loading. Edges must be explicit (they are). |
| Workflow state | Weak | Rules would need history, which a single-snapshot interpreter doesn't have. |
| Arbitrary object graphs | **No, deliberately** | The interpreter follows declared edges only. Implicit whole-graph walking breaks on framework objects. |

| Rule shape | Natural? |
|---|---|
| Constraints, warnings, informational findings | Yes: severity is a property of a finding. |
| Normalization | Yes: it is a first-class member step. |
| Defaulting of absence | Yes: a normalizer that accepts absence. |
| Conditional rules | Yes: activation predicate over (owner, context). |
| Cross-property rules | Yes: object-level rules, or rules that read the owner. |
| Cross-object (parent/sibling-object) rules | Only from the ancestor, as an object rule on the parent. There is no upward references by design. |
| Type-changing transformation (string → PhoneNumber) | **No.** This is where M5 becomes a mapper/parser. It is a different product. |
| Derived values and recommendations | Weak. They could be modelled as normalizations or info findings, but that is stretching. |

Rules *compose* because they are values: a member has an ordered list, and rules can be wrapped
(e.g. with a different severity). Rules do **not** need a combinator algebra (and/or/not); the
evidence shows nothing that wants one.

---

## 8. Attribute Strategy (models A–H)

| | Expressive | Cross-prop | Conditional | Compile-safe | Ordering | Dynamic config | AOT | DX / discoverability |
|---|---|---|---|---|---|---|---|---|
| A. Attributes only | low (constants) | no | no | partial | **undefined order** | no | good if source-generated | excellent (the model is self-documenting) |
| B. Fluent API | high | yes | yes | yes | explicit | no | good (no reflection needed) | good |
| C. External definitions (JSON/YAML) | low–medium | via expressions | via expressions | no | explicit | **yes** | fine | poor |
| D. Code-based rules (classes) | highest | yes | yes | yes | explicit | no | good | medium |
| E. Expression-based rules | high | yes | yes | yes | explicit | no | **risky** (the interpreter is slow in AOT) | good; enables introspection |
| F. Source-generated metadata | = A | no | no | yes | defined at generation | no | best | same as A |
| G. Hybrid metadata + executable rules | high | yes | yes | yes | explicit | partial | good | good |
| H. **Attributes as one adapter into a rule model** | = G | yes | yes | yes | explicit in core | extensible | good | best of both |

Decisive evidence from the repository:

1. The legacy attributes encode an **ordered pipeline** (digits → suffix → write → length →
   regex). Attribute order is not a guaranteed property of `GetCustomAttributes`. **Attributes
   cannot own ordering semantics.**
2. The legacy attributes could not express "tomorrow" without a hard-coded flag. They could not
   express "EndDate after StartDate" at all. Attributes cannot be the whole rule language.
3. The one thing attributes did well was making the contract **visible on the model**. That is
   worth keeping as an adapter.

**Decision: H.** The core is a runtime *contract model* made of rule and normalizer **values**. A
typed code builder (B/D) is the primary authoring surface. Attributes are an adapter, and the
adapter **reuses the BCL DataAnnotations vocabulary** (`[Required]`, `[StringLength]`,
`[RegularExpression]`, `[Range]`, `[MinLength]`, and any custom `ValidationAttribute`) instead of
inventing `Smart*`. It adds only what DataAnnotations lacks:

- normalization steps with explicit order
- traversal edges
- scenario activation

See ADR-002.

---

## 9. Reflection Strategy

Reflection in the legacy code does three separate jobs:

1. **Discover metadata.** This is needed only by the attribute adapter, and only once per type.
2. **Read and write members.** Replace this with delegates created once
   (`MethodInfo.CreateDelegate` for property getters and setters).
3. **Decide rule applicability by CLR type.** This is a design mistake and should be removed.

**Runtime reflection is not architecturally central.** It is one metadata source. A source
generator could produce the same contract model at compile time; that is the AOT story, and the
contract model is deliberately a plain data structure that could be emitted as code. Building a
source generator now would be premature. Nothing yet proves the model is stable. Expression-tree
compilation is also unnecessary: `CreateDelegate` gives direct-call speed without `System.Linq.
Expressions`, which is interpreted, not compiled, under NativeAOT.

**Sync vs async.** Async rules (such as "barcode exists in DB") look like an obvious gap. They
**should not** be in the core. Once a rule does I/O, it is no longer a contract over a value. It is
an application service with failure modes (timeouts, retries), and it belongs in the application
layer. Keeping the interpreter synchronous and pure is a feature.

---

## 10. Mutation Strategy

The five verbs are distinct:

| Verb | Meaning | Output | Legacy equivalent |
|---|---|---|---|
| **Validate** | Does the value satisfy constraints? | findings | length, regex, required |
| **Normalize (canonicalize)** | Same meaning, canonical representation | new value of the **same type** | OnlyDigit, Suffix |
| **Default** | Supply a value for *absence* | a value | ChangeIfDefault (intended) |
| **Transform** | Change the meaning or type (string → PhoneNumber, derive totals) | new value, possibly of a new type | none |
| **Repair / Enforce** | Force the data to satisfy constraints (truncate, clamp) | new value, with loss | none. Also dangerous: it hides bad input. |

Semantic boundary chosen:

```
raw ──normalize/default*──► canonical ──lens*──► observed ──constraints──► findings
                               │
                               └── if canonical ≠ raw  ─►  ProposedChange(path, before, after)

report.Apply()  ──►  ApplyResult (applied | skipped: read-only, conflict)
```

- **Normalize/Default** are in scope. Their result is *proposed*, never written during evaluation.
- **Lens** (the legacy `ChangeProperty=false` with `OnlyDigit`) is in scope: "judge this as digits
  only, but don't store digits only". This is FluentValidation's `Transform` semantics.
- **Transform** (type-changing) is out of scope. It is a mapping library.
- **Repair/Enforce** is out of scope. Silent lossy fixes are the opposite of conformance.
- A normalizer may **fail**. The legacy "too short for suffix" is a *normalization failure*: a
  distinct finding kind, after which the member's constraints are skipped because no canonical
  value exists.
- Apply uses **optimistic concurrency**: a change applies only if the member still holds `before`.
  This makes the patch safe to hold, inspect, serialize for display, or discard.

See ADR-001.

---

## 11. The 2026 Architecture (from scratch)

### Core abstractions

The complete vocabulary has eleven types:

| Concept | Responsibility |
|---|---|
| `Rule` | A value that checks one observed value (member or whole object). Has a `Code`, a `Severity` and a `Description` (what it expects). |
| `Normalizer` | A value that maps a value to its canonical form or fails. It may accept absence (defaulting). |
| `MemberContract` | Accessor + presence + normalizers + lenses + rules + edge + activation, for one member. |
| `TypeContract` | The member contracts plus the object-level rules for one CLR type. This is *the metadata model*. |
| `ContractRegistry` | Resolves the contract for a runtime type from explicit definitions, then from pluggable `IContractSource`s (e.g. attributes). Caches per type. |
| `ContractBuilder<T>` | Typed authoring facade that produces a `TypeContract`. |
| `ConformanceEngine` | The interpreter: iterative traversal with identity tracking, producing a report. |
| `EvaluationOptions` / `RuleContext` | Clock, scenarios, owner and path given to rules. |
| `Finding` | Path, object type, member, code, severity, kind, message, expected, observed. |
| `ProposedChange` | Path, member, before, after, normalizer steps, and whether it can be applied. |
| `ConformanceReport` | Findings + changes + statistics, and `Apply()`. |

### Execution model

1. Resolve the contract for the root's runtime type.
2. Visit the object once, keyed by reference identity:
   - For each member in declaration order: check activation, read the value (a failure becomes a
     *fault* finding), canonicalize (a failure becomes a *normalization* finding), propose a
     change if the value changed, apply lenses, check presence, then run the rules on the
     observed value.
   - Queue edges: descend into the canonical child value, or into each element (nested
     enumerables are flattened with nested indices).
   - Then run the object-level rules.
3. Continue depth-first until no objects remain. Traversal uses an explicit stack, so depth is
   bounded by memory, not by the call stack.

### Traversal model

- **Explicit edges only.** Members without an edge are leaves.
- **Identity visitation.** Each instance is evaluated exactly once, at its first path. This makes
  cycles terminate. It also means repeated references produce no duplicate findings and no
  conflicting duplicate patches.
- **Polymorphism.** The contract is resolved from the most-derived registered type.

### Diagnostics

Every finding carries a stable **code**, a path in the ASP.NET ModelState style
(`Lines[0].Barcode`), expected and observed values, a severity (Error, Warning, Info) and a kind
(Violation, NormalizationFailed, Fault). **Faults are not violations.** They mean "the engine could
not decide", e.g. a getter threw or a rule crashed.

### Package boundaries

- `Conform`: the core. No dependencies, no attribute scanning.
- `Conform.Annotations`: the attribute adapter. BCL only (`System.ComponentModel.Annotations` is
  part of the shared framework).

Two packages, because the architecture has exactly one seam that matters: metadata *sources*.

### Integration model

`ConformanceReport.Findings` maps directly onto `ValidationProblemDetails` / ModelState (path →
messages). The API does not ship an ASP.NET integration package. That would be speculative.

---

## 12. Rule Model

**A rule is a named, described, severity-bearing predicate over one observed value in context,
which yields at most one violation.**

- *Named* (`Code`): stable, machine-readable, and usable for localization lookups.
- *Described* (`Description`): what it expects. This makes contracts introspectable and
  documentable.
- *Over one observed value*: this is what makes rules reusable. Member rules and object rules have
  the same shape (an object rule's value is the object).
- *In context*: owner (for cross-property logic), clock, scenarios.
- **Absence is not given to rules.** Presence is decided once, by the member. This fixes the
  legacy gating bug structurally.
- A rule that throws is caught and becomes a *fault* finding. It is not propagated and it is not
  treated as a pass.

## 13. Result Model

```
ConformanceReport
  IsConformant          // no Error-severity violations and no faults
  Findings[]            // ordered: DFS by declaration order (deterministic)
  Changes[]             // proposed, never yet applied
  ObjectsVisited
  Apply() → ApplyResult { Applied[], Skipped[(change, reason)] }
```

It is immutable apart from the explicit `Apply()`. Evaluating twice gives identical reports.

## 14. Extensibility

- Custom rule: subclass `Rule<T>` (or use `Must(...)` for one-offs).
- Custom normalizer: subclass `Normalizer<T>` or use `Normalizer.Create`.
- Custom attribute: subclass the BCL `ValidationAttribute` (adapted automatically), or
  `RuleAttribute` / `NormalizeAttribute` for native structured findings.
- New metadata source: implement `IContractSource` (JSON, source generator, database).

None of these require touching the engine. The test suite includes a Luhn-check rule defined only
in the test project to prove it.

## 15. Performance

In the legacy design, cost comes from:

- `GetProperties()` and `GetCustomAttributes()` on every object, every call
- `PropertyInfo.GetValue` / `SetValue`
- `Regex.Match` through the static cache
- string allocation for messages

The new design moves discovery to once per type. Access uses bound delegates. Paths are built
lazily (a linked parent chain, rendered to a string only when a finding or change needs it).
Regexes are constructed once per rule. The measured numbers are in `docs/research/FINAL_REVIEW.md`
and `benchmarks/`.

## 16. Ecosystem Comparison

| Technology | What it already solves here |
|---|---|
| **DataAnnotations** (`Validator.TryValidateObject`) | Required, length, regex, range, custom attributes. **No** recursion, no normalization, no paths. |
| **Microsoft.Extensions.Validation** (.NET 10, source-generated) | Recursive DataAnnotations validation of nested objects and collections for Minimal APIs, with paths. **This makes the legacy repository's whole purpose obsolete.** It does not normalize. |
| **FluentValidation** | Code-first rules, cross-property, conditions, rule sets (≈ scenarios), `SetValidator`/`ForEach` for graphs, structured failures. It had a `Transform` lens but deprecated it. It does not propose or apply changes. It does not handle cycles (you would recurse). |
| **Options validation** (`[OptionsValidator]` source gen) | Configuration validation. No normalization. |
| **JSON Schema / OpenAPI** | Wire-level contracts, recursion via `$ref`, `format`, `default` (annotations only). Validation of JSON, not of CLR graphs. |
| **Pydantic (Python) / Zod (TS)** | *Exactly* the "parse, don't validate" core: coercion/normalization + validation producing canonical output. This is the proof that the idea is valuable, and the proof that it is not novel. |
| **AutoMapper / Mapperly** | Type-changing transformation. This is out of scope and deliberately left to them. |
| **OPA / Cedar / rule engines** | Policy and decision logic. They do not overlap with the evidence here. |
| **Roslyn analyzers** | Could validate contract definitions at compile time. This is future tooling, not core. |

## 17. Kill Criteria

Abandon the project if any of the following is true:

1. **Normalization turns out not to matter to users.** If the only thing used is constraints,
   then `Microsoft.Extensions.Validation` + DataAnnotations (or FluentValidation) already does
   everything better. It is supported, source-generated and AOT-ready.
2. **Users mostly want type-changing parsing** (string → value object). Then the right tool is
   value objects with `TryParse` at the boundary. That is a pattern, not a library, and the
   in-place T → T patch becomes irrelevant.
3. **The patch/apply split is seen as ceremony.** If every caller writes
   `Evaluate(x).Apply()` unconditionally, then the separation earns nothing over a mutating
   `Normalize()`.
4. **FluentValidation re-adds a normalization step** with a proposal model. Then there is no gap
   left.
5. **Commercially:** a 1–2k line library in a crowded category, with no distribution channel, is
   not a business. At best it is a well-made OSS utility.

## 18. Opportunity

If it survives: **"Pydantic-style boundary conformance for .NET object graphs"**. One declaration
answers "is this input acceptable?" *and* "what is its canonical form?". The result is an
inspectable patch instead of silent mutation, with cycle-safe graph traversal and first-class
structured diagnostics. It reuses DataAnnotations rather than competing with it. The honest size
of the opportunity is a **focused OSS library**, not a platform.

## 19. Open Questions

- Would real users accept `Apply()` as a separate step, or want an `EvaluateAndApply` shortcut?
- Should element-level rules for primitive collections (`List<string>` of tags) be first-class?
  The legacy code has no evidence either way.
- Should a derived type's contract *merge* with a registered base contract? Today the
  most-derived one wins.
- Is the author's GSM normalization a single case, or representative of their real workload
  (phones, IBANs, tax numbers, barcodes)? If it is representative, a library of well-known
  normalizers is the real product. If not, the product is thin.
- Should a source generator produce `TypeContract`s for AOT? Only if adoption appears.

---

## 20. Final Self-Critique

1. **Is this merely an old validation library?**
   Mostly, yes. It is also a *bad* one: its own example fails, one of its models is unsatisfiable,
   it crashes on cycles, and its core mutation feature fires before checks and overwrites
   unconditionally.

2. **Is there a deeper abstraction?**
   Yes, but it is small: *canonicalize-then-constrain over an object graph, with the side effects
   reified as a patch*.

3. **What is it?**
   A contract interpreter: `(graph, contracts, context) → (findings, proposed patch)`, plus a
   separate `apply`.

4. **Was the implementation accidentally pointing at something broader?**
   Toward "parse, don't validate". That is broader than validation, but it is not broader than
   what Pydantic/Zod already are. It does not point toward "governance" or policy engines.

5. **What is genuinely interesting in 2026?**
   - normalization as first-class, *proposed* (not applied) changes
   - lens-vs-commit distinction
   - normalization *failure* as a distinct finding kind
   - scenario activation carried through the whole graph

6. **What should be discarded?**
   - the `Smart*` attributes
   - the CLR-type-driven rule dispatch
   - the sentinel/boolean bags
   - mutation during evaluation
   - single-string results
   - the tree assumption
   - `IsCollection`
   - hard-coded domain messages

7. **What makes the original obsolete?**
   .NET 10's source-generated recursive DataAnnotations validation, and FluentValidation.

8. **What makes it more powerful now?**
   `TimeProvider` (deterministic rules), `CreateDelegate` accessors, reference-identity hashing,
   and a stable BCL attribute vocabulary to *reuse*. A source generator is possible later.

9. **Core API, conceptually:**
   ```csharp
   registry.Define<Order>(c => c.Member(o => o.Phone).Required().Normalize(Normalizers.DigitsOnly).Matches("^5\\d{9}$"));
   var report = engine.Evaluate(order);   // pure
   report.Findings / report.Changes;
   report.Apply();                          // explicit
   ```

10. **Strongest argument for NOT rebuilding:**
    Constraint validation, which is 70% of the legacy surface, is solved in the platform.
    Normalization can be written as ten lines in the endpoint.

11. **Strongest argument for rebuilding:**
    No .NET tool lets one declaration drive both canonicalization and validation with an
    inspectable result. Every team re-writes the phone/barcode/whitespace normalize-then-validate
    dance by hand, as this author did.

12. **Single discovery that justifies continuing:**
    `ChangeProperty=false` together with `OnlyDigit`. It shows the author needed to *judge a value
    through its canonical form* independently of *committing* that form. That separation
    (lens vs proposal vs apply) is the one idea here that is neither obsolete nor already standard
    in .NET.
