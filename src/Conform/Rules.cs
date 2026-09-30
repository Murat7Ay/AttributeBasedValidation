using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Conform;

/// <summary>Built-in rules. Each is a reusable value; attach it with <c>Check(...)</c> or the builder shortcuts.</summary>
public static class Rules
{
    /// <summary>String length within [<paramref name="min"/>, <paramref name="max"/>]. Code: <c>length</c>.</summary>
    public static Rule Length(int min = 0, int max = int.MaxValue) => new LengthRule(min, max);

    /// <summary>String matches <paramref name="pattern"/> (unanchored unless the pattern anchors itself). Code: <c>pattern</c>.</summary>
    public static Rule Matches(string pattern, RegexOptions options = RegexOptions.None) => new PatternRule(pattern, options);

    /// <summary>Value within [<paramref name="min"/>, <paramref name="max"/>]. Code: <c>range</c>.</summary>
    public static Rule Range<T>(T min, T max) where T : IComparable<T> => new RangeRule<T>(min, max);

    /// <summary>Collection element count within [<paramref name="min"/>, <paramref name="max"/>]. Code: <c>count</c>.</summary>
    public static Rule Count(int min = 0, int max = int.MaxValue) => new CountRule(min, max);

    /// <summary>A present value type is not its default (e.g. <c>0001-01-01</c>). Code: <c>not-default</c>.</summary>
    public static Rule NotDefault<T>() where T : struct => new NotDefaultRule<T>();

    /// <summary>
    /// Date is on or after today + <paramref name="days"/> (by the evaluation clock).
    /// Works for <see cref="DateTime"/>, <see cref="DateTimeOffset"/> and <see cref="DateOnly"/>. Code: <c>min-date</c>.
    /// </summary>
    public static Rule NotBeforeToday(int days = 0) => new RelativeDateRule(days);

    /// <summary>Custom predicate rule.</summary>
    public static Rule Must<T>(string code, Func<T, bool> predicate, string message) =>
        new PredicateRule<T>(code, message, (v, _) => predicate(v));

    /// <summary>Custom predicate rule that can read the owner, clock and scenarios.</summary>
    public static Rule Must<T>(string code, Func<T, RuleContext, bool> predicate, string message) =>
        new PredicateRule<T>(code, message, predicate);

    internal static string Bounds(int min, int max, string unit) => (min, max) switch
    {
        (<= 0, int.MaxValue) => $"any number of {unit}",
        (_, int.MaxValue) => $"at least {min} {unit}",
        (<= 0, _) => $"at most {max} {unit}",
        _ when min == max => $"exactly {min} {unit}",
        _ => $"between {min} and {max} {unit}",
    };

    private sealed class LengthRule : Rule<string>
    {
        private readonly int _min, _max;

        public LengthRule(int min, int max) : base("length", Bounds(min, max, "characters"))
        {
            if (min < 0 || max < min) throw new ArgumentOutOfRangeException(nameof(max), $"Invalid length bounds {min}..{max}.");
            _min = min;
            _max = max;
        }

        protected override Violation? Check(string value, in RuleContext context) =>
            value.Length < _min || value.Length > _max
                ? new Violation($"must have {Description} (has {value.Length})")
                : null;
    }

    private sealed class PatternRule : Rule<string>
    {
        private readonly Regex _regex;

        public PatternRule(string pattern, RegexOptions options) : base("pattern", $"matches /{pattern}/")
        {
            // Constructed once per rule; invalid patterns fail when the contract is defined, not when data arrives.
            _regex = new Regex(pattern, options | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }

        protected override Violation? Check(string value, in RuleContext context) =>
            _regex.IsMatch(value) ? null : new Violation($"must match /{_regex}/");
    }

    private sealed class RangeRule<T>(T min, T max) : Rule<T>("range", $"between {Format.Value(min)} and {Format.Value(max)}")
        where T : IComparable<T>
    {
        protected override Violation? Check(T value, in RuleContext context) =>
            value.CompareTo(min) < 0 || value.CompareTo(max) > 0
                ? new Violation($"must be {Description}")
                : null;
    }

    private sealed class CountRule : Rule<IEnumerable>
    {
        private readonly int _min, _max;

        public CountRule(int min, int max) : base("count", Bounds(min, max, "items"))
        {
            if (min < 0 || max < min) throw new ArgumentOutOfRangeException(nameof(max), $"Invalid count bounds {min}..{max}.");
            _min = min;
            _max = max;
        }

        public override bool AppliesTo(Type valueType) =>
            valueType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(valueType);

        protected override Violation? Check(IEnumerable value, in RuleContext context)
        {
            var count = value is ICollection c ? c.Count : CountSlow(value);
            return count < _min || count > _max ? new Violation($"must have {Description} (has {count})") : null;
        }

        private static int CountSlow(IEnumerable value)
        {
            var n = 0;
            foreach (var _ in value) n++;
            return n;
        }
    }

    private sealed class NotDefaultRule<T>() : Rule<T>("not-default", $"a value other than default({typeof(T).Name})")
        where T : struct
    {
        protected override Violation? Check(T value, in RuleContext context) =>
            EqualityComparer<T>.Default.Equals(value, default) ? new Violation("must not be the default value") : null;
    }

    private sealed class RelativeDateRule(int days) : Rule<object>(
        "min-date",
        days switch { 0 => "today or later", 1 => "tomorrow or later", _ => $"today + {days} days or later" })
    {
        public override bool AppliesTo(Type valueType) =>
            TypeCompat.Unwrap(valueType) is var t && (t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(DateOnly));

        protected override Violation? Check(object value, in RuleContext context)
        {
            var date = value switch
            {
                DateTime d => DateOnly.FromDateTime(d),
                DateTimeOffset o => DateOnly.FromDateTime(o.ToOffset(context.Clock.GetLocalNow().Offset).DateTime),
                DateOnly d => d,
                _ => throw new InvalidOperationException($"'{Code}' cannot check {value.GetType().Name}."),
            };
            var min = context.Today.AddDays(days);
            return date < min
                ? new Violation($"must be {Description} (earliest {min.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})")
                : null;
        }
    }

    private sealed class PredicateRule<T>(string code, string message, Func<T, RuleContext, bool> predicate)
        : Rule<T>(code, message)
    {
        protected override Violation? Check(T value, in RuleContext context) =>
            predicate(value, context) ? null : new Violation(Description);
    }
}
