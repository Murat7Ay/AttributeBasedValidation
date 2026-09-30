using System.ComponentModel.DataAnnotations;
using Conform.Annotations;

namespace Conform.Tests;

/// <summary>Everything here is defined outside the engine; nothing in src/ knows about it.</summary>
public class ExtensibilityTests
{
    // --- a custom rule -------------------------------------------------------------------------------------------
    public sealed class LuhnRule() : Rule<string>("luhn", "a number passing the Luhn checksum")
    {
        protected override Violation? Check(string value, in RuleContext context)
        {
            if (value.Length == 0 || !value.All(char.IsAsciiDigit)) return new Violation("must contain only digits");
            var sum = 0;
            for (var i = 0; i < value.Length; i++)
            {
                var d = value[value.Length - 1 - i] - '0';
                if (i % 2 == 1 && (d *= 2) > 9) d -= 9;
                sum += d;
            }
            return sum % 10 == 0 ? null : new Violation("checksum does not match");
        }
    }

    // --- a custom normalizer --------------------------------------------------------------------------------------
    public sealed class CollapseSpaces() : Normalizer<string>("collapse-spaces")
    {
        protected override NormalizeResult<string> Normalize(string value) =>
            string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void Custom_rule_and_normalizer_plug_in_without_engine_changes()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
        {
            c.Member(o => o.Phone).Normalize(Normalizers.DigitsOnly).Check(new LuhnRule());
            c.Member(o => o.Name).Normalize(new CollapseSpaces());
        });

        var report = Eval.Run(registry, new Order { Phone = "4539 1488 0343 6467", Name = "Ada   King" });
        Assert.True(report.IsConformant);
        Assert.Equal("Ada King", Assert.Single(report.Changes, c => c.Member == "Name").After);

        var bad = Eval.Run(registry, new Order { Phone = "4539 1488 0343 6468" });
        Assert.Equal("luhn", Assert.Single(bad.Findings).Code);
    }

    // --- a custom metadata source (e.g. rules loaded from configuration) ------------------------------------------
    public sealed class RequiredMembersSource(Dictionary<Type, string[]> config) : IContractSource
    {
        public TypeContract? TryCreate(Type type)
        {
            if (!config.TryGetValue(type, out var names)) return null;
            return new TypeContract(type, names.Select(n => type.GetProperty(n)!).Select(p =>
                new MemberContract(p.Name, p.PropertyType, p.GetValue) { IsRequired = true }));
        }
    }

    [Fact]
    public void Custom_contract_source_feeds_the_same_engine()
    {
        var registry = new ContractRegistry().AddSource(new RequiredMembersSource(new() { [typeof(Order)] = ["Name", "Phone"] }));
        var report = Eval.Run(registry, new Order { Name = "x" });
        Assert.Equal("Phone", Assert.Single(report.Findings).Path);
    }

    [Fact]
    public void Explicit_definitions_win_over_sources()
    {
        var registry = new ContractRegistry()
            .AddSource(new RequiredMembersSource(new() { [typeof(Order)] = ["Name"] }))
            .Define<Order>(c => c.Member(o => o.Phone).Required());

        Assert.Equal("Phone", Assert.Single(Eval.Run(registry, new Order()).Findings).Path);
    }

    // --- custom attributes ----------------------------------------------------------------------------------------
    public sealed class LuhnAttribute : RuleAttribute
    {
        public override Rule CreateRule(Type memberType) => new LuhnRule();
    }

    public sealed class CollapseSpacesAttribute : NormalizeAttribute
    {
        public override Normalizer CreateNormalizer(Type memberType) => new CollapseSpaces();
    }

    public sealed class EvenAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value) => value is not int i || i % 2 == 0;
        public override string FormatErrorMessage(string name) => $"{name} must be even";
    }

    public class Card
    {
        [DigitsOnly, Luhn] public string? Number { get; set; }
        [CollapseSpaces] public string? Holder { get; set; }
        [Even] public int Seats { get; set; }
    }

    [Fact]
    public void Custom_attributes_of_all_three_kinds_are_adapted()
    {
        var registry = new ContractRegistry().UseAnnotations();
        var report = Eval.Run(registry, new Card { Number = "4539-1488-0343-6468", Holder = "A  B", Seats = 3 });

        Assert.Equal(["luhn", "even"], report.Findings.Select(f => f.Code));
        Assert.Contains("Seats must be even", report.Findings[1].Message);
        Assert.Equal(["Number", "Holder"], report.Changes.Select(c => c.Member));
    }

    [Fact]
    public void Contracts_are_introspectable_for_documentation_or_tooling()
    {
        var registry = new ContractRegistry().UseAnnotations();
        var contract = registry.Resolve(typeof(Card))!;

        var doc = contract.Members.Select(m =>
            $"{m.Name}: {string.Join(", ", m.Normalizers.Select(n => n.Name).Concat(m.Rules.Select(r => r.Description)))}");

        Assert.Equal(
            ["Number: digits-only, a number passing the Luhn checksum", "Holder: collapse-spaces", "Seats: even"],
            doc);
    }

    [Fact]
    public void Resolved_contracts_are_cached()
    {
        var registry = new ContractRegistry().UseAnnotations();
        Assert.Same(registry.Resolve(typeof(Card)), registry.Resolve(typeof(Card)));
    }

    [Fact]
    public void Core_assembly_does_not_depend_on_attributes()
    {
        var references = typeof(ConformanceEngine).Assembly.GetReferencedAssemblies().Select(a => a.Name);
        Assert.DoesNotContain("Conform.Annotations", references);
        Assert.DoesNotContain("System.ComponentModel.Annotations", references);
        Assert.DoesNotContain(typeof(ConformanceEngine).Assembly.GetExportedTypes(), t => t.IsSubclassOf(typeof(Attribute)));
    }
}
