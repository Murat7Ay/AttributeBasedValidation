using System.Globalization;

namespace Conform.Annotations;

/// <summary>
/// Base class for normalization attributes. Reflection does not guarantee attribute order, so when a member has
/// more than one normalizer each must declare a distinct <see cref="Order"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public abstract class NormalizeAttribute : Attribute
{
    /// <summary>Position in the member's pipeline (ascending).</summary>
    public int Order { get; set; }

    /// <summary>When true the step is a lens: rules see its output, but no change is proposed.</summary>
    public bool Lens { get; set; }

    public abstract Normalizer CreateNormalizer(Type memberType);
}

/// <summary>Trims leading and trailing whitespace.</summary>
public sealed class TrimAttribute : NormalizeAttribute
{
    public override Normalizer CreateNormalizer(Type memberType) => Normalizers.Trim;
}

/// <summary>Keeps only decimal digits.</summary>
public sealed class DigitsOnlyAttribute : NormalizeAttribute
{
    public override Normalizer CreateNormalizer(Type memberType) => Normalizers.DigitsOnly;
}

/// <summary>Keeps the last N characters; fails (normalization failure) when the value is shorter.</summary>
public sealed class KeepLastAttribute(int length) : NormalizeAttribute
{
    public int Length { get; } = length;

    public override Normalizer CreateNormalizer(Type memberType) => Normalizers.KeepLast(Length);
}

/// <summary>
/// Supplies <see cref="Value"/> (parsed with invariant culture into the member type) when the member is absent:
/// null, whitespace, or the type's default value.
/// </summary>
public sealed class DefaultIfMissingAttribute(string value) : NormalizeAttribute
{
    public string Value { get; } = value;

    public override Normalizer CreateNormalizer(Type memberType)
    {
        var type = Nullable.GetUnderlyingType(memberType) ?? memberType;
        object parsed;
        try
        {
            parsed = type switch
            {
                _ when type == typeof(string) => Value,
                _ when type == typeof(DateOnly) => DateOnly.Parse(Value, CultureInfo.InvariantCulture),
                _ when type == typeof(DateTimeOffset) => DateTimeOffset.Parse(Value, CultureInfo.InvariantCulture),
                _ when type == typeof(Guid) => Guid.Parse(Value),
                _ when type.IsEnum => Enum.Parse(type, Value),
                _ => Convert.ChangeType(Value, type, CultureInfo.InvariantCulture),
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentException or OverflowException)
        {
            throw new ContractDefinitionException($"[DefaultIfMissing(\"{Value}\")] cannot be converted to {type.Name}.", ex);
        }

        return new DefaultValueNormalizer(type, parsed);
    }

    private sealed class DefaultValueNormalizer(Type type, object fallback) : Normalizer($"default-to({fallback})")
    {
        private readonly object? _default = type.IsValueType ? Activator.CreateInstance(type) : null;

        public override Type ValueType => type;

        public override NormalizeResult<object?> NormalizeValue(object value) =>
            NormalizeResult<object?>.Success(Equals(value, _default) ? fallback : value);

        public override NormalizeResult<object?>? NormalizeAbsent() => NormalizeResult<object?>.Success(fallback);
    }
}

/// <summary>
/// Base class for attributes that contribute native Conform rules (with structured codes and expectations).
/// On a class, the rule is an object-level rule. Any BCL <c>ValidationAttribute</c> works too.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class, AllowMultiple = true)]
public abstract class RuleAttribute : Attribute
{
    public Severity Severity { get; set; } = Severity.Error;

    public abstract Rule CreateRule(Type memberType);
}

/// <summary>The date must be today + <see cref="Days"/> or later, by the evaluation clock.</summary>
public sealed class NotBeforeTodayAttribute(int days = 0) : RuleAttribute
{
    public int Days { get; } = days;

    public override Rule CreateRule(Type memberType) => Rules.NotBeforeToday(Days);
}

/// <summary>A present value-type value must not equal its default (e.g. <c>DateTime.MinValue</c>).</summary>
public sealed class NotDefaultAttribute : RuleAttribute
{
    public override Rule CreateRule(Type memberType)
    {
        var type = Nullable.GetUnderlyingType(memberType) ?? memberType;
        if (!type.IsValueType) throw new ContractDefinitionException($"[NotDefault] requires a value type, not {type.Name}.");
        return new NotDefaultRule(type);
    }

    private sealed class NotDefaultRule(Type type) : Rule("not-default", $"a value other than default({type.Name})")
    {
        private readonly object _default = Activator.CreateInstance(type)!;

        public override Type ValueType => type;

        public override Violation? CheckValue(object value, in RuleContext context) =>
            Equals(value, _default) ? new Violation("must not be the default value") : null;
    }
}

/// <summary>Traverse into this member: the object, or every element of an enumerable (nested enumerables included).</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class DescendAttribute : Attribute
{
    public bool AllowNullElements { get; set; }
}

/// <summary>Do not evaluate this member when the evaluation runs in any of the given scenarios.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = true)]
public sealed class SkipInAttribute(params string[] scenarios) : Attribute
{
    public IReadOnlyList<string> Scenarios { get; } = scenarios;
}
