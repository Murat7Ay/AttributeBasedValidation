using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;

namespace Conform.Annotations;

/// <summary>
/// Builds contracts from attributes, once per type:
/// BCL <see cref="RequiredAttribute"/> becomes presence, every other <see cref="ValidationAttribute"/> becomes a
/// rule judged against the canonical value, and Conform's own attributes add normalization, traversal, scenarios
/// and native rules. Public instance properties and fields are scanned; members without relevant attributes are
/// ignored. Types without any relevant attribute get no contract.
/// </summary>
[RequiresUnreferencedCode("Reads attributes and members through reflection. Define contracts in code for trimmed or AOT apps.")]
public sealed class AnnotationContractSource : IContractSource
{
    public TypeContract? TryCreate(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type.IsPointer) return null;

        var members = new List<MemberContract>();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

        foreach (var property in type.GetProperties(flags))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod is null) continue;
            var setter = property.SetMethod is { IsPublic: true } ? property.SetValue : (Action<object, object?>?)null;
            if (Build(type, property, property.PropertyType, property.GetValue, setter) is { } m) members.Add(m);
        }

        foreach (var field in type.GetFields(flags))
        {
            var setter = field.IsInitOnly ? null : (Action<object, object?>)field.SetValue;
            if (Build(type, field, field.FieldType, field.GetValue, setter) is { } m) members.Add(m);
        }

        var objectRules = new List<Rule>();
        foreach (var attribute in Attribute.GetCustomAttributes(type, inherit: true))
        {
            switch (attribute)
            {
                case RuleAttribute r: objectRules.Add(r.CreateRule(type).WithSeverity(r.Severity)); break;
                case ValidationAttribute v: objectRules.Add(new DataAnnotationRule(v, null)); break;
            }
        }

        return members.Count == 0 && objectRules.Count == 0 ? null : new TypeContract(type, members, objectRules);
    }

    private static MemberContract? Build(Type owner, MemberInfo member, Type valueType,
        Func<object, object?> getter, Action<object, object?>? setter)
    {
        var attributes = Attribute.GetCustomAttributes(member, inherit: true);
        if (!attributes.Any(IsRelevant)) return null;

        try
        {
            var normalizeAttributes = attributes.OfType<NormalizeAttribute>().ToArray();
            return new MemberContract(member.Name, valueType, getter, setter)
            {
                IsRequired = attributes.OfType<RequiredAttribute>().Any(),
                Normalizers = Ordered(normalizeAttributes.Where(a => !a.Lens), valueType, owner, member),
                Lenses = Ordered(normalizeAttributes.Where(a => a.Lens), valueType, owner, member),
                Rules =
                [
                    .. attributes.OfType<ValidationAttribute>().Where(a => a is not RequiredAttribute)
                        .Select(a => (Rule)new DataAnnotationRule(a, member.Name)),
                    .. attributes.OfType<RuleAttribute>().Select(a => a.CreateRule(valueType).WithSeverity(a.Severity)),
                ],
                Descend = attributes.OfType<DescendAttribute>().Any(),
                AllowNullElements = attributes.OfType<DescendAttribute>().Any(a => a.AllowNullElements),
                SkipInScenarios = [.. attributes.OfType<SkipInAttribute>().SelectMany(a => a.Scenarios)],
            };
        }
        catch (Exception ex) when (ex is not ContractDefinitionException)
        {
            throw new ContractDefinitionException($"{owner.Name}.{member.Name}: {ex.Message}", ex);
        }
    }

    private static Normalizer[] Ordered(IEnumerable<NormalizeAttribute> attributes, Type valueType, Type owner, MemberInfo member)
    {
        var list = attributes.ToArray();
        if (list.Length > 1 && list.Select(a => a.Order).Distinct().Count() != list.Length)
        {
            throw new ContractDefinitionException(
                $"{owner.Name}.{member.Name}: {list.Length} normalization attributes need distinct Order values; " +
                "attribute order is not guaranteed by reflection.");
        }

        return [.. list.OrderBy(a => a.Order).Select(a => a.CreateNormalizer(valueType))];
    }

    private static bool IsRelevant(Attribute a) =>
        a is ValidationAttribute or NormalizeAttribute or RuleAttribute or DescendAttribute or SkipInAttribute;
}

/// <summary>Adapts any BCL <see cref="ValidationAttribute"/> into a rule. Its semantics and messages are the BCL's.</summary>
internal sealed class DataAnnotationRule(ValidationAttribute attribute, string? memberName)
    : Rule(CodeFor(attribute), Describe(attribute))
{
    public override Type ValueType => typeof(object);

    public override bool AppliesTo(Type valueType) => true;

    public override Violation? CheckValue(object value, in RuleContext context)
    {
        var validationContext = new ValidationContext(context.Owner, memberName ?? context.Owner.GetType().Name, null, null)
        {
            MemberName = memberName,
        };
        var result = attribute.GetValidationResult(value, validationContext);
        return result == ValidationResult.Success ? null : new Violation(result?.ErrorMessage ?? $"{Code} failed");
    }

    private static string CodeFor(ValidationAttribute attribute)
    {
        var name = attribute.GetType().Name;
        if (name.EndsWith("Attribute", StringComparison.Ordinal)) name = name[..^"Attribute".Length];

        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) sb.Append('-');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }

    private static string Describe(ValidationAttribute attribute) => attribute switch
    {
        StringLengthAttribute s => Rules.Bounds(s.MinimumLength, s.MaximumLength, "characters"),
        LengthAttribute l => Rules.Bounds(l.MinimumLength, l.MaximumLength, "characters or items"),
        MinLengthAttribute m => Rules.Bounds(m.Length, int.MaxValue, "characters or items"),
        MaxLengthAttribute m => Rules.Bounds(0, m.Length, "characters or items"),
        RegularExpressionAttribute r => $"fully matches /{r.Pattern}/",
        RangeAttribute r => $"between {r.Minimum} and {r.Maximum}",
        CompareAttribute c => $"equal to {c.OtherProperty}",
        EmailAddressAttribute => "an e-mail address",
        _ => CodeFor(attribute),
    };
}

public static class ContractRegistryAnnotationExtensions
{
    /// <summary>Adds <see cref="AnnotationContractSource"/>: types without an explicit contract are read from their attributes.</summary>
    [RequiresUnreferencedCode("Reads attributes and members through reflection.")]
    public static ContractRegistry UseAnnotations(this ContractRegistry registry) =>
        registry.AddSource(new AnnotationContractSource());
}
