// A booking request arrives from a web form or a partner integration. It is messy: phone numbers with country
// codes and spaces, padded names, lower-case seat codes, a missing end date. We want to know, in one pass:
//   1. what is wrong (with exact locations),
//   2. what the canonical form of the data is,
// and only then decide whether to commit that canonical form.

using System.ComponentModel.DataAnnotations;
using Conform;
using Conform.Annotations;

var registry = new ContractRegistry()
    // Code contracts: full expressiveness (cross-property rules, conditions, typed rules).
    .Define<BookingRequest>(c =>
    {
        c.Member(b => b.CustomerName).Required().Normalize(Normalizers.Trim).Length(2, 60);
        c.Member(b => b.Phone).Required()
            .Normalize(Normalizers.DigitsOnly, Normalizers.KeepLast(10))   // "+90 532 ..." -> "532..."
            .Matches(@"^5\d{9}$");                                           // judged on the canonical value
        c.Member(b => b.AppointmentDate).NotBeforeToday(1);
        c.Member(b => b.EndDate).Normalize(Normalizers.DefaultTo(new DateTime(2026, 12, 31)));
        c.Member(b => b.PartySize).Range(1, 20);
        c.Member(b => b.Billing).Required().Descend().SkipIn("draft");
        c.Member(b => b.Guests).Required().Count(min: 1).Descend();
        c.Member(b => b.SeatingPlan).Descend();
        c.Member(b => b.Notes).Check(Rules.Length(max: 40).AsWarning());
        c.Must("end-after-appointment", b => b.EndDate is null || b.EndDate >= b.AppointmentDate,
            "EndDate must not be before AppointmentDate");
        c.Check(Rules.Must<BookingRequest>("party-matches-guests",
            b => b.Guests is null || b.Guests.Count == b.PartySize,
            "PartySize differs from the number of guests").AsWarning());
    })
    .Define<Seat>(c => c.Member(s => s.Code).Required()
        .Normalize(Normalizers.Trim, Normalizers.UpperInvariant, Normalizers.From<string>("strip-dash", s => s.Replace("-", "")))
        .Matches(@"^[A-Z]\d{1,2}$"))
    // Attribute contracts for the rest: plain DataAnnotations plus normalization and traversal.
    .UseAnnotations();

var engine = new ConformanceEngine(registry);

var request = new BookingRequest
{
    CustomerName = "   Ada Lovelace ",
    Phone = "+90 (532) 123 45 67",
    AppointmentDate = DateTime.Today,                 // must be tomorrow or later
    EndDate = null,                                   // will be defaulted
    PartySize = 3,
    Billing = new Address { City = " Izmir ", PostalCode = "35 000" },
    Guests =
    [
        new Guest { Name = "Ada", Email = "ada@example.com" },
        new Guest { Name = "  ", Email = "not-an-email", PlusOne = new Guest { Name = "Charles " } },
    ],
    SeatingPlan =
    [
        [new Seat { Code = "a-1" }, new Seat { Code = " a-2 " }],
        [new Seat { Code = "b-7" }, new Seat { Code = "zz-top" }],
    ],
    Notes = "Window table please, and a high chair for the little one",
};

Section("1. Evaluate (nothing is written)");
var report = engine.Evaluate(request);
Console.WriteLine(report);
Console.WriteLine($"Phone on the object is still: \"{request.Phone}\"");

Section("2. Findings grouped by path (ready for ValidationProblemDetails)");
foreach (var (path, messages) in report.ToErrorDictionary())
    Console.WriteLine($"  {path}: {string.Join("; ", messages)}");

Section("3. Fix the data, re-evaluate, then commit the canonical form");
request.AppointmentDate = DateTime.Today.AddDays(2);
request.Guests[1].Name = "Byron";
request.Guests[1].Email = null;
request.SeatingPlan[1][1].Code = "b-8";
request.PartySize = 2;

report = engine.Evaluate(request);
Console.WriteLine(report);
if (report.IsConformant)
{
    var applied = report.Apply();
    Console.WriteLine($"Applied {applied.Applied.Count} change(s); skipped {applied.Skipped.Count}.");
    Console.WriteLine($"Phone is now \"{request.Phone}\", seats: {string.Join(", ", request.SeatingPlan.SelectMany(r => r).Select(s => s.Code))}");
}

Section("4. Scenarios: a draft may omit billing");
request.Billing = null;
Console.WriteLine($"  default: conformant = {engine.Evaluate(request).IsConformant}");
Console.WriteLine($"  draft:   conformant = {engine.Evaluate(request, new EvaluationOptions { Scenarios = ["draft"] }).IsConformant}");

Section("5. The same metadata, consumed by something other than the engine: generated docs");
foreach (var type in new[] { typeof(BookingRequest), typeof(Guest), typeof(Address), typeof(Seat) })
{
    var contract = registry.Resolve(type)!;
    Console.WriteLine($"  {type.Name}");
    foreach (var m in contract.Members)
    {
        var parts = new List<string>();
        if (m.IsRequired) parts.Add("required");
        parts.AddRange(m.Normalizers.Select(n => $"normalize:{n.Name}"));
        parts.AddRange(m.Rules.Select(r => r.Severity == Severity.Error ? r.Description : $"{r.Description} ({r.Severity})"));
        if (m.Descend) parts.Add("descend");
        if (m.SkipInScenarios.Count > 0) parts.Add($"skip in {string.Join("/", m.SkipInScenarios)}");
        Console.WriteLine($"    {m.Name,-16} {string.Join(" | ", parts)}");
    }
    foreach (var r in contract.ObjectRules) Console.WriteLine($"    {"(object)",-16} {r.Code}: {r.Description}");
}

static void Section(string title) => Console.WriteLine($"\n=== {title} ===");

public class BookingRequest
{
    public string? CustomerName { get; set; }
    public string? Phone { get; set; }
    public DateTime AppointmentDate { get; set; }
    public DateTime? EndDate { get; set; }
    public int PartySize { get; set; }
    public Address? Billing { get; set; }
    public List<Guest> Guests { get; set; } = [];
    public List<List<Seat>> SeatingPlan { get; set; } = [];
    public string? Notes { get; set; }
}

public class Guest
{
    [Required, Trim] public string? Name { get; set; }
    [EmailAddress] public string? Email { get; set; }
    [Descend] public Guest? PlusOne { get; set; }
}

public class Address
{
    [Required, Trim] public string? City { get; set; }
    [Required, DigitsOnly, StringLength(5, MinimumLength = 5)] public string? PostalCode { get; set; }
}

public class Seat
{
    public string? Code { get; set; }
}
