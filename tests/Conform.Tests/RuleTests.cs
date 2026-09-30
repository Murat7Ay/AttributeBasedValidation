namespace Conform.Tests;

public class RuleTests
{
    [Fact]
    public void All_violations_are_aggregated_in_declaration_order()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
        {
            c.Member(o => o.Name).Required();
            c.Member(o => o.Phone).Length(10).Matches("^5");
            c.Member(o => o.Quantity).Range(1, 10);
        });

        var codes = Eval.Run(registry, new Order { Phone = "123" }).Findings.Select(f => f.Code);

        Assert.Equal(["required", "length", "pattern", "range"], codes);
    }

    [Fact]
    public void Conflicting_rules_both_report()
    {
        var registry = new ContractRegistry().Define<Order>(c => c.Member(o => o.Name).Length(min: 5).Length(max: 3));
        // 4 characters violates both bounds: unsatisfiable contracts are visible, not silently resolved.
        Assert.Equal(["length", "length"], Eval.Run(registry, new Order { Name = "abcd" }).Findings.Select(f => f.Code));
        Assert.Single(Eval.Run(registry, new Order { Name = "ab" }).Findings);
        Assert.Single(Eval.Run(registry, new Order { Name = "abcdef" }).Findings);
    }

    [Fact]
    public void Conditional_members_depend_on_sibling_state()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
            c.Member(o => o.Phone).When(o => o.Quantity > 5).Required());

        Assert.True(Eval.Run(registry, new Order { Quantity = 1 }).IsConformant);
        Assert.False(Eval.Run(registry, new Order { Quantity = 6 }).IsConformant);
    }

    [Fact]
    public void Conditions_can_use_scenarios()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
            c.Member(o => o.Phone).When((_, ctx) => ctx.InScenario("checkout")).Required());

        Assert.True(Eval.Run(registry, new Order()).IsConformant);
        Assert.False(Eval.Run(registry, new Order(), "checkout").IsConformant);
    }

    [Fact]
    public void Cross_property_object_rule()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
            c.Must("finish-after-start", o => o.Finish >= o.Start, "Finish must not be before Start"));

        var report = Eval.Run(registry, new Order { Start = new DateTime(2026, 2, 1), Finish = new DateTime(2026, 1, 1) });

        var f = Assert.Single(report.Findings);
        Assert.Equal(("", null, "finish-after-start"), (f.Path, f.Member, f.Code));
    }

    [Fact]
    public void Cross_property_member_rule_reads_owner()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
            c.Member(o => o.Finish).Must("after-start", (o, finish) => finish > o.Start, "must be after Start"));

        var f = Assert.Single(Eval.Run(registry, new Order { Start = new DateTime(2026, 1, 2), Finish = new DateTime(2026, 1, 1) }).Findings);
        Assert.Equal("Finish", f.Path);
    }

    [Fact]
    public void Warnings_and_infos_do_not_block_conformance()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
        {
            c.Member(o => o.Note).Check(Rules.Length(max: 5).AsWarning());
            c.Member(o => o.Name).Check(Rules.Must<string>("shouty", s => s != s.ToUpperInvariant(), "all caps").AsInfo());
        });

        var report = Eval.Run(registry, new Order { Note = "too long note", Name = "ABC" });

        Assert.True(report.IsConformant);
        Assert.Equal([Severity.Warning, Severity.Info], report.Findings.Select(f => f.Severity));
        Assert.Single(report.Warnings);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public void Severity_copies_do_not_affect_the_original_rule()
    {
        var rule = Rules.Length(max: 1);
        _ = rule.AsWarning();
        Assert.Equal(Severity.Error, rule.Severity);
    }

    [Fact]
    public void Throwing_rules_are_faults_not_passes()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
            c.Member(o => o.Name).Must("boom", _ => throw new InvalidOperationException("bad rule"), "never"));

        var f = Assert.Single(Eval.Run(registry, new Order { Name = "x" }).Findings);
        Assert.Equal((FindingKind.Fault, "boom"), (f.Kind, f.Code));
    }

    [Fact]
    public void Throwing_conditions_are_faults()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
            c.Member(o => o.Name).When(_ => throw new InvalidOperationException()).Required());
        Assert.Equal("condition-fault", Assert.Single(Eval.Run(registry, new Order()).Findings).Code);
    }

    [Fact]
    public void Scenario_skips_remove_the_member_entirely()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
            c.Member(o => o.Phone).SkipIn("draft", "import").Required().Normalize(Normalizers.Trim));

        Assert.False(Eval.Run(registry, new Order()).IsConformant);
        var skipped = Eval.Run(registry, new Order { Phone = " 1 " }, "import");
        Assert.Empty(skipped.Findings);
        Assert.Empty(skipped.Changes);
    }

    [Fact]
    public void Declaring_a_member_twice_continues_the_same_contract()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
        {
            c.Member(o => o.Name).Required();
            c.Member(o => o.Name).Length(min: 3);
        });

        var contract = registry.Resolve(typeof(Order))!;
        var name = Assert.Single(contract.Members);
        Assert.True(name.IsRequired);
        Assert.Single(name.Rules);
    }
}
