using System.ComponentModel.DataAnnotations;
using Conform.Annotations;

namespace Conform.Tests;

/// <summary>
/// The useful semantics of the 2019 implementation, expressed in the new model. Each test names the legacy behavior
/// it keeps (or the bug it deliberately does not keep).
/// </summary>
public class LegacySemanticsTests
{
    // The legacy Master/Child models, re-declared with the new attribute vocabulary.
    public class Master
    {
        [Required] public string? Name { get; set; }

        [Required, DigitsOnly(Order = 1), KeepLast(10, Order = 2), StringLength(15, MinimumLength = 10)]
        [RegularExpression(@"5\d{9}")]
        public string? GsmNumber { get; set; }

        [NotBeforeToday(1)] public DateTime AppointmentDate { get; set; }

        [DefaultIfMissing("2020-12-31")] public DateTime? EndDate { get; set; }

        [Required, MinLength(1), Descend] public IList<Child>? CantBeNull { get; set; }

        [Required, Descend] public Child? Child { get; set; }

        [Required, SkipIn("partial")] public string? IgnoreAbleProperty { get; set; }
    }

    public class Child
    {
        [Required] public string? Name { get; set; }
        [Required, StringLength(15, MinimumLength = 10)] public string? Barcode { get; set; }
        [Descend] public IList<Child>? NestedChildren { get; set; }
        [Descend] public Child? NestedChild { get; set; } // optional now; the legacy model was unsatisfiable
    }

    private static readonly ContractRegistry Registry = new ContractRegistry().UseAnnotations();

    private static Master ReadmeExample() => new()
    {
        CantBeNull = [new Child { NestedChildren = [], Barcode = "12345678900", Name = "Murat Ay" }],
        Child = new Child { NestedChildren = [], Barcode = "12345678900", Name = "Murat Ay" },
        Name = "Murat Ay",
        AppointmentDate = FixedClock.Today.AddDays(-1),
        EndDate = null,
        GsmNumber = "+90 588 888 88 88",
        IgnoreAbleProperty = null,
    };

    [Fact]
    public void Readme_example_is_judged_on_canonical_phone_and_reports_the_past_appointment()
    {
        var report = Eval.Run(Registry, ReadmeExample(), "partial");

        // The legacy date rule never ran (bug). Here it does, and it is the only violation.
        var finding = Assert.Single(report.Findings);
        Assert.Equal("AppointmentDate", finding.Path);
        Assert.Equal("min-date", finding.Code);
    }

    [Fact]
    public void Phone_canonical_form_is_proposed_not_written()
    {
        var model = ReadmeExample();
        var report = Eval.Run(Registry, model, "partial");

        var change = Assert.Single(report.Changes, c => c.Member == "GsmNumber");
        Assert.Equal("+90 588 888 88 88", change.Before);
        Assert.Equal("5888888888", change.After);
        Assert.Equal(["digits-only", "keep-last-10"], change.Steps);
        Assert.Equal("+90 588 888 88 88", model.GsmNumber); // untouched until Apply

        report.Apply();
        Assert.Equal("5888888888", model.GsmNumber);
    }

    [Fact]
    public void Regex_failure_is_reported_as_pattern_with_canonical_value_observed()
    {
        var model = ReadmeExample();
        model.GsmNumber = "+90 488 888 88 88";
        var report = Eval.Run(Registry, model, "partial");

        var finding = Eval.Single(report, "regular-expression");
        Assert.Equal("GsmNumber", finding.Path);
        Assert.Equal("4888888888", finding.Observed);
        Assert.Equal("+90 488 888 88 88", model.GsmNumber); // legacy mutated here
    }

    [Fact]
    public void Too_short_phone_is_a_normalization_failure()
    {
        var model = ReadmeExample();
        model.GsmNumber = "12345";
        var report = Eval.Run(Registry, model, "partial");

        var finding = Assert.Single(report.Findings, f => f.Path == "GsmNumber");
        Assert.Equal(FindingKind.NormalizationFailed, finding.Kind);
        Assert.Equal("normalize:keep-last-10", finding.Code);
        Assert.DoesNotContain(report.Changes, c => c.Member == "GsmNumber");
    }

    [Fact]
    public void Missing_end_date_gets_default_but_present_one_is_kept()
    {
        var missing = Eval.Run(Registry, ReadmeExample(), "partial");
        Assert.Equal(new DateTime(2020, 12, 31), Assert.Single(missing.Changes, c => c.Member == "EndDate").After);

        var model = ReadmeExample();
        model.EndDate = new DateTime(2030, 1, 1);
        var present = Eval.Run(Registry, model, "partial");
        Assert.DoesNotContain(present.Changes, c => c.Member == "EndDate"); // legacy overwrote it
    }

    [Fact]
    public void Scenario_skip_applies_only_in_that_scenario()
    {
        Assert.Contains(Eval.Run(Registry, ReadmeExample()).Findings, f => f.Path == "IgnoreAbleProperty");
        Assert.DoesNotContain(Eval.Run(Registry, ReadmeExample(), "partial").Findings, f => f.Path == "IgnoreAbleProperty");
    }

    public class ScenarioParent
    {
        [Descend] public ScenarioInner? Inner { get; set; }
    }

    public class ScenarioInner
    {
        [Required, SkipIn("partial")] public string? Optional { get; set; }
    }

    [Fact]
    public void Scenario_propagates_through_the_whole_graph()
    {
        var report = Eval.Run(Registry, new ScenarioParent { Inner = new ScenarioInner() }, "partial");
        Assert.True(report.IsConformant); // legacy lost the flag one level down
    }

    [Fact]
    public void Optional_child_is_still_traversed_when_present()
    {
        var model = ReadmeExample();
        model.Child!.NestedChild = new Child { Name = "x", Barcode = "short" };
        var report = Eval.Run(Registry, model, "partial");
        Assert.Contains(report.Findings, f => f.Path == "Child.NestedChild.Barcode");
    }

    [Fact]
    public void Nested_findings_carry_full_paths_and_all_are_reported()
    {
        var model = ReadmeExample();
        model.Name = null;
        model.CantBeNull![0].Barcode = "1";
        var report = Eval.Run(Registry, model, "partial");

        Assert.Contains(report.Findings, f => f.Path == "Name" && f.Code == "required");
        Assert.Contains(report.Findings, f => f.Path == "CantBeNull[0].Barcode" && f.Code == "string-length");
    }

    [Fact]
    public void Cycles_terminate()
    {
        var model = ReadmeExample();
        model.Child!.NestedChild = model.Child;
        var report = Eval.Run(Registry, model, "partial");
        Assert.Single(report.Findings); // just the appointment date
    }
}
