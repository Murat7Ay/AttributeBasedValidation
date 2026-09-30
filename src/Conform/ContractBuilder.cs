using System.Linq.Expressions;
using System.Reflection;

namespace Conform;

/// <summary>Typed authoring surface that produces a <see cref="TypeContract"/> for <typeparamref name="T"/>.</summary>
public sealed class ContractBuilder<T> where T : notnull
{
    private readonly List<IMemberBuilder> _members = [];
    private readonly Dictionary<string, IMemberBuilder> _byName = new(StringComparer.Ordinal);
    private readonly List<Rule> _objectRules = [];

    /// <summary>
    /// Declares (or continues declaring) the contract of a direct property or field of <typeparamref name="T"/>.
    /// Members are evaluated in the order they are first declared.
    /// </summary>
    public MemberBuilder<T, TValue> Member<TValue>(Expression<Func<T, TValue>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var member = MemberAccess.Resolve(selector);

        if (_byName.TryGetValue(member.Name, out var existing))
        {
            return existing as MemberBuilder<T, TValue>
                ?? throw new ContractDefinitionException($"{typeof(T).Name}.{member.Name} was declared with a different type.");
        }

        var builder = new MemberBuilder<T, TValue>(member);
        _byName.Add(member.Name, builder);
        _members.Add(builder);
        return builder;
    }

    /// <summary>Adds an object-level rule (its value is the whole object).</summary>
    public ContractBuilder<T> Check(Rule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _objectRules.Add(rule);
        return this;
    }

    /// <summary>Adds an object-level predicate, typically a cross-property constraint.</summary>
    public ContractBuilder<T> Must(string code, Func<T, bool> predicate, string message) =>
        Check(Rules.Must(code, predicate, message));

    internal TypeContract Build() => new(typeof(T), _members.Select(m => m.Build()), _objectRules);
}

internal interface IMemberBuilder
{
    MemberContract Build();
}

/// <summary>Declares the contract of one member. Every method returns the same builder for chaining.</summary>
public sealed class MemberBuilder<TOwner, TValue> : IMemberBuilder where TOwner : notnull
{
    private readonly MemberInfo _member;
    private readonly List<Normalizer> _normalizers = [];
    private readonly List<Normalizer> _lenses = [];
    private readonly List<Rule> _rules = [];
    private readonly List<string> _skipIn = [];
    private Func<RuleContext, bool>? _condition;
    private bool _required, _descend, _allowNullElements;

    internal MemberBuilder(MemberInfo member) => _member = member;

    /// <summary>The value must be present: not null, and for strings not empty or whitespace (after normalization).</summary>
    public MemberBuilder<TOwner, TValue> Required()
    {
        _required = true;
        return this;
    }

    /// <summary>
    /// Appends canonicalization steps. Rules judge the canonical value, and if it differs from the current value a
    /// change is proposed. Nothing is written until <see cref="ConformanceReport.Apply"/>.
    /// </summary>
    public MemberBuilder<TOwner, TValue> Normalize(params Normalizer[] normalizers)
    {
        _normalizers.AddRange(normalizers);
        return this;
    }

    /// <summary>
    /// Appends evaluation-only steps: rules see the transformed value, but no change is ever proposed.
    /// Lenses run after all <see cref="Normalize"/> steps.
    /// </summary>
    public MemberBuilder<TOwner, TValue> Lens(params Normalizer[] normalizers)
    {
        _lenses.AddRange(normalizers);
        return this;
    }

    /// <summary>Attaches a rule.</summary>
    public MemberBuilder<TOwner, TValue> Check(Rule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        _rules.Add(rule);
        return this;
    }

    /// <summary>Attaches a predicate over the (present) value.</summary>
    public MemberBuilder<TOwner, TValue> Must(string code, Func<TValue, bool> predicate, string message) =>
        Check(Rules.Must(code, predicate, message));

    /// <summary>Attaches a predicate over the (present) value and its owner, for cross-property rules.</summary>
    public MemberBuilder<TOwner, TValue> Must(string code, Func<TOwner, TValue, bool> predicate, string message) =>
        Check(Rules.Must<TValue>(code, (v, ctx) => predicate((TOwner)ctx.Owner, v), message));

