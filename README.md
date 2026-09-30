# AttributeBasedValidation → Conform

This repository started in 2019 as a 230-line reflection-based validation helper (preserved untouched in the root:
`ValidationHelper.cs`, `Attributes/`, `Models/`, `Program.cs`). An architectural archaeology in 2026 found that its
one durable idea was not "attribute-based validation" but **judging a value by its canonical form and optionally
committing that form**. `Conform` is that idea rebuilt from first principles:

```csharp
var registry = new ContractRegistry()
    .Define<Order>(c =>
    {
        c.Member(o => o.Phone).Required()
            .Normalize(Normalizers.DigitsOnly, Normalizers.KeepLast(10))  // canonical form
            .Matches(@"^5\d{9}$");                                          // judged on the canonical form
        c.Member(o => o.Lines).Required().Count(min: 1).Descend();
    })
    .UseAnnotations(); // DataAnnotations + [Trim]/[DigitsOnly]/[Descend]/... for everything else

var report = new ConformanceEngine(registry).Evaluate(order); // pure: findings + proposed changes
if (report.IsConformant) report.Apply();                        // explicit, conflict-checked writes
```

| Read | For |
|---|---|
| [docs/research/ARCHAEOLOGY.md](docs/research/ARCHAEOLOGY.md) | what the 2019 code really does, and what idea was hiding in it |
| [docs/research/ADR-*.md](docs/research/) | the three architectural decisions |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | how Conform works; legacy → new concept map |
| [docs/MIGRATION.md](docs/MIGRATION.md) | attribute mapping; legacy behavior intentionally not preserved |
| [docs/research/FINAL_REVIEW.md](docs/research/FINAL_REVIEW.md) | honest assessment, benchmarks, weaknesses |
| `samples/Conform.Example` | `dotnet run --project samples/Conform.Example` |

```
dotnet test Conform.slnx
dotnet run -c Release --project benchmarks/Conform.Benchmarks -- --filter '*'
```

---

## Legacy README (2019, unchanged)

# AttributeBasedValidation
Attribute based validation -- reflection


            var masterModel = new Master
            {
                CantBeNull = new List<Child>
                {
                    new Child
                    {
                        NestedChildren = new List<Child>(),
                        Barcode = "12345678900",
                        Name = "Murat Ay"
                    }
                },
                Child = new Child
                {
                    NestedChildren = new List<Child>(),
                    Barcode = "12345678900",
                    Name = "Murat Ay"
                },
                Name = "Murat Ay",
                AppointmentDate = DateTime.Today.AddDays(-1),
                CanbeNull = null,
                EndDate = null,
                GsmNumber = "+90 588 888 88 88",
                IgnoreAbleProperty = null
            };

            var result = ValidationHelper.CheckModel(masterModel, true);
            Console.WriteLine(result.IsValid+" "+result.Error);
            Console.ReadLine();
            
    public class Master
    {
        [SmartString]
        public string Name { get; set; }

        [SmartString(regexPattern: @"5\d{9}\s*?$", minLength: 10, maxLength: 15, onlyDigit: true, changeProperty: true, subStringIndex: 10)]
        public string GsmNumber { get; set; }

        [SmartDateTime(notNullOrDefault: false, greaterOrEqualThanTomorrow: true)]
        public DateTime AppointmentDate { get; set; }

        [SmartDateTime(false, false, changeIfDefault: true, year: 2020, day: 31, month: 12)]
        public DateTime? EndDate { get; set; }

        public IList<Child> CanbeNull { get; set; }

        [SmartChildClass(true, minCollectionCount: 1)]
        public IList<Child> CantBeNull { get; set; }

        [SmartChildClass(isCollection: false)]
        public Child Child { get; set; }

        [SmartString(ignoreWhenPrompted: true)]
        public string IgnoreAbleProperty { get; set; }

    }
    
    public class Child
    {
        [SmartString]
        public string Name { get; set; }

        [SmartString(minLength:10,maxLength:15)]
        public string Barcode { get; set; }

        [SmartChildClass(isCollection:true,minCollectionCount:0)]
        public IList<Child> NestedChildren { get; set; }

        [SmartChildClass(false)]
        public Child NestedChild { get; set; }
    }
