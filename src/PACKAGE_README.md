# Conform.Net

**Validate *and* normalize .NET object graphs in one pass.** Conform checks nested objects and collections against
declarative contracts. Every problem is reported with an exact path (`Lines[3].Barcode`), and you get each value's
**canonical form** as a proposed change. Nothing is written to your objects until you call `Apply()`.

- Validation + normalization in one contract; rules judge the *normalized* value
- Pure evaluation, plus an explicit, conflict-checked `Apply()`
- Nested objects, collections, `List<List<T>>`, cycles and shared references
- Structured findings: path, code, severity, expected, observed
- Works with the `DataAnnotations` attributes you already use (`Conform.Net.Annotations`)
- Zero-dependency, AOT-compatible core

```csharp
using Conform;

var registry = new ContractRegistry()
    .Define<Order>(c =>
    {
        c.Member(o => o.Phone).Required()
            .Normalize(Normalizers.DigitsOnly, Normalizers.KeepLast(10))  // "+90 (532) 123 45 67" → "5321234567"
            .Matches(@"^5\d{9}$");
        c.Member(o => o.Lines).Required().Count(min: 1).Descend();
    });

var report = new ConformanceEngine(registry).Evaluate(order);   // pure
if (report.IsConformant) report.Apply();                           // commit canonical values
else return Results.ValidationProblem(report.ToErrorDictionary());
```

With attributes (`Conform.Net.Annotations`):

```csharp
var registry = new ContractRegistry().UseAnnotations();

public class Customer
{
    [Required, Trim, StringLength(60, MinimumLength = 2)] public string? Name { get; set; }
    [Required, DigitsOnly(Order = 1), KeepLast(10, Order = 2)] public string? Phone { get; set; }
    [Required, MinLength(1), Descend] public List<Address>? Addresses { get; set; }
}
```

📖 Documentation, comparison with FluentValidation/DataAnnotations, and benchmarks:
https://github.com/Murat7Ay/Conform
