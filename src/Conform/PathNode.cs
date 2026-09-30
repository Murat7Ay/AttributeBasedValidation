using System.Globalization;
using System.Text;

namespace Conform;

/// <summary>
/// Immutable parent-linked path segment. Paths are rendered to strings only when a finding or change needs one,
/// so traversal does not pay for string building on the success path.
/// </summary>
internal sealed class PathNode
{
    private PathNode(PathNode? parent, string? member, int index)
    {
        Parent = parent;
        Member = member;
        Index = index;
    }

    public PathNode? Parent { get; }
    public string? Member { get; }
    public int Index { get; }

    public static PathNode ForMember(PathNode? parent, string member) => new(parent, member, -1);

    public static PathNode ForIndex(PathNode? parent, int index) => new(parent, null, index);

    public static string Render(PathNode? node, string? leafMember = null)
    {
        if (node is null) return leafMember ?? string.Empty;

        var segments = new Stack<PathNode>();
        for (var n = node; n is not null; n = n.Parent) segments.Push(n);

        var sb = new StringBuilder();
        foreach (var s in segments) Append(sb, s.Member, s.Index);
        if (leafMember is not null) Append(sb, leafMember, -1);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string? member, int index)
    {
        if (member is null)
        {
            sb.Append('[').Append(index.ToString(CultureInfo.InvariantCulture)).Append(']');
            return;
        }

        if (sb.Length > 0) sb.Append('.');
        sb.Append(member);
    }

    public override string ToString() => Render(this);
}
