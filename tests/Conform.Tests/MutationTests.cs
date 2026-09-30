namespace Conform.Tests;

public class MutationTests
{
    private static readonly ContractRegistry Registry = new ContractRegistry()
        .Define<Order>(c =>
        {
            c.Member(o => o.Name).Normalize(Normalizers.Trim).Required();
            c.Member(o => o.Phone).Normalize(Normalizers.DigitsOnly, Normalizers.KeepLast(10)).Matches(@"^5\d{9}$");
            c.Member(o => o.EndDate).Normalize(Normalizers.DefaultTo(new DateTime(2020, 12, 31)));
            c.Member(o => o.Lines).Descend();
        })
        .Define<Line>(c => c.Member(l => l.Barcode).Normalize(Normalizers.Trim, Normalizers.UpperInvariant));

    private static Order Messy() => new()
    {
        Name = "  Ada  ",
        Phone = "+90 (532) 123 45 67",
        Lines = [new Line { Barcode = " ab-1 " }],
    };

    [Fact]
    public void Evaluation_never_mutates()
    {
        var order = Messy();
        var report = Eval.Run(Registry, order);

        Assert.Equal(4, report.Changes.Count);
        Assert.Equal("  Ada  ", order.Name);
        Assert.Equal("+90 (532) 123 45 67", order.Phone);
        Assert.Null(order.EndDate);
        Assert.Equal(" ab-1 ", order.Lines![0].Barcode);
    }

    [Fact]
    public void Evaluating_twice_is_deterministic()
    {
        var order = Messy();
        var a = Eval.Run(Registry, order);
        var b = Eval.Run(Registry, order);
        Assert.Equal(a.Findings, b.Findings);
        Assert.Equal(a.Changes.Select(c => c.ToString()), b.Changes.Select(c => c.ToString()));
    }

    [Fact]
    public void Apply_writes_all_changes_and_is_idempotent()
    {
        var order = Messy();
        var report = Eval.Run(Registry, order);

        var first = report.Apply();
        Assert.Equal(4, first.Applied.Count);
        Assert.Equal(("Ada", "5321234567", "AB-1"), (order.Name, order.Phone, order.Lines![0].Barcode));
        Assert.Equal(new DateTime(2020, 12, 31), order.EndDate);

        var second = report.Apply();
        Assert.Empty(second.Applied);
        Assert.All(second.Skipped, s => Assert.Equal(ApplyOutcome.AlreadyApplied, s.Reason));

        Assert.Empty(Eval.Run(Registry, order).Changes); // canonical is a fixed point
    }

    [Fact]
    public void Apply_does_not_overwrite_values_changed_after_evaluation()
    {
        var order = Messy();
        var report = Eval.Run(Registry, order);
        order.Name = "Grace";

        var result = report.Apply();

        Assert.Equal("Grace", order.Name);
        Assert.Contains(result.Skipped, s => s.Change.Member == "Name" && s.Reason == ApplyOutcome.Conflict);
    }

    [Fact]
    public void Rules_judge_the_canonical_value()
    {
        var report = Eval.Run(Registry, new Order { Name = "   x   ", Phone = "0 (532) 123-45-67" });
        Assert.True(report.IsConformant);
    }

    [Fact]
    public void Whitespace_that_normalizes_to_empty_is_absent()
    {
        var f = Assert.Single(Eval.Run(Registry, new Order { Name = "     " }).Findings);
        Assert.Equal("required", f.Code);
    }

    [Fact]
    public void Lens_shapes_what_rules_see_without_proposing_a_change()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
            c.Member(o => o.Phone).Lens(Normalizers.DigitsOnly).Length(10, 10));

        var order = new Order { Phone = "532-123-45-67" };
        var report = Eval.Run(registry, order);

        Assert.True(report.IsConformant);
        Assert.Empty(report.Changes);
    }

    [Fact]
    public void Lens_runs_after_normalizers()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
            c.Member(o => o.Phone).Lens(Normalizers.DigitsOnly).Normalize(Normalizers.Trim).Length(3, 3));

        var report = Eval.Run(registry, new Order { Phone = " 1-2-3 " });

        Assert.True(report.IsConformant);
        Assert.Equal("1-2-3", Assert.Single(report.Changes).After);
    }

    [Fact]
    public void Failed_normalization_proposes_nothing_and_skips_rules()
    {
        var report = Eval.Run(Registry, new Order { Name = "x", Phone = "12-34" });

        var f = Assert.Single(report.Findings);
        Assert.Equal(FindingKind.NormalizationFailed, f.Kind);
        Assert.Equal("1234", f.Observed); // the value the failing step received
        Assert.DoesNotContain(report.Changes, c => c.Member == "Phone");
    }

    [Fact]
    public void Throwing_normalizers_are_faults()
    {
        var registry = new ContractRegistry().Define<Order>(c =>
            c.Member(o => o.Name).Normalize(Normalizers.From<string>("explode", _ => throw new FormatException("nope"))));

        var f = Assert.Single(Eval.Run(registry, new Order { Name = "x" }).Findings);
        Assert.Equal((FindingKind.Fault, "normalizer-fault"), (f.Kind, f.Code));
    }

    [Fact]
    public void Read_only_members_get_proposals_that_cannot_be_applied()
    {
        var registry = new ContractRegistry().Define<Frozen>(c => c.Member(f => f.Code).Normalize(Normalizers.Trim));
        var frozen = new Frozen(" a ");

        var report = Eval.Run(registry, frozen);
        var change = Assert.Single(report.Changes);
        Assert.False(change.CanApply);
        Assert.Equal(ApplyOutcome.ReadOnly, Assert.Single(report.Apply().Skipped).Reason);
        Assert.Equal(" a ", frozen.Code);
    }

    [Fact]
    public void Init_only_record_members_can_be_applied()
    {
        var registry = new ContractRegistry().Define<Dto>(c => c.Member(d => d.Code).Normalize(Normalizers.Trim));
        var dto = new Dto { Code = " a " };
        Eval.Run(registry, dto).Apply();
        Assert.Equal("a", dto.Code);
    }

    [Fact]
    public void Struct_owners_are_never_written()
    {
        var registry = new ContractRegistry().Define<Point>(c => c.Member(p => p.Label).Normalize(Normalizers.Trim));
        var report = Eval.Run(registry, new Point { Label = " p " });
        Assert.False(Assert.Single(report.Changes).CanApply);
    }

    public sealed class Frozen(string code)
    {
        public string Code { get; } = code;
    }

    public sealed record Dto
    {
        public string? Code { get; init; }
    }

    public struct Point
    {
        public string? Label { get; set; }
    }
}
