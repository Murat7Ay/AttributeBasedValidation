namespace Conform;

/// <summary>
/// Maps a value to its canonical form of the same type, or fails.
/// Normalizers never write to objects; the engine turns their output into proposed changes (or, for lenses, only
/// into the value rules observe).
/// </summary>
public abstract class Normalizer
{
    protected Normalizer(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>Stable name, reported in <see cref="ProposedChange.Steps"/> and normalization failures.</summary>
    public string Name { get; }

    public abstract Type ValueType { get; }

    public virtual bool AppliesTo(Type valueType) => TypeCompat.Compatible(ValueType, valueType);

    /// <summary>Normalizes a present value.</summary>
    public abstract NormalizeResult<object?> NormalizeValue(object value);

    /// <summary>
    /// Offers a replacement for an absent value (null, or a whitespace-only string).
    /// Returns <c>null</c> to leave the value absent; most normalizers do.
    /// </summary>
    public virtual NormalizeResult<object?>? NormalizeAbsent() => null;

    public override string ToString() => Name;
}

/// <summary>Base class for strongly typed normalizers.</summary>
public abstract class Normalizer<T> : Normalizer
{
    protected Normalizer(string name) : base(name) { }

    public sealed override Type ValueType => typeof(T);

    public sealed override NormalizeResult<object?> NormalizeValue(object value)
    {
        if (value is not T typed)
            throw new InvalidOperationException(
                $"Normalizer '{Name}' expects {typeof(T).Name} but received {value.GetType().Name}.");

        var result = Normalize(typed);
        return result.Succeeded
            ? NormalizeResult<object?>.Success(result.Value)
            : NormalizeResult<object?>.Failure(result.Error!);
    }

    public sealed override NormalizeResult<object?>? NormalizeAbsent()
    {
        if (NormalizeMissing() is not { } result) return null;
        return result.Succeeded
            ? NormalizeResult<object?>.Success(result.Value)
            : NormalizeResult<object?>.Failure(result.Error!);
    }

    protected abstract NormalizeResult<T> Normalize(T value);

    /// <summary>Override to supply a value when the member is absent (defaulting). Returns <c>null</c> by default.</summary>
    protected virtual NormalizeResult<T>? NormalizeMissing() => null;
}

/// <summary>Result of a normalization step: the canonical value, or the reason there is none.</summary>
public readonly struct NormalizeResult<T>
{
    private NormalizeResult(bool succeeded, T value, string? error)
    {
        Succeeded = succeeded;
        Value = value;
        Error = error;
    }

    public bool Succeeded { get; }
    public T Value { get; }
    public string? Error { get; }

    public static NormalizeResult<T> Success(T value) => new(true, value, null);

    public static NormalizeResult<T> Failure(string error) => new(false, default!, error);

    public static implicit operator NormalizeResult<T>(T value) => Success(value);
}
