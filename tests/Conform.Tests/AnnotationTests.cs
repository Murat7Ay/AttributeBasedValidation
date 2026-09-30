using System.ComponentModel.DataAnnotations;
using Conform.Annotations;

namespace Conform.Tests;

public class AnnotationTests
{
    private static readonly ContractRegistry Registry = new ContractRegistry().UseAnnotations();

    public class Signup
    {
        [Required, Trim, EmailAddress] public string? Email { get; set; }
        [Required, Trim(Order = 1), StringLength(20, MinimumLength = 3)] public string? UserName { get; set; }
        [Range(18, 130)] public int Age { get; set; }
        [Compare(nameof(Password))] public string? Confirm { get; set; }
        public string? Password { get; set; }
        [MinLength(1), Descend] public List<Address>? Addresses { get; set; }
        [DigitsOnly(Lens = true), StringLength(10, MinimumLength = 10)] public string? Phone { get; set; }
        [NotDefault] public DateTime Birthday { get; set; }
        [Required] public string? Nickname; // fields are scanned
    }

    public class Address
    {
        [Required] public string? City { get; set; }
    }

    private static Signup Valid() => new()
    {
        Email = " ada@example.com ",
        UserName = "ada",
        Age = 36,
        Password = "p",
        Confirm = "p",
        Addresses = [new Address { City = "Izmir" }],
        Phone = "532 123 45 67",
        Birthday = new DateTime(1990, 1, 1),
        Nickname = "a",
    };

    [Fact]
    public void Valid_model_conforms_and_proposes_trim()
    {
        var report = Eval.Run(Registry, Valid());
        Assert.True(report.IsConformant, report.ToString());
        Assert.Equal("ada@example.com", Assert.Single(report.Changes).After);
    }

    [Fact]
    public void BCL_attributes_are_judged_against_canonical_values_with_BCL_messages()
    {
        var model = Valid();
        model.Email = "  not-an-email ";
        model.UserName = "  ab  "; // trimmed to 2 chars
        model.Age = 7;
        model.Confirm = "q";
        model.Addresses = [];
        model.Phone = "12-34";
        model.Birthday = default;
        model.Nickname = null;

        var report = Eval.Run(Registry, model);

        Assert.Equal(
            ["email-address", "string-length", "range", "compare", "min-length", "string-length", "not-default", "required"],
            report.Findings.Select(f => f.Code));
        Assert.Equal("ab", report.Findings[1].Observed);
        Assert.Contains("UserName", report.Findings[1].Message);
        Assert.Equal("1234", report.Findings[5].Observed); // lens value
        Assert.DoesNotContain(report.Changes, c => c.Member == "Phone");
    }

    [Fact]
    public void Descend_reaches_nested_annotated_types()
    {
        var model = Valid();
        model.Addresses = [new Address(), new Address { City = "x" }];
        Assert.Equal("Addresses[0].City", Assert.Single(Eval.Run(Registry, model).Findings).Path);
    }

    public class Ambiguous
    {
        [DigitsOnly, KeepLast(3)] public string? Code { get; set; }
    }

    [Fact]
    public void Multiple_normalizers_without_explicit_order_are_rejected()
    {
        var ex = Assert.Throws<ContractDefinitionException>(() => Registry.Resolve(typeof(Ambiguous)));
        Assert.Contains("distinct Order", ex.Message);
    }

    public class BadDefault
    {
        [DefaultIfMissing("not a date")] public DateTime? When { get; set; }
    }

    [Fact]
    public void Unparseable_default_is_a_definition_error()
    {
        Assert.Throws<ContractDefinitionException>(() => Registry.Resolve(typeof(BadDefault)));
    }

    public class Misplaced
    {
        [KeepLast(2)] public int Number { get; set; }
    }

    [Fact]
    public void Normalizer_on_wrong_type_is_a_definition_error()
    {
        Assert.Throws<ContractDefinitionException>(() => Registry.Resolve(typeof(Misplaced)));
    }

    public class Plain
    {
        public string? Name { get; set; }
    }

    [Fact]
    public void Types_without_relevant_attributes_have_no_contract()
    {
        Assert.Null(Registry.Resolve(typeof(Plain)));
        Assert.Throws<InvalidOperationException>(() => new ConformanceEngine(Registry).Evaluate(new Plain()));
    }

    [CustomValidation(typeof(Range2), nameof(Range2.Check))]
    public class Range2
    {
        public int From { get; set; }
        public int To { get; set; }

        public static ValidationResult? Check(Range2 r) =>
            r.From <= r.To ? ValidationResult.Success : new ValidationResult("From must not exceed To");
    }

    [Fact]
    public void Class_level_validation_attributes_become_object_rules()
    {
        var f = Assert.Single(Eval.Run(Registry, new Range2 { From = 2, To = 1 }).Findings);
        Assert.Equal(("", null, "custom-validation", "From must not exceed To"), (f.Path, f.Member, f.Code, f.Message));
    }

    public class Base
    {
        [Required] public virtual string? Name { get; set; }
    }

    public class Derived : Base
    {
        [Required] public string? Extra { get; set; }
    }

    [Fact]
    public void Inherited_attributes_are_honoured()
    {
        var report = Eval.Run(Registry, new Derived());
        Assert.Equal(["Extra", "Name"], report.Findings.Select(f => f.Path).Order());
    }

    public class Booking
    {
        [NotBeforeToday(1, Severity = Severity.Warning)] public DateOnly Day { get; set; }
        [DefaultIfMissing("2020-12-31")] public DateOnly? End { get; set; }
    }

    [Fact]
    public void Rule_attribute_severity_and_dateonly_support()
    {
        var report = Eval.Run(Registry, new Booking { Day = DateOnly.FromDateTime(FixedClock.Today) });
        Assert.True(report.IsConformant);
        Assert.Equal(Severity.Warning, Assert.Single(report.Findings).Severity);
        Assert.Equal(new DateOnly(2020, 12, 31), Assert.Single(report.Changes).After);
    }
}
