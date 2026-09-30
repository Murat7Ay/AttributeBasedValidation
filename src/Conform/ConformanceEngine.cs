using System.Collections;

namespace Conform;

/// <summary>
/// Interprets contracts over an object graph. Evaluation never writes to the graph: it returns findings and the
/// changes that normalization would make. See <see cref="ConformanceReport.Apply"/> to commit them.
/// </summary>
public sealed class ConformanceEngine(ContractRegistry registry)
{
    private readonly ContractRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public ContractRegistry Registry => _registry;

    /// <summary>Evaluates <paramref name="root"/> and everything reachable from it through declared edges.</summary>
    /// <exception cref="InvalidOperationException">The root's type has no contract.</exception>
    /// <exception cref="ContractDefinitionException">A contract discovered during traversal is malformed.</exception>
    public ConformanceReport Evaluate<T>(T? root, EvaluationOptions? options = null)
    {
        options ??= EvaluationOptions.Default;
        var state = new EvaluationState(options.Clock, new HashSet<string>(options.Scenarios, StringComparer.Ordinal));

        if (root is null)
        {
            var missing = new Finding("", typeof(T), null, "required", Severity.Error, FindingKind.Violation,
                "a value is required", null, null);
            return new ConformanceReport([missing], [], 0);
        }

        if (_registry.Resolve(root.GetType()) is null)
            throw new InvalidOperationException(
                $"No contract for {root.GetType().Name}. Define one or add a contract source (e.g. UseAnnotations()).");

        return new Run(_registry, state).Execute(root);
    }

    /// <summary>One evaluation: explicit-stack, depth-first, declaration-ordered, each instance visited once.</summary>
    private sealed class Run(ContractRegistry registry, EvaluationState state)
    {
        private readonly List<Finding> _findings = [];
        private readonly List<ProposedChange> _changes = [];
        private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);
        private readonly Stack<Pending> _stack = new();
        private readonly List<Pending> _children = [];
        private int _objects;

        public ConformanceReport Execute(object root)
        {
            _stack.Push(new Pending(root, null, AsElements: false, AllowNullElements: false));

            while (_stack.TryPop(out var item))
            {
                if (!item.Value.GetType().IsValueType && !_visited.Add(item.Value)) continue;

                _children.Clear();
                if (item.AsElements) ExpandElements(item);
                else EvaluateObject(item.Value, item.Path);

                // Push in reverse so children are processed in declaration / index order.
                for (var i = _children.Count - 1; i >= 0; i--) _stack.Push(_children[i]);
            }

            return new ConformanceReport(_findings.ToArray(), _changes.ToArray(), _objects);
        }

        private void EvaluateObject(object obj, PathNode? path)
        {
            var contract = registry.Resolve(obj.GetType());
            if (contract is null) return;
            _objects++;

            foreach (var member in contract.Members) EvaluateMember(obj, member, path);

            foreach (var rule in contract.ObjectRules)
            {
                var ctx = new RuleContext(obj, null, path, state);
                RunRule(rule, obj, obj, ctx, path, null);
            }
        }

        private void EvaluateMember(object owner, MemberContract member, PathNode? ownerPath)
        {
            if (member.SkipInScenarios.Count > 0 && state.Scenarios.Count > 0 && member.SkipInScenarios.Any(state.Scenarios.Contains))
                return;

            var ctx = new RuleContext(owner, member.Name, ownerPath, state);

            if (member.Condition is { } condition)
            {
                bool active;
                try { active = condition(ctx); }
                catch (Exception ex) { Fault(owner, ownerPath, member.Name, "condition-fault", ex, null); return; }
                if (!active) return;
            }

            object? raw;
            try { raw = member.Getter(owner); }
            catch (Exception ex) { Fault(owner, ownerPath, member.Name, "access-fault", ex, null); return; }

            // 1. Canonicalize. The result is what rules judge and what a change would write.
            if (!RunSteps(member.Normalizers, raw, owner, ownerPath, member.Name, out var canonical, out var steps)) return;

            if (!Equals(raw, canonical))
            {
                _changes.Add(new ProposedChange(PathNode.Render(ownerPath, member.Name), owner.GetType(), member.Name,
                    raw, canonical, (IReadOnlyList<string>?)steps ?? [], owner, member));
            }

            // 2. Lenses shape only what rules observe.
            if (!RunSteps(member.Lenses, canonical, owner, ownerPath, member.Name, out var observed, out _)) return;

            // 3. Presence, decided once. Rules never see absent values.
            if (IsAbsent(observed))
            {
                if (member.IsRequired)
                {
                    Add(owner.GetType(), ownerPath, member.Name, "required", Severity.Error, FindingKind.Violation,
                        "a value is required", "a present value", observed);
                }
                return;
            }

            // 4. Constraints over the observed value.
            foreach (var rule in member.Rules) RunRule(rule, observed!, owner, ctx, ownerPath, member.Name);

            // 5. Edges. Traverse the canonical value.
            if (member.Descend && canonical is not null)
            {
                var childPath = PathNode.ForMember(ownerPath, member.Name);
                _children.Add(new Pending(canonical, childPath, IsElements(canonical), member.AllowNullElements));
            }
        }

