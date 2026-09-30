namespace Conform.Tests;

/// <summary>Fixed clock: 2026-10-01 12:00 UTC, local zone UTC.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public static readonly FixedClock Default = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    public static DateTime Today => Default.GetUtcNow().Date;

    public override DateTimeOffset GetUtcNow() => now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

public static class Eval
{
    public static EvaluationOptions Options(params string[] scenarios) =>
        new() { Clock = FixedClock.Default, Scenarios = scenarios };

    public static ConformanceReport Run<T>(ContractRegistry registry, T root, params string[] scenarios) =>
        new ConformanceEngine(registry).Evaluate(root, Options(scenarios));

    public static Finding Single(ConformanceReport report, string code) =>
        Assert.Single(report.Findings, f => f.Code == code);
}

public class Order
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public DateTime Appointment { get; set; }
    public DateTime? EndDate { get; set; }
    public DateTime Start { get; set; }
    public DateTime Finish { get; set; }
    public List<Line>? Lines { get; set; }
    public Line? Main { get; set; }
    public HashSet<Line>? Set { get; set; }
    public List<List<Line>>? Grid { get; set; }
    public string? Note { get; set; }
    public int Quantity { get; set; }
}

public class Line
{
    public string? Name { get; set; }
    public string? Barcode { get; set; }
    public Line? Next { get; set; }
    public List<Line>? Children { get; set; }
}

public class SpecialLine : Line
{
    public string? Extra { get; set; }
}
