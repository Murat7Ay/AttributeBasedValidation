namespace Conform;

/// <summary>How much a finding matters to the caller.</summary>
public enum Severity
{
    Info,
    Warning,
    Error,
}

/// <summary>What kind of thing a finding reports.</summary>
public enum FindingKind
{
    /// <summary>A value (or object) did not satisfy a rule or presence requirement.</summary>
    Violation,

    /// <summary>A value could not be brought into canonical form, so its rules were not evaluated.</summary>
    NormalizationFailed,

    /// <summary>The engine could not decide: a getter, rule, normalizer, condition or enumeration threw.</summary>
    Fault,
}

/// <summary>
/// One structured observation about the evaluated graph.
/// </summary>
/// <param name="Path">Location in ASP.NET ModelState style, e.g. <c>Lines[0].Barcode</c>. Empty for the root object.</param>
/// <param name="ObjectType">The type of the object that owns the member (or the object itself for object rules).</param>
/// <param name="Member">The member name, or <c>null</c> for object-level findings.</param>
/// <param name="Code">Stable machine-readable code, e.g. <c>required</c>, <c>length</c>, <c>pattern</c>.</param>
/// <param name="Severity">How much it matters.</param>
/// <param name="Kind">Violation, normalization failure or fault.</param>
/// <param name="Message">Human-readable explanation.</param>
/// <param name="Expected">What the rule expected, when it can say.</param>
/// <param name="Observed">The value that was judged (the canonical value when normalizers ran).</param>
public sealed record Finding(
    string Path,
    Type ObjectType,
    string? Member,
    string Code,
    Severity Severity,
    FindingKind Kind,
    string Message,
    string? Expected,
    object? Observed)
{
    /// <summary>True when the finding blocks conformance: an error-severity violation, a normalization failure, or a fault.</summary>
    public bool IsBlocking => Kind != FindingKind.Violation || Severity == Severity.Error;

    public override string ToString()
    {
        var where = Path.Length == 0 ? "(root)" : Path;
        var kind = Kind == FindingKind.Violation ? Severity.ToString() : Kind.ToString();
        var text = $"{kind,-7} {where} [{Code}] {Message}";
        if (Expected is not null) text += $" | expected: {Expected}";
        if (Observed is not null) text += $" | observed: {Format.Value(Observed)}";
        return text;
    }
}

/// <summary>The outcome of a rule that did not pass.</summary>
/// <param name="Message">Why it failed.</param>
/// <param name="Expected">What was expected; defaults to the rule's description when omitted.</param>
public sealed record Violation(string Message, string? Expected = null);

internal static class Format
{
    public static string Value(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? value.GetType().Name,
    };
}