        private bool RunSteps(IReadOnlyList<Normalizer> normalizers, object? input, object owner, PathNode? ownerPath,
            string memberName, out object? output, out List<string>? applied)
        {
            output = input;
            applied = null;
            foreach (var n in normalizers)
            {
                NormalizeResult<object?>? result;
                try { result = IsAbsent(output) ? n.NormalizeAbsent() : n.NormalizeValue(output!); }
                catch (Exception ex)
                {
                    Fault(owner, ownerPath, memberName, "normalizer-fault", ex, output);
                    return false;
                }

                if (result is not { } r) continue;
                if (!r.Succeeded)
                {
                    Add(owner.GetType(), ownerPath, memberName, $"normalize:{n.Name}", Severity.Error,
                        FindingKind.NormalizationFailed, r.Error ?? "could not be normalized", null, output);
                    return false;
                }

                if (!Equals(output, r.Value)) (applied ??= []).Add(n.Name);
                output = r.Value;
            }

            return true;
        }

        private void RunRule(Rule rule, object value, object owner, in RuleContext ctx, PathNode? ownerPath, string? member)
        {
            Violation? violation;
            try { violation = rule.CheckValue(value, ctx); }
            catch (Exception ex)
            {
                Fault(owner, ownerPath, member, rule.Code, ex, value);
                return;
            }

            if (violation is null) return;
            var expected = violation.Expected ?? (rule.Description == violation.Message ? null : rule.Description);
            Add(owner.GetType(), ownerPath, member, rule.Code, rule.Severity, FindingKind.Violation,
                violation.Message, expected, member is null ? null : value);
        }

        private void ExpandElements(Pending item)
        {
            var index = 0;
            try
            {
                foreach (var element in (IEnumerable)item.Value)
                {
                    var path = PathNode.ForIndex(item.Path, index++);
                    if (element is null)
                    {
                        if (!item.AllowNullElements)
                        {
                            _findings.Add(new Finding(path.ToString(), item.Value.GetType(), null, "null-element",
                                Severity.Error, FindingKind.Violation, "elements must not be null", "a non-null element", null));
                        }
                        continue;
                    }

                    _children.Add(new Pending(element, path, IsElements(element), item.AllowNullElements));
                }
            }
            catch (Exception ex)
            {
                _findings.Add(new Finding(PathNode.Render(item.Path), item.Value.GetType(), null, "enumeration-fault",
                    Severity.Error, FindingKind.Fault, $"enumeration failed: {ex.Message}", null, null));
            }
        }

        private void Fault(object owner, PathNode? ownerPath, string? member, string code, Exception ex, object? observed) =>
            Add(owner.GetType(), ownerPath, member, code, Severity.Error, FindingKind.Fault,
                $"{ex.GetType().Name}: {ex.Message}", null, observed);

        private void Add(Type objectType, PathNode? ownerPath, string? member, string code, Severity severity,
            FindingKind kind, string message, string? expected, object? observed) =>
            _findings.Add(new Finding(PathNode.Render(ownerPath, member), objectType, member, code, severity, kind,
                message, expected, observed));
    }

    internal static bool IsAbsent(object? value) => value is null || (value is string s && string.IsNullOrWhiteSpace(s));

    private static bool IsElements(object value) => value is IEnumerable and not string;

    private readonly record struct Pending(object Value, PathNode? Path, bool AsElements, bool AllowNullElements);
}
