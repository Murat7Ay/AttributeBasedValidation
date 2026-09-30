namespace Conform;

/// <summary>Everything a rule, normalizer condition or activation predicate may know besides the value itself.</summary>
public readonly struct RuleContext
{
    private readonly PathNode? _ownerPath;
    private readonly EvaluationState _state;

    internal RuleContext(object owner, string? memberName, PathNode? ownerPath, EvaluationState state)
    {
        Owner = owner;
        MemberName = memberName;
        _ownerPath = ownerPath;
        _state = state;
    }

    /// <summary>The object that owns the member being checked (the object itself for object rules). Use it for cross-property rules.</summary>
    public object Owner { get; }

    /// <summary>The member being checked, or <c>null</c> for object rules.</summary>
    public string? MemberName { get; }

    /// <summary>The path of the value being checked.</summary>
    public string Path => PathNode.Render(_ownerPath, MemberName);

    /// <summary>The clock of this evaluation. Rules must use it instead of <see cref="DateTime.Now"/>.</summary>
    public TimeProvider Clock => _state.Clock;

    /// <summary>Today's date according to <see cref="Clock"/> (local time).</summary>
    public DateOnly Today => DateOnly.FromDateTime(_state.Clock.GetLocalNow().DateTime);

    /// <summary>The scenarios this evaluation runs in.</summary>
    public IReadOnlyCollection<string> Scenarios => _state.Scenarios;

    public bool InScenario(string scenario) => _state.Scenarios.Contains(scenario);
}

/// <summary>Per-evaluation settings that rules can observe.</summary>
internal sealed class EvaluationState(TimeProvider clock, HashSet<string> scenarios)
{
    public TimeProvider Clock { get; } = clock;
    public HashSet<string> Scenarios { get; } = scenarios;
}

/// <summary>Options for one evaluation.</summary>
public sealed record EvaluationOptions
{
    public static EvaluationOptions Default { get; } = new();

    /// <summary>
    /// Named scenarios this evaluation runs in (e.g. <c>"draft"</c>, <c>"update"</c>).
    /// Members declared with <c>SkipIn(...)</c> for any of these are not evaluated.
    /// </summary>
    public IReadOnlyCollection<string> Scenarios { get; init; } = [];

    /// <summary>Clock used by time-relative rules. Defaults to the system clock; inject a fixed one for determinism.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;
}
