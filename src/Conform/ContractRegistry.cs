using System.Collections.Concurrent;

namespace Conform;

/// <summary>
/// Resolves the contract for a runtime type. Explicit definitions win; then sources are asked in order; then base
/// types' explicit definitions. Each type is resolved once and cached.
/// </summary>
/// <remarks>Configure the registry first, then share it. Resolution is thread-safe; configuration is not.</remarks>
public sealed class ContractRegistry
{
    private readonly Dictionary<Type, TypeContract> _defined = [];
    private readonly List<IContractSource> _sources = [];
    private readonly ConcurrentDictionary<Type, TypeContract?> _cache = new();

    /// <summary>Defines the contract of <typeparamref name="T"/> in code. Replaces any previous definition.</summary>
    public ContractRegistry Define<T>(Action<ContractBuilder<T>> define) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(define);
        var builder = new ContractBuilder<T>();
        define(builder);
        return Add(builder.Build());
    }

    /// <summary>Adds a contract built elsewhere.</summary>
    public ContractRegistry Add(TypeContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        _defined[contract.Type] = contract;
        _cache.Clear();
        return this;
    }

    /// <summary>Adds a fallback source for types without an explicit definition.</summary>
    public ContractRegistry AddSource(IContractSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sources.Add(source);
        _cache.Clear();
        return this;
    }

    /// <summary>Contracts defined explicitly (not those discovered through sources).</summary>
    public IReadOnlyCollection<TypeContract> Defined => _defined.Values;

    /// <summary>The contract for <paramref name="type"/>, or <c>null</c> when there is none.</summary>
    /// <exception cref="ContractDefinitionException">A source produced a malformed contract.</exception>
    public TypeContract? Resolve(Type type) => _cache.GetOrAdd(type, ResolveUncached);

    private TypeContract? ResolveUncached(Type type)
    {
        if (_defined.TryGetValue(type, out var contract)) return contract;

        foreach (var source in _sources)
        {
            if (source.TryCreate(type) is { } found) return found;
        }

        for (var t = type.BaseType; t is not null && t != typeof(object); t = t.BaseType)
        {
            if (_defined.TryGetValue(t, out contract)) return contract;
        }

        return null;
    }
}
