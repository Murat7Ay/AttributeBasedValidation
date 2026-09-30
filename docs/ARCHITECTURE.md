# Conform: Architecture

> **Conform** evaluates an object graph against *contracts*. It reports what is wrong and computes each value's
> canonical form. It **never writes anything until you explicitly ask it to.**

## 1. Why this project exists

At a trust boundary (API requests, partner payloads, imports, configuration), code typically performs a fixed
sequence of steps for every value:

1. accept messy input: `"+90 (532) 123 45 67"`, `"  Ada "`, `"a-1"`
2. reduce it to a canonical form: `"5321234567"`, `"Ada"`, `"A1"`
3. reject what cannot be canonicalized, or what violates a constraint *in canonical form*
4. store the canonical form

The platform's validators cover steps 3 partially. They do not judge the canonical value, and they do not produce
it. The 2019 code in this repository hand-rolled all four steps with mutation, and got most of them wrong (see
[research/ARCHAEOLOGY.md](research/ARCHAEOLOGY.md)).

## 2. The abstraction

```
(object graph, contracts, context)  ──evaluate──►  report = findings + proposed changes
report  ──apply──►  writes (optional, explicit, conflict-checked)
```

A **contract** is plain metadata. It exists per type and per member:

| Member facet | Meaning |
|---|---|
| presence | `Required`: null or whitespace (after normalization) is a violation |
| normalizers | canonicalization steps; their output is judged and proposed as a change |
| lenses | evaluation-only steps; rules see their output, but no change is proposed |
| rules | constraints over the observed value; they never see absent values |
| edge | `Descend`: follow into the object, or into each element |
| activation | `When(condition)` and `SkipIn(scenario)` |

Each type contract also has **object rules**, which cover cross-property constraints.

## 3. How the engine works

`ConformanceEngine.Evaluate(root, options)`:

1. Resolve the root's contract. A missing contract is a programming error and throws.
2. Traverse depth-first with an **explicit stack**. Each reference-type instance is visited **once**,
   identified by reference (`ReferenceEqualityComparer`). For each object, in declaration order:
   - skip the member if its scenario or condition says so
   - read the value; if the getter throws, record a `Fault` finding
   - run the normalizers; a failure produces a `NormalizationFailed` finding, and the member's rules are skipped
   - if the canonical value differs from the raw value, record a `ProposedChange`
   - run the lenses to produce the observed value
   - check presence; if the value is absent, report a `required` violation when required, then stop
   - run the rules; if a rule throws, record a `Fault` (it is never treated as a pass)
   - queue the edge using the canonical value
   - then run the object rules
3. Enumerables (except `string`) are expanded element by element. Nested enumerables get nested indices, and
   collections themselves are identity-tracked so self-containing lists terminate.

The engine is synchronous and stateless. It is thread-safe once its registry is configured.

## 4. How rules are represented

A `Rule` is an immutable value with four parts:

- `Code`: stable and machine-readable
- `Description`: what it expects
- `Severity`: Error, Warning or Info
- `CheckValue(value, context) → Violation?`

`Rule<T>` gives typed access. The context provides:

- `Owner`, for cross-property checks
- `Clock`, a `TimeProvider`, for deterministic time rules
- `Scenarios`
- `Path`

Built-ins (`Rules.*`):

- `Length`
- `Matches`, which compiles its regex once with a timeout
- `Range`
- `Count`, which works on any `IEnumerable`
- `NotDefault`
- `NotBeforeToday`
- `Must`

Built-in normalizers (`Normalizers.*`):

- `Trim`
- `DigitsOnly`
- `UpperInvariant` and `LowerInvariant`
- `KeepLast(n)`, which is partial and can fail
- `DefaultTo(value)`, which acts on absence
- `From` and `FromPartial`

## 5. How contracts are authored

**Code (primary).**

```csharp
var registry = new ContractRegistry()
    .Define<Order>(c =>
    {
        c.Member(o => o.Phone).Required()
            .Normalize(Normalizers.DigitsOnly, Normalizers.KeepLast(10))
            .Matches(@"^5\d{9}$");
        c.Member(o => o.Lines).Required().Count(min: 1).Descend();
        c.Must("end-after-start", o => o.End >= o.Start, "End must not be before Start");
    });
```

The typed shortcuts are constrained, so they appear only where they make sense:

- `Length` and `Matches` for strings
- `Count` for enumerables
- `NotBeforeToday` for dates

`Normalize` and `Check` take any `Normalizer` or `Rule`. Type compatibility is checked when `Define` runs.

**Attributes (adapter, `Conform.Annotations`).** `registry.UseAnnotations()` reads the following:

- BCL `[Required]` becomes presence.
- Every other BCL `ValidationAttribute` (built-in or custom) becomes a rule, judged against the canonical value.
- `[Trim]`, `[DigitsOnly]`, `[KeepLast(n)]` and `[DefaultIfMissing("...")]`, or any `NormalizeAttribute`, become
  normalizers. Set `Lens = true` to make one a lens. If a member has several, each must have a distinct `Order`.
- `[Descend]` becomes an edge, `[SkipIn("scenario")]` becomes activation, and any `RuleAttribute` becomes a native
  rule.

**Anything else.** Implement `IContractSource` and add it with `registry.AddSource(...)`. The tests include a
configuration-driven source.

Resolution order for a runtime type:

1. its explicit definition
2. the sources, in order
3. the explicit definitions of its base types

The result is cached per type.

## 6. How object graphs are traversed

