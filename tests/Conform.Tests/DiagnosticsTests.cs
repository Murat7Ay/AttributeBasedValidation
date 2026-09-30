namespace Conform.Tests;

public class DiagnosticsTests
{
    private static readonly ContractRegistry Registry = new ContractRegistry()
        .Define<Order>(c =>
        {
            c.Member(o => o.Name).Required();
            c.Member(o => o.Lines).Descend();
        })
        .Define<Line>(c => c.Member(l => l.Barcode).Normalize(Normalizers.Trim).Length(10, 15));

    [Fact]
    public void Finding_identifies_location_rule_expectation_and_observation()
    {
        var report = Eval.Run(Registry, new Order { Name = "x", Lines = [new Line(), new Line { Barcode = " 123 " }] });

        var f = Assert.Single(report.Findings);
        Assert.Equal("Lines[1].Barcode", f.Path);
        Assert.Equal(typeof(Line), f.ObjectType);
        Assert.Equal("Barcode", f.Member);
        Assert.Equal("length", f.Code);
        Assert.Equal(Severity.Error, f.Severity);
        Assert.Equal(FindingKind.Violation, f.Kind);
        Assert.Equal("between 10 and 15 characters", f.Expected);
        Assert.Equal("123", f.Observed); // canonical, not raw
        Assert.Contains("has 3", f.Message);

        var change = Assert.Single(report.Changes); // the finding and the fix are both visible
        Assert.Equal(f.Path, change.Path);
    }

    [Fact]
    public void Error_dictionary_matches_model_state_shape()
    {
        var report = Eval.Run(Registry, new Order { Lines = [new Line { Barcode = "1" }] });

        var errors = report.ToErrorDictionary();

        Assert.Equal(["Lines[0].Barcode", "Name"], errors.Keys.Order());
        Assert.Equal(["a value is required"], errors["Name"]);
    }

    [Fact]
    public void Root_member_paths_have_no_prefix()
    {
        var f = Assert.Single(Eval.Run(Registry, new Order()).Findings);
        Assert.Equal("Name", f.Path);
    }

    [Fact]
    public void Report_text_is_readable()
    {
        var text = Eval.Run(Registry, new Order { Lines = [new Line { Barcode = " 1 " }] }).ToString();

        Assert.Contains("Not conformant", text);
        Assert.Contains("Error   Name [required]", text);
        Assert.Contains("Lines[0].Barcode [length]", text);
        Assert.Contains("Change  Lines[0].Barcode: \" 1 \" -> \"1\" via trim", text);
    }
}
