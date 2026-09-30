namespace Conform.Tests;

public class GraphTests
{
    private static ContractRegistry Registry(bool allowNullElements = false) => new ContractRegistry()
        .Define<Order>(c =>
        {
            c.Member(o => o.Lines).Descend(allowNullElements);
            c.Member(o => o.Main).Descend();
            c.Member(o => o.Set).Descend();
            c.Member(o => o.Grid).Descend();
        })
        .Define<Line>(c =>
        {
            c.Member(l => l.Name).Required();
            c.Member(l => l.Next).Descend();
            c.Member(l => l.Children).Descend();
        });

    private static Line Ok(string name = "ok") => new() { Name = name };

    [Fact]
    public void Nested_objects_and_collections_produce_indexed_paths()
    {
        var order = new Order
        {
            Main = new Line { Name = "m", Next = new Line { Children = [Ok(), new Line()] } },
            Lines = [Ok(), new Line()],
        };

        var paths = Eval.Run(Registry(), order).Findings.Select(f => f.Path).ToArray();

        Assert.Equal(["Lines[1].Name", "Main.Next.Name", "Main.Next.Children[1].Name"], paths);
    }

    [Fact]
    public void Nested_collections_flatten_with_nested_indices()
    {
        var order = new Order { Grid = [[Ok()], [Ok(), new Line()], []] };
        var finding = Assert.Single(Eval.Run(Registry(), order).Findings);
        Assert.Equal("Grid[1][1].Name", finding.Path);
    }

    [Fact]
    public void Empty_and_null_collections_are_fine_unless_required()
    {
        Assert.True(Eval.Run(Registry(), new Order { Lines = [] }).IsConformant);
        Assert.True(Eval.Run(Registry(), new Order { Lines = null }).IsConformant);

        var required = new ContractRegistry().Define<Order>(c => c.Member(o => o.Lines).Required().Descend());
        Assert.Equal("required", Assert.Single(Eval.Run(required, new Order()).Findings).Code);
    }

    [Fact]
    public void Null_elements_are_violations_unless_allowed()
    {
        var order = new Order { Lines = [Ok(), null!, Ok()] };

        var finding = Assert.Single(Eval.Run(Registry(), order).Findings);
        Assert.Equal(("Lines[1]", "null-element"), (finding.Path, finding.Code));

        Assert.True(Eval.Run(Registry(allowNullElements: true), order).IsConformant);
    }

    [Fact]
    public void Sets_are_traversed()
    {
        var finding = Assert.Single(Eval.Run(Registry(), new Order { Set = [new Line()] }).Findings);
        Assert.Equal("Set[0].Name", finding.Path);
    }

    [Fact]
    public void Repeated_references_are_evaluated_once_at_first_path()
    {
        var shared = new Line();
        var order = new Order { Main = shared, Lines = [shared, shared] };

        var report = Eval.Run(Registry(), order);

        Assert.Equal("Lines[0].Name", Assert.Single(report.Findings).Path);
        Assert.Equal(2, report.ObjectsVisited); // order + shared line
    }

    [Fact]
    public void Cycles_terminate_including_self_references()
    {
        var a = new Line { Name = "a" };
        var b = new Line { Next = a };
        a.Next = b;
        a.Children = [a, b];

        var report = Eval.Run(Registry(), new Order { Main = a });

        Assert.Equal("Main.Next.Name", Assert.Single(report.Findings).Path);
    }

    [Fact]
    public void Collections_that_contain_themselves_terminate()
    {
        var registry = new ContractRegistry().Define<Holder>(c => c.Member(h => h.Items).Descend());
        var list = new List<object>();
        list.Add(list);
        list.Add(new Holder());

        var report = Eval.Run(registry, new Holder { Items = list });

        Assert.True(report.IsConformant);
        Assert.Equal(2, report.ObjectsVisited);
    }

    [Fact]
    public void Deep_graphs_do_not_overflow_the_stack()
    {
        var head = new Line { Name = "0" };
        var current = head;
        for (var i = 1; i < 200_000; i++) current = current.Next = new Line { Name = i.ToString() };
        current.Name = null;

        var report = Eval.Run(Registry(), new Order { Main = head });

        Assert.Equal(200_001, report.ObjectsVisited);
        Assert.Single(report.Findings);
    }

    [Fact]
    public void Derived_instances_use_the_most_derived_registered_contract()
    {
        var registry = Registry();
        var order = new Order { Main = new SpecialLine() }; // only Line is registered
        Assert.Equal("Main.Name", Assert.Single(Eval.Run(registry, order).Findings).Path);

        registry.Define<SpecialLine>(c => c.Member(s => s.Extra).Required());
        Assert.Equal("Main.Extra", Assert.Single(Eval.Run(registry, order).Findings).Path);
    }

    [Fact]
    public void Inherited_members_can_be_declared_on_derived_contracts()
    {
        var registry = new ContractRegistry().Define<SpecialLine>(c => c.Member(s => s.Name).Normalize(Normalizers.Trim));
        var line = new SpecialLine { Name = " x " };

        Eval.Run(registry, line).Apply();

        Assert.Equal("x", line.Name);
    }

    [Fact]
    public void Undeclared_members_are_neither_evaluated_nor_traversed()
    {
        var registry = new ContractRegistry()
            .Define<Order>(c => c.Member(o => o.Name).Required())
            .Define<Line>(c => c.Member(l => l.Name).Required());

        var report = Eval.Run(registry, new Order { Name = "x", Main = new Line(), Lines = [new Line()] });

        Assert.True(report.IsConformant);
        Assert.Equal(1, report.ObjectsVisited);
    }

    [Fact]
    public void Throwing_getters_become_faults_and_evaluation_continues()
    {
        var registry = new ContractRegistry().Define<Exploding>(c =>
        {
            c.Member(e => e.Boom).Required();
            c.Member(e => e.Fine).Required();
        });

        var report = Eval.Run(registry, new Exploding());

        var fault = Assert.Single(report.Findings, f => f.Kind == FindingKind.Fault);
        Assert.Equal(("Boom", "access-fault"), (fault.Path, fault.Code));
        Assert.Contains("kaboom", fault.Message);
        Assert.Contains(report.Findings, f => f.Path == "Fine" && f.Code == "required");
        Assert.False(report.IsConformant);
    }

    [Fact]
    public void Throwing_enumerations_become_faults()
    {
        var registry = new ContractRegistry().Define<Holder>(c => c.Member(h => h.Items).Descend());
        var report = Eval.Run(registry, new Holder { Items = Broken() });
        Assert.Equal("enumeration-fault", Assert.Single(report.Findings).Code);

        static IEnumerable<object> Broken()
        {
            yield return new Holder();
            throw new InvalidOperationException("db gone");
        }
    }

    [Fact]
    public void Value_type_elements_without_contracts_are_ignored()
    {
        var registry = new ContractRegistry().Define<Holder>(c => c.Member(h => h.Items).Descend());
        Assert.True(Eval.Run(registry, new Holder { Items = new object[] { 1, 2, "three" } }).IsConformant);
    }

    public class Holder
    {
        public IEnumerable<object>? Items { get; set; }
    }

    public class Exploding
    {
        public string Boom => throw new InvalidOperationException("kaboom");
        public string? Fine { get; set; }
    }
}