- **Explicit edges only.** Undeclared members are leaves, even when they are objects.
- **Identity visitation.** A shared instance is evaluated once, at its first path. Cycles terminate.
- **Deterministic order.** Traversal is depth-first in declaration and index order. Two evaluations of the same
  graph produce identical reports.
- **Explicit stack.** A 200,000-deep linked list is covered by a test.
- **Polymorphism.** The contract of the most-derived registered type applies.

## 7. How results are represented

`ConformanceReport` contains:

- `Findings`: each has `Path`, `ObjectType`, `Member`, `Code`, `Severity`, `Kind`, `Message`, `Expected` and `Observed`
- `Changes`: each has `Path`, `Member`, `Before`, `After`, `Steps` and `CanApply`
- `ObjectsVisited`
- `IsConformant`: true when there are no error-severity violations, normalization failures or faults
- `Errors` and `Warnings`
- `ToErrorDictionary()`: path → messages, the shape of `ValidationProblemDetails`
- `Apply()`

Finding kinds answer different questions:

| Kind | Meaning |
|---|---|
| `Violation` | the data is wrong |
| `NormalizationFailed` | the data has no canonical form |
| `Fault` | the engine could not decide (a getter, rule or enumeration threw) |

## 8. How normalization is committed

```csharp
var report = engine.Evaluate(order);   // pure
if (report.IsConformant)
{
    var result = report.Apply();       // writes the proposed changes
}
```

`Apply()` writes a change only if the member still holds `Before`:

- If the member now holds `After`, the outcome is `AlreadyApplied`.
- If the member holds some other value, the outcome is `Conflict`.
- If the member has no public setter, or its owner is a struct, the outcome is `ReadOnly`.

Applying is idempotent, and a canonical value is a fixed point: re-evaluating after `Apply` proposes nothing.

## 9. How to create a custom rule

```csharp
public sealed class LuhnRule() : Rule<string>("luhn", "a number passing the Luhn checksum")
{
    protected override Violation? Check(string value, in RuleContext context) =>
        IsLuhnValid(value) ? null : new Violation("checksum does not match");
}

c.Member(o => o.CardNumber).Normalize(Normalizers.DigitsOnly).Check(new LuhnRule());
```

For attributes, wrap it: `class LuhnAttribute : RuleAttribute { CreateRule(...) => new LuhnRule(); }`. A plain
BCL `ValidationAttribute` also works unchanged.

Custom normalizers follow the same pattern: subclass `Normalizer<T>`, return `NormalizeResult<T>.Failure(...)`
when no canonical form exists, and override `NormalizeMissing()` to supply defaults.

## 10. Packages

| Project | Contents | Dependencies |
|---|---|---|
| `src/Conform` | model, rules, normalizers, builder, registry, engine, report | none (BCL only); `IsAotCompatible` |
| `src/Conform.Annotations` | attribute adapter and attributes | `Conform`, BCL DataAnnotations; reflection (`RequiresUnreferencedCode`) |
| `legacy/` | the untouched 2019 sources, compiled by link | none |
| `tests/Conform.Tests` | behavior, graph, mutation, diagnostics, extensibility and annotation tests | xUnit |
| `tests/Conform.Characterization.Tests` | pins legacy behavior | xUnit, `legacy` |
| `samples/Conform.Example` | end-to-end demo | both packages |
| `benchmarks/Conform.Benchmarks` | legacy vs. code contracts vs. annotations | BenchmarkDotNet |

## 11. Why this differs from the original repository

| Legacy concept | Discovered abstraction | New concept | Reason for change |
|---|---|---|---|
| `SmartString`, `SmartDateTime`, `SmartChildClass` | member contract = presence + canonicalization + constraints + edge | `MemberContract` built by code or by any source | the three attributes were one idea split by CLR type |
| Rule chosen by property CLR type | a rule declares what it accepts | `Rule.AppliesTo`, checked at definition | silent no-ops on misapplied attributes |
| `NotEmptyOrNullOrWhiteSpace` / `NotNull` gating all checks | presence is one facet | `IsRequired`; rules never see absent values | a non-required value was never checked at all |
| `OnlyDigit`, `SubStringIndex` | canonicalization, possibly partial | `Normalizer`, `NormalizeResult.Failure` | the steps are reusable, ordered and named; failure is a distinct kind |
| `ChangeProperty` | commit vs. judge-only | `Normalize` (proposes) vs. `Lens` (never proposes) | the legacy default (`false`) was already a lens |
| writes during validation | side effects as data | `ProposedChange`, `Apply()` | mutation happened before the checks that could still fail |
| `ChangeIfDefault` + Y/M/D | canonicalization of absence | `DefaultTo` / `[DefaultIfMissing]` | legacy overwrote values unconditionally |
| `GreaterOrEqualThanTomorrow` + `DateTime.Today` | time-relative constraint with injected clock | `NotBeforeToday(days)` + `TimeProvider` | deterministic, and not gated by presence |
| `IsCollection` + `ICollection` cast | the runtime value decides | any non-string `IEnumerable`, nested | `HashSet<T>` and lazy sequences were silently skipped |
| recursion without memory | graph, not tree | identity-visited explicit stack | a cycle killed the process |
| `ignoreWhenPrompted` flag | evaluation scenario | `EvaluationOptions.Scenarios` + `SkipIn` | the flag was lost below the root |
| `ValidationResult { IsValid, Error }` | structured findings + patch | `ConformanceReport` | first error only, no path, wrong messages |
| hard-coded `" invalid gsm number"` | domain belongs in the contract | messages come from rules and normalizers | the engine had domain knowledge |
