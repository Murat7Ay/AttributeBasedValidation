using System.Text.RegularExpressions;

namespace Conform.Tests;

public class BoundaryTests
{
    private static ContractRegistry For(Action<ContractBuilder<Order>> define) => new ContractRegistry().Define(define);

    [Fact]
    public void Null_root_is_a_required_violation_not_an_exception()
    {
        var report = Eval.Run<Order?>(For(_ => { }), null);
        var f = Assert.Single(report.Findings);
        Assert.Equal(("", "required", typeof(Order)), (f.Path, f.Code, f.ObjectType));
    }

    [Fact]
    public void Root_without_contract_is_a_programming_error()
    {
        Assert.Throws<InvalidOperationException>(() => new ConformanceEngine(new ContractRegistry()).Evaluate(new Order()));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("\t\n", true)]
    [InlineData("x", false)]
    public void Required_treats_null_empty_and_whitespace_as_absent(string? name, bool violated)
    {
        var report = Eval.Run(For(c => c.Member(o => o.Name).Required()), new Order { Name = name });
        Assert.Equal(violated, report.Findings.Any(f => f.Code == "required"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Absent_optional_values_skip_rules(string? name)
    {
        var report = Eval.Run(For(c => c.Member(o => o.Name).Length(5, 6).Matches("^z$")), new Order { Name = name });
        Assert.Empty(report.Findings);
    }

    [Theory]
    [InlineData("123456789", false)]  // min - 1
    [InlineData("1234567890", true)]  // min
    [InlineData("123456789012345", true)] // max
    [InlineData("1234567890123456", false)] // max + 1
    public void Length_bounds_are_inclusive(string value, bool ok)
    {
        var report = Eval.Run(For(c => c.Member(o => o.Name).Length(10, 15)), new Order { Name = value });
        Assert.Equal(ok, report.IsConformant);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    [InlineData(int.MinValue, false)]
    [InlineData(int.MaxValue, false)]
    public void Range_bounds_are_inclusive(int quantity, bool ok)
    {
        var report = Eval.Run(For(c => c.Member(o => o.Quantity).Range(1, 100)), new Order { Quantity = quantity });
        Assert.Equal(ok, report.IsConformant);
    }

    [Fact]
    public void Value_type_default_is_present_unless_NotDefault_is_declared()
    {
        var registry = For(c => c.Member(o => o.Appointment).Required());
        Assert.True(Eval.Run(registry, new Order()).IsConformant);

        var strict = For(c => c.Member(o => o.Appointment).NotDefault());
        Assert.Equal("not-default", Assert.Single(Eval.Run(strict, new Order()).Findings).Code);
    }

    [Theory]
    [InlineData(0, false)] // today
    [InlineData(1, true)]  // tomorrow
    [InlineData(-1, false)]
    public void Relative_date_uses_evaluation_clock(int offsetDays, bool ok)
    {
        var registry = For(c => c.Member(o => o.Appointment).NotBeforeToday(1));
        var report = Eval.Run(registry, new Order { Appointment = FixedClock.Today.AddDays(offsetDays).AddHours(23) });
        Assert.Equal(ok, report.IsConformant);
    }

    [Fact]
    public void Nullable_members_accept_underlying_rules()
    {
        var registry = For(c => c.Member(o => o.EndDate).NotBeforeToday().NotDefault());
        Assert.True(Eval.Run(registry, new Order { EndDate = null }).IsConformant);
        Assert.False(Eval.Run(registry, new Order { EndDate = default(DateTime) }).IsConformant);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(2, 2, true)]
    [InlineData(2, 3, false)]
    public void Count_bounds_on_collections(int min, int items, bool ok)
    {
        var registry = For(c => c.Member(o => o.Lines).Count(min, 2));
        var order = new Order { Lines = Enumerable.Range(0, items).Select(_ => new Line()).ToList() };
        Assert.Equal(ok, Eval.Run(registry, order).IsConformant);
    }

    [Fact]
    public void Count_works_on_non_ICollection_sets()
    {
        var registry = For(c => c.Member(o => o.Set).Count(min: 1));
        Assert.Equal("count", Assert.Single(Eval.Run(registry, new Order { Set = [] }).Findings).Code);
    }

    [Fact]
    public void KeepLast_on_exact_length_succeeds_and_on_shorter_fails()
    {
        var registry = For(c => c.Member(o => o.Phone).Normalize(Normalizers.KeepLast(3)));
        Assert.True(Eval.Run(registry, new Order { Phone = "123" }).IsConformant);
        Assert.Equal(FindingKind.NormalizationFailed, Assert.Single(Eval.Run(registry, new Order { Phone = "12" }).Findings).Kind);
    }

    [Fact]
    public void Converting_selectors_are_supported()
    {
        var registry = For(c => c.Member(o => (object)o.Quantity).Must("positive", q => (int)q > 0, "must be positive"));
        Assert.Equal("Quantity", Assert.Single(Eval.Run(registry, new Order()).Findings).Path);
    }

    [Fact]
    public void Malformed_definitions_fail_at_definition_time()
    {
        Assert.Throws<RegexParseException>(() => For(c => c.Member(o => o.Name).Matches("(unclosed")));
        Assert.Throws<ArgumentOutOfRangeException>(() => For(c => c.Member(o => o.Name).Length(5, 1)));
        Assert.Throws<ContractDefinitionException>(() => For(c => c.Member(o => o.Main!.Name))); // nested selector
        Assert.Throws<ContractDefinitionException>(() => For(c => c.Member(o => o.Quantity).Normalize(Normalizers.Trim)));
        Assert.Throws<ContractDefinitionException>(() => For(c => c.Member(o => o.Quantity).Check(Rules.Length(1))));
    }
}
