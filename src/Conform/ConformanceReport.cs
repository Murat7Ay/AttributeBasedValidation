using System.Text;

namespace Conform;

/// <summary>The immutable outcome of one evaluation.</summary>
public sealed class ConformanceReport
{
    internal ConformanceReport(IReadOnlyList<Finding> findings, IReadOnlyList<ProposedChange> changes, int objectsVisited)
    {
        Findings = findings;
        Changes = changes;
        ObjectsVisited = objectsVisited;
    }

    /// <summary>All findings, in deterministic traversal order.</summary>
    public IReadOnlyList<Finding> Findings { get; }

    /// <summary>Changes normalization would make. Nothing has been written.</summary>
    public IReadOnlyList<ProposedChange> Changes { get; }

    /// <summary>Number of objects evaluated against a contract.</summary>
    public int ObjectsVisited { get; }

    /// <summary>No error-severity violations, normalization failures or faults. Warnings and infos do not count.</summary>
    public bool IsConformant => !Findings.Any(f => f.IsBlocking);

    public IEnumerable<Finding> Errors => Findings.Where(f => f.IsBlocking);

    public IEnumerable<Finding> Warnings => Findings.Where(f => !f.IsBlocking && f.Severity == Severity.Warning);

    /// <summary>Blocking findings grouped by path: the shape of ASP.NET ModelState / ValidationProblemDetails.</summary>
    public IReadOnlyDictionary<string, string[]> ToErrorDictionary() =>
        Errors.GroupBy(f => f.Path).ToDictionary(g => g.Key, g => g.Select(f => f.Message).ToArray());

    /// <summary>
    /// Writes the proposed changes. A change is applied only if the member still holds the value it had during
    /// evaluation; otherwise it is skipped as a conflict. Applying is idempotent.
    /// </summary>
    /// <remarks>Whether to apply a non-conformant report is the caller's decision; this method does not check.</remarks>
    public ApplyResult Apply()
    {
        var applied = new List<ProposedChange>();
        var skipped = new List<SkippedChange>();

        foreach (var change in Changes)
        {
            var outcome = change.TryApply(out var error);
            if (outcome == ApplyOutcome.Applied) applied.Add(change);
            else skipped.Add(new SkippedChange(change, outcome, error));
        }

        return new ApplyResult(applied, skipped);
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{(IsConformant ? "Conformant" : "Not conformant")}: {Findings.Count} finding(s), {Changes.Count} proposed change(s), {ObjectsVisited} object(s)");
        foreach (var f in Findings) sb.AppendLine($"  {f}");
        foreach (var c in Changes) sb.AppendLine($"  {c}");
        return sb.ToString();
    }
}

/// <summary>A change that canonicalization would make to one member.</summary>
public sealed class ProposedChange
{
    private readonly object _owner;
    private readonly MemberContract _member;

    internal ProposedChange(string path, Type objectType, string member, object? before, object? after,
        IReadOnlyList<string> steps, object owner, MemberContract memberContract)
    {
        Path = path;
        ObjectType = objectType;
        Member = member;
        Before = before;
        After = after;
        Steps = steps;
        _owner = owner;
        _member = memberContract;
    }

    public string Path { get; }
    public Type ObjectType { get; }
    public string Member { get; }
    public object? Before { get; }
    public object? After { get; }

    /// <summary>Names of the normalizers that changed the value, in order.</summary>
    public IReadOnlyList<string> Steps { get; }

    /// <summary>Whether <see cref="ConformanceReport.Apply"/> can write this change (public setter, reference-type owner).</summary>
    public bool CanApply => _member.CanWrite && !_owner.GetType().IsValueType;

    internal ApplyOutcome TryApply(out string? error)
    {
        error = null;
        if (!CanApply) return ApplyOutcome.ReadOnly;

        try
        {
            var current = _member.Getter(_owner);
            if (Equals(current, After)) return ApplyOutcome.AlreadyApplied;
            if (!Equals(current, Before)) return ApplyOutcome.Conflict;
            _member.Setter!(_owner, After);
            return ApplyOutcome.Applied;
        }
        catch (Exception ex)
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
            return ApplyOutcome.Failed;
        }
    }

    public override string ToString() =>
        $"Change  {Path}: {Format.Value(Before)} -> {Format.Value(After)} via {string.Join(" > ", Steps)}{(CanApply ? "" : " (read-only)")}";
}

public enum ApplyOutcome
{
    Applied,

    /// <summary>The member already holds the canonical value.</summary>
    AlreadyApplied,

    /// <summary>The member changed after evaluation; the change was not written.</summary>
    Conflict,

    /// <summary>The member has no public setter, or its owner is a value type.</summary>
    ReadOnly,

    /// <summary>The getter or setter threw.</summary>
    Failed,
}

public sealed record SkippedChange(ProposedChange Change, ApplyOutcome Reason, string? Error);

public sealed record ApplyResult(IReadOnlyList<ProposedChange> Applied, IReadOnlyList<SkippedChange> Skipped);
