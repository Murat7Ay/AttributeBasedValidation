using System.ComponentModel.DataAnnotations;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Conform;
using Conform.Annotations;

BenchmarkSwitcher.FromAssembly(typeof(GraphBenchmarks).Assembly).Run(args);

/// <summary>
/// A conformant graph (so evaluation walks all of it):
/// 1 order, <see cref="Lines"/> lines, each with 2 child lines = 1 + 3 * Lines objects.
/// Both contract styles check the same things: required name, phone digits/suffix/regex, barcode length,
/// recursion into lines and children.
/// </summary>
[MemoryDiagnoser]
public class GraphBenchmarks
{
    [Params(10, 200)]
    public int Lines;

    private CodeOrder _code = null!;
    private AnnotatedOrder _annotated = null!;
    private ConformanceEngine _codeEngine = null!, _annotationEngine = null!;

    [GlobalSetup]
    public void Setup()
    {
        _code = new CodeOrder
        {
            Name = "order", Phone = "+90 532 123 45 67",
            Lines = Enumerable.Range(0, Lines).Select(i => new CodeLine
            {
                Name = "l" + i, Barcode = "1234567890" + i % 10, Children = [NewCodeLeaf(), NewCodeLeaf()],
            }).ToList(),
        };
        _annotated = new AnnotatedOrder
        {
            Name = "order", Phone = "+90 532 123 45 67",
            Lines = Enumerable.Range(0, Lines).Select(i => new AnnotatedLine
            {
                Name = "l" + i, Barcode = "1234567890" + i % 10, Children = [NewAnnotatedLeaf(), NewAnnotatedLeaf()],
            }).ToList(),
        };

        _codeEngine = new ConformanceEngine(CodeRegistry());
        _annotationEngine = new ConformanceEngine(new ContractRegistry().UseAnnotations());

        if (!_codeEngine.Evaluate(_code).IsConformant || !_annotationEngine.Evaluate(_annotated).IsConformant)
            throw new InvalidOperationException("Benchmark graphs must be valid so every run walks them fully.");
    }

    [Benchmark(Baseline = true)]
    public bool Conform_CodeContracts() => _codeEngine.Evaluate(_code).IsConformant;

    [Benchmark]
    public bool Conform_Annotations() => _annotationEngine.Evaluate(_annotated).IsConformant;

    /// <summary>Includes building contracts from scratch: the cost a cold start pays once.</summary>
    [Benchmark]
    public bool Conform_Annotations_ColdRegistry() =>
        new ConformanceEngine(new ContractRegistry().UseAnnotations()).Evaluate(_annotated).IsConformant;

    private static ContractRegistry CodeRegistry() => new ContractRegistry()
        .Define<CodeOrder>(c =>
        {
            c.Member(o => o.Name).Required();
            c.Member(o => o.Phone).Required().Lens(Normalizers.DigitsOnly, Normalizers.KeepLast(10)).Length(10, 15).Matches(@"5\d{9}\s*?$");
            c.Member(o => o.Lines).Required().Descend();
        })
        .Define<CodeLine>(c =>
        {
            c.Member(l => l.Name).Required();
            c.Member(l => l.Barcode).Required().Length(10, 15);
            c.Member(l => l.Children).Required().Descend();
        });

    private static CodeLine NewCodeLeaf() => new() { Name = "leaf", Barcode = "1234567890", Children = [] };
    private static AnnotatedLine NewAnnotatedLeaf() => new() { Name = "leaf", Barcode = "1234567890", Children = [] };
}

public class CodeOrder
{
    public string? Name { get; set; }
    public string? Phone { get; set; }
    public List<CodeLine>? Lines { get; set; }
}

public class CodeLine
{
    public string? Name { get; set; }
    public string? Barcode { get; set; }
    public List<CodeLine>? Children { get; set; }
}

public class AnnotatedOrder
{
    [Required] public string? Name { get; set; }
    [Required, DigitsOnly(Lens = true, Order = 1), KeepLast(10, Lens = true, Order = 2), StringLength(15, MinimumLength = 10), RegularExpression(@"5\d{9}")]
    public string? Phone { get; set; }
    [Required, Descend] public List<AnnotatedLine>? Lines { get; set; }
}

public class AnnotatedLine
{
    [Required] public string? Name { get; set; }
    [Required, StringLength(15, MinimumLength = 10)] public string? Barcode { get; set; }
    [Required, Descend] public List<AnnotatedLine>? Children { get; set; }
}
