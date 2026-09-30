namespace Conform;

/// <summary>
/// A named, described, severity-bearing check over one observed value.
/// Rules are immutable values: they can be shared between members and contracts.
/// </summary>
/// <remarks>
/// Rules never see absent values (null, or whitespace-only strings). Presence is decided once, by the member.
/// A rule that throws produces a <see cref="FindingKind.Fault"/> finding; it is never treated as a pass.
/// </remarks>
public abstract class Rule
{
    protected Rule(string code, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Description = description ?? string.Empty;
    }

    /// <summary>Stable machine-readable code, reported on every finding the rule produces.</summary>
    public string Code { get; }

    /// <summary>What the rule expects, in words. Used as <see cref="Finding.Expected"/> and for documentation.</summary>
    public string Description { get; }

    public Severity Severity { get; private set; } = Severity.Error;

    /// <summary>The value type the rule is written for.</summary>
    public abstract Type ValueType { get; }

    /// <summary>Whether the rule can be attached to a member of <paramref name="valueType"/>. Checked when the contract is built.</summary>
    public virtual bool AppliesTo(Type valueType) => TypeCompat.Compatible(ValueType, valueType);

    /// <summary>Checks a present value. Returns <c>null</c> when the value passes.</summary>
    public abstract Violation? CheckValue(object value, in RuleContext context);

    /// <summary>Returns a copy of this rule that reports with a different severity.</summary>
    public Rule WithSeverity(Severity severity)
    {
        var copy = (Rule)MemberwiseClone();
        copy.Severity = severity;
        return copy;
    }

    public Rule AsWarning() => WithSeverity(Severity.Warning);

    public Rule AsInfo() => WithSeverity(Severity.Info);

    public override string ToString() => $"{Code}: {Description}";
}

/// <summary>Base class for strongly typed rules.</summary>
public abstract class Rule<T> : Rule
{
    protected Rule(string code, string description) : base(code, description) { }

    public sealed override Type ValueType => typeof(T);

    public sealed override Violation? CheckValue(object value, in RuleContext context) => value is T typed
        ? Check(typed, context)
        : throw new InvalidOperationException(
            $"Rule '{Code}' expects {typeof(T).Name} but received {value.GetType().Name}.");

    /// <summary>Checks a present value. Returns <c>null</c> when the value passes.</summary>
    protected abstract Violation? Check(T value, in RuleContext context);
}

internal static class TypeCompat
{
    public static Type Unwrap(Type t) => Nullable.GetUnderlyingType(t) ?? t;

    /// <summary>
    /// A rule/normalizer for <paramref name="declared"/> fits a member of <paramref name="member"/> when a value of
    /// the member could be the declared type (either direction of assignability; the runtime check decides).
    /// </summary>
    public static bool Compatible(Type declared, Type member)
    {
        var d = Unwrap(declared);
        var m = Unwrap(member);
        return d.IsAssignableFrom(m) || m.IsAssignableFrom(d);
    }
}
