using ValidationHelper.Attributes;
using ValidationHelper.Models;
using Legacy = ValidationHelper.ValidationHelper;

namespace Conform.Characterization.Tests;

/// <summary>
/// Pins what the 2019 implementation actually does, compiled unmodified from the repository root.
/// Each test is labelled: INTENDED (useful semantics carried forward), ACCIDENTAL (side effect of structure),
/// or BUG (contradicts the code's own names or example). See docs/MIGRATION.md for how each is treated.
/// </summary>
public class LegacyBehaviorTests
{
    private static Master ReadmeExample() => new()
    {
        CantBeNull = [new Child { NestedChildren = [], Barcode = "12345678900", Name = "Murat Ay" }],
        Child = new Child { NestedChildren = [], Barcode = "12345678900", Name = "Murat Ay" },
        Name = "Murat Ay",
        AppointmentDate = DateTime.Today.AddDays(-1),
        CanbeNull = null,
        EndDate = null,
        GsmNumber = "+90 588 888 88 88",
        IgnoreAbleProperty = null,
    };

    [Fact] // BUG: the README's own showcase is invalid, because Child.NestedChild is required.
    public void Readme_example_fails_its_own_validation()
    {
        var result = Legacy.CheckModel(ReadmeExample(), true);

        Assert.False(result.IsValid);
        Assert.Equal("NestedChild null or empty", result.Error);
    }

    [Fact] // BUG: [SmartChildClass(false)] defaults notNull:true, so every Child needs another Child. Unsatisfiable.
    public void Child_model_is_unsatisfiable_for_any_finite_chain()
    {
        Child Leaf() => new() { Name = "a", Barcode = "1234567890", NestedChildren = [] };
        var chain = Leaf();
        for (var i = 0; i < 5; i++) chain = new Child { Name = "a", Barcode = "1234567890", NestedChildren = [], NestedChild = chain };

        var result = Legacy.CheckModel(chain);

        Assert.False(result.IsValid);
        Assert.Equal("NestedChild null or empty", result.Error);
    }

    [Fact] // INTENDED: normalize (digits, last 10), judge the canonical value, write it back.
    public void Gsm_number_is_normalized_and_written_back()
    {
        var model = ReadmeExample();
        Legacy.CheckModel(model, true);
        Assert.Equal("5888888888", model.GsmNumber);
    }

    [Fact] // BUG: the object is mutated before the checks that then reject it; regex failure reports "exceed length".
    public void Invalid_gsm_is_mutated_and_misreported()
    {
        var model = ReadmeExample();
        model.GsmNumber = "+90 488 888 88 88";

        var result = Legacy.CheckModel(model, true);

        Assert.Equal("GsmNumber exceed length", result.Error);
        Assert.Equal("4888888888", model.GsmNumber);
    }

    [Fact] // INTENDED (with domain leak): a value too short to canonicalize is a distinct failure.
    public void Too_short_gsm_is_a_normalization_failure_and_not_mutated()
    {
        var model = ReadmeExample();
        model.GsmNumber = "12345";

        var result = Legacy.CheckModel(model, true);

        Assert.Equal("GsmNumber invalid gsm number", result.Error);
        Assert.Equal("12345", model.GsmNumber);
    }

    [Fact] // BUG: "ChangeIfDefault" overwrites unconditionally.
    public void ChangeIfDefault_overwrites_non_default_values()
    {
        var model = ReadmeExample();
        model.EndDate = new DateTime(2030, 1, 1);
        Legacy.CheckModel(model, true);
        Assert.Equal(new DateTime(2020, 12, 31), model.EndDate);
    }

    [Fact] // INTENDED: absent date gets a default value.
    public void ChangeIfDefault_fills_missing_value()
    {
        var model = ReadmeExample();
        Legacy.CheckModel(model, true);
        Assert.Equal(new DateTime(2020, 12, 31), model.EndDate);
    }

