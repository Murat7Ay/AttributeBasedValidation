using System.Collections;
using System.Text.RegularExpressions;

namespace Conform;

/// <summary>
/// Typed shortcuts for the built-in rules. Constraints keep them compile-time safe: <c>Length</c> only appears on
/// string members, <c>Count</c> on enumerables, <c>NotBeforeToday</c> on dates.
/// </summary>
public static class MemberBuilderExtensions
{
    public static MemberBuilder<TOwner, TValue> Length<TOwner, TValue>(this MemberBuilder<TOwner, TValue> member, int min = 0, int max = int.MaxValue)
        where TOwner : notnull where TValue : IEnumerable<char>? =>
        member.Check(Rules.Length(min, max));

    public static MemberBuilder<TOwner, TValue> Matches<TOwner, TValue>(this MemberBuilder<TOwner, TValue> member, string pattern, RegexOptions options = RegexOptions.None)
        where TOwner : notnull where TValue : IEnumerable<char>? =>
        member.Check(Rules.Matches(pattern, options));

    public static MemberBuilder<TOwner, TValue> Count<TOwner, TValue>(this MemberBuilder<TOwner, TValue> member, int min = 0, int max = int.MaxValue)
        where TOwner : notnull where TValue : IEnumerable? =>
        member.Check(Rules.Count(min, max));

    public static MemberBuilder<TOwner, TValue> Range<TOwner, TValue>(this MemberBuilder<TOwner, TValue> member, TValue min, TValue max)
        where TOwner : notnull where TValue : IComparable<TValue> =>
        member.Check(Rules.Range(min, max));

    public static MemberBuilder<TOwner, TValue?> Range<TOwner, TValue>(this MemberBuilder<TOwner, TValue?> member, TValue min, TValue max)
        where TOwner : notnull where TValue : struct, IComparable<TValue> =>
        member.Check(Rules.Range(min, max));

    public static MemberBuilder<TOwner, TValue> NotDefault<TOwner, TValue>(this MemberBuilder<TOwner, TValue> member)
        where TOwner : notnull where TValue : struct =>
        member.Check(Rules.NotDefault<TValue>());

    public static MemberBuilder<TOwner, TValue?> NotDefault<TOwner, TValue>(this MemberBuilder<TOwner, TValue?> member)
        where TOwner : notnull where TValue : struct =>
        member.Check(Rules.NotDefault<TValue>());

    public static MemberBuilder<TOwner, DateTime> NotBeforeToday<TOwner>(this MemberBuilder<TOwner, DateTime> member, int days = 0)
        where TOwner : notnull => member.Check(Rules.NotBeforeToday(days));

    public static MemberBuilder<TOwner, DateTime?> NotBeforeToday<TOwner>(this MemberBuilder<TOwner, DateTime?> member, int days = 0)
        where TOwner : notnull => member.Check(Rules.NotBeforeToday(days));

    public static MemberBuilder<TOwner, DateOnly> NotBeforeToday<TOwner>(this MemberBuilder<TOwner, DateOnly> member, int days = 0)
        where TOwner : notnull => member.Check(Rules.NotBeforeToday(days));

    public static MemberBuilder<TOwner, DateOnly?> NotBeforeToday<TOwner>(this MemberBuilder<TOwner, DateOnly?> member, int days = 0)
        where TOwner : notnull => member.Check(Rules.NotBeforeToday(days));

    public static MemberBuilder<TOwner, DateTimeOffset> NotBeforeToday<TOwner>(this MemberBuilder<TOwner, DateTimeOffset> member, int days = 0)
        where TOwner : notnull => member.Check(Rules.NotBeforeToday(days));

    public static MemberBuilder<TOwner, DateTimeOffset?> NotBeforeToday<TOwner>(this MemberBuilder<TOwner, DateTimeOffset?> member, int days = 0)
        where TOwner : notnull => member.Check(Rules.NotBeforeToday(days));
}
