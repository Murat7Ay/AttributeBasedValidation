# Conform: Validation + Normalization for .NET Object Graphs

**Validate *and* canonicalize C# objects in one pass.** Conform checks nested objects and collections against
declarative contracts and reports every problem with an exact path such as `Lines[3].Barcode`. It also tells you
the **canonical form** of every value, and it **never mutates your objects unless you explicitly ask**.

> Think *"parse, don't validate"* (the Pydantic / Zod idea) for .NET object graphs. It works with the
> `DataAnnotations` attributes you already use.

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-latest-239120)](https://learn.microsoft.com/dotnet/csharp/)
[![Dependencies](https://img.shields.io/badge/core%20dependencies-0-brightgreen)](src/Conform/Conform.csproj)
[![AOT](https://img.shields.io/badge/core-AOT%20compatible-blue)](src/Conform/Conform.csproj)

---

## Contents

- [Why Conform?](#why-conform)
- [Features](#features)
- [Quick start](#quick-start)
- [Using DataAnnotations attributes](#using-dataannotations-attributes)
- [What a report looks like](#what-a-report-looks-like)
- [Core concepts](#core-concepts)
- [Custom rules and normalizers](#custom-rules-and-normalizers)
- [Object graphs: nesting, collections, cycles](#object-graphs-nesting-collections-cycles)
- [ASP.NET Core integration](#aspnet-core-integration)
- [Conform vs DataAnnotations vs FluentValidation](#conform-vs-dataannotations-vs-fluentvalidation)
- [Performance](#performance)
- [FAQ](#faq)
- [Project history: from a 2019 helper to Conform](#project-history-from-a-2019-helper-to-conform)
- [Building, testing, contributing](#building-testing-contributing)

---

## Why Conform?

Every API endpoint, message consumer and import job has the same boundary problem:

| The user sends | You want to store |
|---|---|
| `"+90 (532) 123 45 67"` | `"5321234567"` |
| `"   Ada Lovelace "` | `"Ada Lovelace"` |
| `"a-1"` | `"A1"` |
| *(no end date)* | `2026-12-31` |

Most .NET validation libraries only answer *"is this valid?"*, and they judge the **raw** input. So teams end up
writing the same code in every handler: trim, strip, re-check, assign, and hope the order is right.

Conform answers three questions **in one declaration and one pass**:

1. **What is wrong?** Structured findings with paths, codes, expected and observed values.
2. **What is the canonical form?** Proposed changes: before → after, plus the steps that produced them.
3. **Should we commit it?** That is your call: `report.Apply()`.

## Features

- ✅ **Validation + normalization in one contract.** Rules judge the *normalized* value.
- ✅ **Pure evaluation.** Your objects are never mutated while being validated.
- ✅ **Explicit, conflict-checked apply.** Changes are written only if the value hasn't changed since evaluation.
- ✅ **Recursive object graph validation.** Handles nested objects, collections, nested collections
  (`List<List<T>>`), `HashSet<T>` and any `IEnumerable`.
- ✅ **Cycle-safe and stack-safe.** Self-references, shared instances and 200,000-deep chains are all fine.
- ✅ **Precise diagnostics.** `Path`, `Code`, `Severity`, `Expected`, `Observed`, and a ModelState-shaped error
  dictionary.
- ✅ **Errors, warnings and info.** Only errors block conformance.
- ✅ **Cross-property and conditional rules.** `Must(...)`, `When(...)`, and scenarios such as `SkipIn("draft")`.
- ✅ **Deterministic time rules.** A `TimeProvider` is injected, so no hidden `DateTime.Now`.
- ✅ **Reuses DataAnnotations.** `[Required]`, `[StringLength]`, `[RegularExpression]`, `[Range]`, `[EmailAddress]`,
  `[Compare]`, `[CustomValidation]` and your own `ValidationAttribute`s all work.
- ✅ **Fluent, strongly typed C# API.** `Length` only appears on strings, `Count` on collections, `NotBeforeToday`
  on dates.
- ✅ **Introspectable contracts.** Generate docs or tooling from the same metadata.
- ✅ **Zero-dependency, AOT-compatible core.** Faults (a throwing getter or rule) are reported, never swallowed.

## Quick start

> Conform is not on NuGet yet. Clone the repo and reference `src/Conform` (and optionally
> `src/Conform.Annotations`).

```csharp
using Conform;

var registry = new ContractRegistry()
    .Define<Order>(c =>
    {
        c.Member(o => o.CustomerName).Required().Normalize(Normalizers.Trim).Length(2, 60);

        c.Member(o => o.Phone).Required()
            .Normalize(Normalizers.DigitsOnly, Normalizers.KeepLast(10)) // "+90 (532) 123 45 67" → "5321234567"
            .Matches(@"^5\d{9}$");                                         // judged on the canonical value

        c.Member(o => o.DeliveryDate).NotBeforeToday(1);                   // tomorrow or later
        c.Member(o => o.EndDate).Normalize(Normalizers.DefaultTo(new DateTime(2026, 12, 31)));
        c.Member(o => o.Lines).Required().Count(min: 1).Descend();         // validate every line

        c.Must("end-after-delivery", o => o.EndDate >= o.DeliveryDate, "EndDate must not be before DeliveryDate");
    })
    .Define<OrderLine>(c =>
    {
        c.Member(l => l.Sku).Required().Normalize(Normalizers.Trim, Normalizers.UpperInvariant);
        c.Member(l => l.Quantity).Range(1, 100);
    });

var engine = new ConformanceEngine(registry);

var report = engine.Evaluate(order);      // pure: nothing is written
if (report.IsConformant)
{
    report.Apply();                        // commit the canonical values
}
else
{
    foreach (var f in report.Errors)
        Console.WriteLine($"{f.Path}: {f.Message}");  // e.g. "Lines[2].Quantity: must be between 1 and 100"
}
```

## Using DataAnnotations attributes

Already have models decorated with `System.ComponentModel.DataAnnotations`? Add one line:

```csharp
var registry = new ContractRegistry().UseAnnotations();
```

Then add the attributes DataAnnotations lacks: normalization, traversal and scenarios.

```csharp
using System.ComponentModel.DataAnnotations;
using Conform.Annotations;

public class Customer
{
    [Required, Trim, StringLength(60, MinimumLength = 2)]
    public string? Name { get; set; }

    [Required, DigitsOnly(Order = 1), KeepLast(10, Order = 2), RegularExpression(@"5\d{9}")]
    public string? Phone { get; set; }

    [EmailAddress]
    public string? Email { get; set; }

    [DefaultIfMissing("2026-12-31")]
    public DateTime? ContractEnd { get; set; }

    [Required, MinLength(1), Descend]          // validate each address, too
    public List<Address>? Addresses { get; set; }

    [Required, SkipIn("draft")]                // not required while saving a draft
    public string? TaxNumber { get; set; }
}
```

| Attribute | Purpose |
|---|---|
| `[Required]` (BCL) | Presence. Null, empty or whitespace (after normalization) is missing. |
| any `ValidationAttribute` (BCL or custom) | A rule, evaluated against the **canonical** value. |
| `[Trim]`, `[DigitsOnly]`, `[KeepLast(n)]`, `[DefaultIfMissing("…")]` | Normalization steps. Add `Lens = true` to judge without proposing a change. |
| `[Descend]` | Validate the nested object, or every element of a collection. |
| `[SkipIn("scenario")]` | Skip the member in that scenario (for example `draft` or `import`). |
| `[NotBeforeToday(n)]`, `[NotDefault]` | Native date and value-type rules. |

Code contracts and attribute contracts can be mixed freely, type by type.

## What a report looks like

Output from [`samples/Conform.Example`](samples/Conform.Example/Program.cs):

```
Not conformant: 6 finding(s), 10 proposed change(s), 9 object(s)
  Error   AppointmentDate [min-date] must be tomorrow or later (earliest 2026-10-02)
  Warning Notes [length] must have at most 40 characters (has 56)
  Warning (root) [party-matches-guests] PartySize differs from the number of guests
  Error   Guests[1].Name [required] a value is required
  Error   Guests[1].Email [email-address] The Email field is not a valid e-mail address.
  Error   SeatingPlan[1][1].Code [pattern] must match /^[A-Z]\d{1,2}$/ | observed: "ZZTOP"
  Change  CustomerName: "   Ada Lovelace " -> "Ada Lovelace" via trim
  Change  Phone: "+90 (532) 123 45 67" -> "5321234567" via digits-only > keep-last-10
  Change  EndDate: null -> 2026-12-31 via default-to(2026-12-31)
  Change  Billing.PostalCode: "35 000" -> "35000" via digits-only
  Change  SeatingPlan[0][1].Code: " a-2 " -> "A2" via trim > upper > strip-dash
  ...
```

Every `Finding` carries:

| Field | Meaning |
|---|---|
| `Path` | ASP.NET-style location, e.g. `Guests[1].Email` |
| `ObjectType`, `Member` | what was evaluated |
| `Code` | stable, machine-readable (`required`, `length`, `pattern`, `range`, …) |
| `Severity` | `Error`, `Warning` or `Info` |
| `Kind` | `Violation`, `NormalizationFailed` or `Fault` |
| `Message`, `Expected`, `Observed` | why it failed, what was expected, and what was seen (the canonical value) |

## Core concepts

```
(object graph, contracts, context) ──Evaluate──► ConformanceReport { Findings, Changes }
                                                        │
                                                        └──Apply()──► conflict-checked writes
```

| Concept | What it is |
|---|---|
| **Contract** (`TypeContract`, `MemberContract`) | Plain metadata. Per member: presence, normalizers, lenses, rules, traversal edge and activation. |
| **Rule** | An immutable value: `Code` + `Description` + `Severity` + `Check(value, context)`. It never sees missing values. |
| **Normalizer** | Maps a value to its canonical form, or fails (for example, a phone number that is too short). |
| **Lens** | A normalizer used only for judging. It never proposes a change. |
| **ContractRegistry** | Resolves contracts from code definitions, attribute sources, or your own `IContractSource`. |
| **ConformanceEngine** | The interpreter. It is stateless and thread-safe once configured. |
| **ConformanceReport** | Immutable findings + proposed changes, plus `Apply()`. |

Full details: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Custom rules and normalizers

```csharp
public sealed class LuhnRule() : Rule<string>("luhn", "a number passing the Luhn checksum")
{
    protected override Violation? Check(string value, in RuleContext context) =>
        IsLuhnValid(value) ? null : new Violation("checksum does not match");
}

public sealed class CollapseSpaces() : Normalizer<string>("collapse-spaces")
{
    protected override NormalizeResult<string> Normalize(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
}

c.Member(o => o.CardNumber).Normalize(Normalizers.DigitsOnly).Check(new LuhnRule());
c.Member(o => o.Holder).Normalize(new CollapseSpaces());
```

To use them as attributes, subclass `RuleAttribute` or `NormalizeAttribute`. Any plain BCL `ValidationAttribute`
works too. You can also load contracts from anywhere (configuration, a database, generated code) by implementing
`IContractSource`. The engine never changes.

## Object graphs: nesting, collections, cycles

- **Explicit edges.** Only members marked `Descend()` / `[Descend]` are traversed, so there is no accidental walk
  into framework objects or lazy-loading proxies.
- **Collections.** Any `IEnumerable` except `string` is traversed, including `List<List<T>>`, with paths like
  `Grid[1][2].Name`. Null elements are reported unless you allow them.
- **Cycles and shared references.** Each instance is evaluated once, at its first path, so no finding or change
  is duplicated.
- **Deep graphs.** Traversal uses an explicit stack instead of recursion, so it never overflows the call stack.
- **Polymorphism.** The contract of the most-derived registered type is used.

## ASP.NET Core integration

`report.ToErrorDictionary()` returns `path → messages`, which is exactly the shape
`ValidationProblemDetails` expects:

```csharp
app.MapPost("/orders", (Order order, ConformanceEngine engine) =>
{
    var report = engine.Evaluate(order);
    if (!report.IsConformant)
        return Results.ValidationProblem(report.ToErrorDictionary());

    report.Apply();                 // store canonical values
    return Results.Ok(order);
});
```

## Conform vs DataAnnotations vs FluentValidation

| Capability | DataAnnotations `Validator` | FluentValidation | **Conform** |
|---|---|---|---|
| Declarative constraints | ✅ | ✅ | ✅ (reuses DataAnnotations) |
| Nested objects and collections | ❌ (✅ in .NET 10 Minimal APIs) | ✅ (`SetValidator`, `ForEach`) | ✅ (`Descend`) |
| Cycle-safe traversal | — | manual | ✅ built in |
| Normalization / canonical form | ❌ | ❌ | ✅ first-class |
| Rules judge the normalized value | ❌ | ❌ | ✅ |
| Proposed changes (before → after) | ❌ | ❌ | ✅ |
| Evaluation never mutates | ✅ | ✅ | ✅, with an explicit `Apply()` |
| Warnings and info findings | ❌ | ✅ | ✅ |
| Scenarios / rule sets | ❌ | ✅ | ✅ |
| Injected clock for date rules | ❌ | ❌ | ✅ `TimeProvider` |
| Maturity and ecosystem | ✅✅ | ✅✅ | 🧪 early |

**Use FluentValidation or DataAnnotations** if you only need constraints; they are mature and excellent.
**Use Conform** when your input needs to be cleaned up *and* validated, and you want both from one declaration,
with an auditable diff.

## Performance

Measured with BenchmarkDotNet against the original 2019 implementation, on a valid graph of 601 objects:

| Implementation | Mean | Allocated |
|---|---:|---:|
| Legacy 2019 (reflection on every call) | 6,740 µs | 727 KB |
| **Conform: code contracts** | **466 µs** | 226 KB |
| **Conform: DataAnnotations** | **483 µs** | 311 KB |

That is roughly **14× faster**, because contracts are built once per type and members are read through bound
delegates. The measurements and their caveats are in
[docs/research/FINAL_REVIEW.md](docs/research/FINAL_REVIEW.md#performance-findings). To reproduce:

```bash
dotnet run -c Release --project benchmarks/Conform.Benchmarks -- --filter '*'
```

## FAQ

**Does validation change my object?**
No. `Evaluate` is pure. Changes are only written by `report.Apply()`, and only if the member still holds the value
it had during evaluation.

**What is the difference between `Normalize` and `Lens`?**
`Normalize` means *"this is the real value; propose storing it"*. `Lens` means *"judge it this way, but keep what
the user typed"*. For example, validate a phone number as digits only while displaying the original.

**What happens when normalization is impossible?**
Take `KeepLast(10)` applied to `"12345"`. You get a `NormalizationFailed` finding, that member's rules are skipped,
and no change is proposed.

**Is it async?**
No, by design. Rules are pure checks over values. I/O checks such as "does this SKU exist?" belong in your
application layer.

**Is it AOT / trimming friendly?**
The core is (`IsAotCompatible`). The attribute adapter uses reflection and is marked `RequiresUnreferencedCode`. Use
code contracts in AOT apps.

**Can I use it for configuration, messages or events, not just API requests?**
Yes. Any object graph works: DTOs, commands, message payloads, configuration objects and imports.

## Project history: from a 2019 helper to Conform

This repository began in 2019 as `AttributeBasedValidation`, a small reflection-based helper with
`[SmartString]`, `[SmartDateTime]` and `[SmartChildClass]` attributes. In 2026 it went through an
**architectural archaeology**: what was the real idea hiding in that code?

The answer was *not* "attribute-based validation", which the platform now provides. It was a single flag
combination: validate a phone number **as digits only**, and *separately* decide whether to store the digits.
Conform is that idea rebuilt from first principles.

- 📜 [The archaeology](docs/research/ARCHAEOLOGY.md): what the old code really did, bugs included
- 🧭 [Architecture decisions](docs/research/): pure evaluation, attributes as an adapter, identity-based traversal
- 🔁 [Migration guide](docs/MIGRATION.md): mapping from the `Smart*` attributes
- ⚖️ [Final review](docs/research/FINAL_REVIEW.md): an honest assessment, including reasons *not* to use this

The original 2019 sources were removed from `master`; they live on at the [`legacy-2019` tag](https://github.com/Murat7Ay/AttributeBasedValidation/tree/legacy-2019).

<details>
<summary>Original 2019 README example</summary>

```csharp
var masterModel = new Master
{
    CantBeNull = new List<Child> { new Child { NestedChildren = new List<Child>(), Barcode = "12345678900", Name = "Murat Ay" } },
    Child = new Child { NestedChildren = new List<Child>(), Barcode = "12345678900", Name = "Murat Ay" },
    Name = "Murat Ay",
    AppointmentDate = DateTime.Today.AddDays(-1),
    CanbeNull = null,
    EndDate = null,
    GsmNumber = "+90 588 888 88 88",
    IgnoreAbleProperty = null
};

var result = ValidationHelper.CheckModel(masterModel, true);
Console.WriteLine(result.IsValid + " " + result.Error);

public class Master
{
    [SmartString] public string Name { get; set; }
    [SmartString(regexPattern: @"5\d{9}\s*?$", minLength: 10, maxLength: 15, onlyDigit: true, changeProperty: true, subStringIndex: 10)]
    public string GsmNumber { get; set; }
    [SmartDateTime(notNullOrDefault: false, greaterOrEqualThanTomorrow: true)] public DateTime AppointmentDate { get; set; }
    [SmartDateTime(false, false, changeIfDefault: true, year: 2020, day: 31, month: 12)] public DateTime? EndDate { get; set; }
    public IList<Child> CanbeNull { get; set; }
    [SmartChildClass(true, minCollectionCount: 1)] public IList<Child> CantBeNull { get; set; }
    [SmartChildClass(isCollection: false)] public Child Child { get; set; }
    [SmartString(ignoreWhenPrompted: true)] public string IgnoreAbleProperty { get; set; }
}
```

(Spoiler from the archaeology: this example prints `False NestedChild null or empty`.)

</details>

## Building, testing, contributing

Requires the **.NET 10 SDK**.

```bash
dotnet test Conform.slnx
dotnet run --project samples/Conform.Example
```

| Project | Contents |
|---|---|
| `src/Conform` | core engine (no dependencies) |
| `src/Conform.Annotations` | DataAnnotations and attribute adapter |
| `tests/Conform.Tests` | 104 behavior tests: graphs, mutation, diagnostics, extensibility |
| `samples/Conform.Example` | end-to-end demo |
| `benchmarks/Conform.Benchmarks` | BenchmarkDotNet comparison |

Issues and pull requests are welcome. Well-known canonicalizers (E.164 phone numbers, IBAN, postal codes) would be
especially valuable.

If Conform saves you from writing another trim-strip-validate-assign handler, **consider giving it a ⭐**.

---

<sub>Keywords: C# validation library, .NET validation, object graph validation, recursive validation, nested object
validation, collection validation, data normalization, input sanitization, canonicalization, DataAnnotations,
FluentValidation alternative, parse don't validate, Pydantic for .NET, ASP.NET Core validation,
ValidationProblemDetails, DTO validation, request validation.</sub>