    [Fact] // BUG: GreaterOrEqualThanTomorrow only runs inside the NotNullOrDefault branch; here it never runs.
    public void Past_appointment_date_is_accepted_because_date_rule_is_gated()
    {
        var model = new Master { Name = "x", GsmNumber = "5321234567", AppointmentDate = DateTime.Today.AddYears(-10) };
        var result = Legacy.CheckModel(model, true);

        // Dates are checked before children; failing on the first child proves the date passed.
        Assert.Equal("CantBeNull null or empty", result.Error);
    }

    [Fact] // INTENDED: the flag is a scenario switch.
    public void IgnoreWhenPrompted_skips_member_only_when_caller_asks()
    {
        Assert.Equal("IgnoreAbleProperty null or empty", Legacy.CheckModel(ReadmeExample(), false).Error);
        Assert.NotEqual("IgnoreAbleProperty null or empty", Legacy.CheckModel(ReadmeExample(), true).Error);
    }

    [Fact] // BUG: the scenario flag is not passed into recursion.
    public void IgnoreWhenPrompted_is_lost_one_level_down()
    {
        var model = new ScenarioParent { Inner = new ScenarioInner { Optional = null } };
        var result = Legacy.CheckModel(model, ignoreWhenPrompted: true);
        Assert.Equal("Optional null or empty", result.Error);
    }

    [Fact] // BUG: notNull:false disables traversal entirely, so a present, invalid child passes.
    public void Optional_child_is_never_traversed()
    {
        var model = new OptionalChildParent { Child = new ScenarioInner { Optional = null } };
        Assert.True(Legacy.CheckModel(model).IsValid);
    }

    [Fact] // ACCIDENTAL: only non-generic ICollection is recognized; HashSet<T> silently passes.
    public void HashSet_collections_are_neither_counted_nor_traversed()
    {
        var model = new HashSetParent { Items = [] };
        Assert.True(Legacy.CheckModel(model).IsValid);
    }

    [Fact] // ACCIDENTAL: with notEmptyOrNullOrWhiteSpace:false, length/regex rules never run.
    public void String_rules_are_gated_by_presence_flag()
    {
        var model = new GatedString { Code = "way too long" };
        Assert.True(Legacy.CheckModel(model).IsValid);
    }

    [Fact] // ACCIDENTAL: AttributeTargets.Field is declared but fields are never scanned.
    public void Attributes_on_fields_are_ignored()
    {
        Assert.True(Legacy.CheckModel(new FieldModel { Name = null }).IsValid);
    }

    [Fact] // INTENDED-ish: a null model is an error, not an exception.
    public void Null_model_is_reported()
    {
        var result = Legacy.CheckModel<Master>(null!);
        Assert.False(result.IsValid);
        Assert.Equal("Model null", result.Error);
    }

    [Fact] // ACCIDENTAL: first failure wins, no path; nested failure is indistinguishable from root failure.
    public void Only_first_error_without_path()
    {
        var model = ReadmeExample();
        model.Name = null;
        model.CantBeNull![0].Barcode = "1";
        var result = Legacy.CheckModel(model, true);
        Assert.Equal("Name null or empty", result.Error);
    }

    [Fact(Skip = "Legacy overflows the stack on cycles, which kills the test host. Verified out-of-process; see ARCHAEOLOGY §2.2.")]
    public void Cyclic_graph_overflows_stack()
    {
        var c = new Child { Name = "a", Barcode = "1234567890", NestedChildren = [] };
        c.NestedChild = c;
        Legacy.CheckModel(c);
    }

    public class ScenarioParent
    {
        [SmartChildClass(false)] public ScenarioInner? Inner { get; set; }
    }

    public class ScenarioInner
    {
        [SmartString(ignoreWhenPrompted: true)] public string? Optional { get; set; }
    }

    public class OptionalChildParent
    {
        [SmartChildClass(false, notNull: false)] public ScenarioInner? Child { get; set; }
    }

    public class HashSetParent
    {
        [SmartChildClass(true, minCollectionCount: 5)] public HashSet<ScenarioInner>? Items { get; set; }
    }

    public class GatedString
    {
        [SmartString(notEmptyOrNullOrWhiteSpace: false, maxLength: 3)] public string? Code { get; set; }
    }

    public class FieldModel
    {
        [SmartString] public string? Name;
    }
}
