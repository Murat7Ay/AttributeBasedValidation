namespace Conform;

/// <summary>
/// The contract of one member: how to read it (and optionally write it), whether it must be present, how its
/// canonical form is computed, which rules judge that form, and whether the engine descends into it.
/// </summary>
/// <remarks>
/// This is plain, inspectable metadata. The code builder, the attribute adapter, or any other
/// <see cref="IContractSource"/> produce it; the engine only interprets it.
/// </remarks>
public sealed class MemberContract
{
    public MemberContract(string name, Type valueType, Func<object, object?> getter, Action<object, object?>? setter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
        Getter = getter ?? throw new ArgumentNullException(nameof(getter));
        Setter = setter;
    }

    public string Name { get; }
    public Type ValueType { get; }
    public Func<object, object?> Getter { get; }
    public Action<object, object?>? Setter { get; }
    public bool CanWrite => Setter is not null;

    /// <summary>An absent value (null or whitespace-only string, after normalization) is a <c>required</c> violation.</summary>
    public bool IsRequired { get; init; }

    /// <summary>Canonicalization steps, in order. Their output is proposed as a change and judged by <see cref="Rules"/>.</summary>
    public IReadOnlyList<Normalizer> Normalizers { get; init; } = [];

    /// <summary>
    /// Evaluation-only steps applied after <see cref="Normalizers"/>. Rules see their output, but it is never proposed
    /// as a change ("judge it as digits only, but keep what the user typed").
    /// </summary>
    public IReadOnlyList<Normalizer> Lenses { get; init; } = [];

    /// <summary>Rules over the observed (canonical, then lensed) value. They only run when the value is present.</summary>
    public IReadOnlyList<Rule> Rules { get; init; } = [];

    /// <summary>Follow this member during traversal: into the object, or into each element of an enumerable.</summary>
    public bool Descend { get; init; }

    /// <summary>When descending into an enumerable, whether null elements are allowed (otherwise: <c>null-element</c> violation).</summary>
    public bool AllowNullElements { get; init; }

    /// <summary>The member is not evaluated in any of these scenarios.</summary>
    public IReadOnlyList<string> SkipInScenarios { get; init; } = [];

    /// <summary>The member is only evaluated when this returns true. <see cref="RuleContext.Owner"/> is the owning object.</summary>
    public Func<RuleContext, bool>? Condition { get; init; }

    internal void Validate(Type ownerType)
    {
        foreach (var n in Normalizers.Concat(Lenses))
        {
            if (!n.AppliesTo(ValueType))
                throw new ContractDefinitionException(
                    $"{ownerType.Name}.{Name}: normalizer '{n.Name}' works on {n.ValueType.Name}, but the member is {ValueType.Name}.");
        }

        foreach (var r in Rules)
        {
            if (!r.AppliesTo(ValueType))
                throw new ContractDefinitionException(
                    $"{ownerType.Name}.{Name}: rule '{r.Code}' checks {r.ValueType.Name}, but the member is {ValueType.Name}.");
        }
    }

    public override string ToString() => Name;
}

/// <summary>The contract of one type: its member contracts (in evaluation order) and object-level rules.</summary>
public sealed class TypeContract
{
    public TypeContract(Type type, IEnumerable<MemberContract> members, IEnumerable<Rule>? objectRules = null)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Members = members?.ToArray() ?? throw new ArgumentNullException(nameof(members));
        ObjectRules = objectRules?.ToArray() ?? [];

        var duplicate = Members.GroupBy(m => m.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ContractDefinitionException($"{type.Name}: member '{duplicate.Key}' is defined more than once.");

        foreach (var m in Members) m.Validate(type);

        foreach (var r in ObjectRules)
        {
            if (!r.AppliesTo(type))
                throw new ContractDefinitionException($"{type.Name}: object rule '{r.Code}' checks {r.ValueType.Name}.");
        }
    }

    public Type Type { get; }
    public IReadOnlyList<MemberContract> Members { get; }

    /// <summary>Rules whose value is the whole object; use them for cross-property constraints.</summary>
    public IReadOnlyList<Rule> ObjectRules { get; }

    public override string ToString() => $"Contract<{Type.Name}> ({Members.Count} members, {ObjectRules.Count} object rules)";
}

/// <summary>A source of contracts for types that were not defined explicitly (attributes, generated code, files, ...).</summary>
public interface IContractSource
{
    /// <summary>Returns the contract for <paramref name="type"/>, or <c>null</c> if this source has nothing to say about it.</summary>
    TypeContract? TryCreate(Type type);
}

/// <summary>A contract is malformed. This is a programming error, raised when the contract is built, never mid-data.</summary>
public sealed class ContractDefinitionException(string message, Exception? inner = null) : Exception(message, inner);
