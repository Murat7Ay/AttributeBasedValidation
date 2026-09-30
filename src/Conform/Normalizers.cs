namespace Conform;

/// <summary>Built-in normalizers and factories for custom ones.</summary>
public static class Normalizers
{
    /// <summary>Removes leading and trailing whitespace.</summary>
    public static Normalizer Trim { get; } = From<string>("trim", s => s.Trim());

    /// <summary>Keeps only decimal digits: <c>"+90 (588) 888-88-88"</c> becomes <c>"905888888888"</c>.</summary>
    public static Normalizer DigitsOnly { get; } = From<string>("digits-only", s => string.Concat(s.Where(char.IsAsciiDigit)));

    /// <summary>Upper-cases with invariant culture.</summary>
    public static Normalizer UpperInvariant { get; } = From<string>("upper", s => s.ToUpperInvariant());

    /// <summary>Lower-cases with invariant culture.</summary>
    public static Normalizer LowerInvariant { get; } = From<string>("lower", s => s.ToLowerInvariant());

    /// <summary>
    /// Keeps the last <paramref name="length"/> characters. Fails when the value is shorter: there is no canonical
    /// form, which is a normalization failure rather than a rule violation.
    /// </summary>
    public static Normalizer KeepLast(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        return FromPartial<string>($"keep-last-{length}", s => s.Length >= length
            ? s[^length..]
            : NormalizeResult<string>.Failure($"needs at least {length} characters, has {s.Length}"));
    }

    /// <summary>Replaces an absent value (null, whitespace string, or <c>default(T)</c>) with <paramref name="value"/>.</summary>
    public static Normalizer DefaultTo<T>(T value) => new DefaultToNormalizer<T>(value);

    /// <summary>Creates a normalizer from a total function.</summary>
    public static Normalizer From<T>(string name, Func<T, T> normalize) =>
        new DelegateNormalizer<T>(name, v => NormalizeResult<T>.Success(normalize(v)));

    /// <summary>Creates a normalizer that may fail.</summary>
    public static Normalizer FromPartial<T>(string name, Func<T, NormalizeResult<T>> normalize) =>
        new DelegateNormalizer<T>(name, normalize);

    private sealed class DelegateNormalizer<T>(string name, Func<T, NormalizeResult<T>> normalize) : Normalizer<T>(name)
    {
        protected override NormalizeResult<T> Normalize(T value) => normalize(value);
    }

    private sealed class DefaultToNormalizer<T>(T fallback) : Normalizer<T>($"default-to({Format.Value(fallback)})")
    {
        protected override NormalizeResult<T> Normalize(T value) =>
            EqualityComparer<T>.Default.Equals(value, default!) ? fallback : value;

        protected override NormalizeResult<T>? NormalizeMissing() => fallback;
    }
}