    /// <summary>Follows this member during traversal: into the object, or into every element of an enumerable.</summary>
    public MemberBuilder<TOwner, TValue> Descend(bool allowNullElements = false)
    {
        _descend = true;
        _allowNullElements = allowNullElements;
        return this;
    }

    /// <summary>Evaluates the member only when <paramref name="condition"/> holds for the owner.</summary>
    public MemberBuilder<TOwner, TValue> When(Func<TOwner, bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return When((owner, _) => condition(owner));
    }

    /// <summary>Evaluates the member only when <paramref name="condition"/> holds for the owner and context.</summary>
    public MemberBuilder<TOwner, TValue> When(Func<TOwner, RuleContext, bool> condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var previous = _condition;
        _condition = previous is null
            ? ctx => condition((TOwner)ctx.Owner, ctx)
            : ctx => previous(ctx) && condition((TOwner)ctx.Owner, ctx);
        return this;
    }

    /// <summary>Skips the member entirely when the evaluation runs in any of these scenarios.</summary>
    public MemberBuilder<TOwner, TValue> SkipIn(params string[] scenarios)
    {
        _skipIn.AddRange(scenarios);
        return this;
    }

    MemberContract IMemberBuilder.Build()
    {
        var (getter, setter) = MemberAccess.CreateAccessors<TOwner, TValue>(_member);
        return new MemberContract(_member.Name, typeof(TValue), getter, setter)
        {
            IsRequired = _required,
            Normalizers = [.. _normalizers],
            Lenses = [.. _lenses],
            Rules = [.. _rules],
            Descend = _descend,
            AllowNullElements = _allowNullElements,
            SkipInScenarios = [.. _skipIn],
            Condition = _condition,
        };
    }
}

internal static class MemberAccess
{
    public static MemberInfo Resolve<T, TValue>(Expression<Func<T, TValue>> selector)
    {
        var body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert } convert ? convert.Operand : selector.Body;
        if (body is MemberExpression { Member: PropertyInfo or FieldInfo } access && access.Expression == selector.Parameters[0])
            return access.Member;

        throw new ContractDefinitionException(
            $"'{selector}' must select a property or field directly on {typeof(T).Name} (e.g. x => x.Name). " +
            "Nested members get their own contract; use Descend() to reach them.");
    }

    /// <summary>
    /// Builds accessors once per member. Class owners with property accessors get bound delegates (no reflection per
    /// access); fields and struct owners fall back to reflection. Only public setters (including init) are writable,
    /// and never on struct owners, where a write would land on a boxed copy.
    /// </summary>
    public static (Func<object, object?> Getter, Action<object, object?>? Setter) CreateAccessors<TOwner, TValue>(MemberInfo member)
    {
        switch (member)
        {
            // Exact types only: a converting selector (x => (object)x.Count) cannot bind a typed delegate.
            case PropertyInfo p when !typeof(TOwner).IsValueType && p.PropertyType == typeof(TValue) && p.GetMethod is { IsStatic: false } get:
            {
                var typedGet = get.CreateDelegate<Func<TOwner, TValue>>();
                Action<object, object?>? set = null;
                if (p.SetMethod is { IsPublic: true, IsStatic: false } setMethod)
                {
                    var typedSet = setMethod.CreateDelegate<Action<TOwner, TValue>>();
                    set = (o, v) => typedSet((TOwner)o, (TValue)v!);
                }

                return (o => typedGet((TOwner)o), set);
            }

            case PropertyInfo p:
                return (p.GetValue, !typeof(TOwner).IsValueType && p.SetMethod is { IsPublic: true } ? p.SetValue : null);

            case FieldInfo f:
                return (f.GetValue, typeof(TOwner).IsValueType || f.IsInitOnly || !f.IsPublic ? null : f.SetValue);

            default:
                throw new ContractDefinitionException($"Unsupported member {member.Name}.");
        }
    }
}
