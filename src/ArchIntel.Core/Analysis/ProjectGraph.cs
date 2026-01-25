using System.Collections.ObjectModel;
using System.Linq;

namespace ArchIntel.Analysis;

public sealed record ProjectNode(string Id, string Name, string Path, string Layer);

public sealed record ProjectEdge(string FromId, string ToId);

public sealed record ProjectGraph(
    IReadOnlyList<ProjectNode> Nodes,
    IReadOnlyList<ProjectEdge> Edges,
    IReadOnlyList<IReadOnlyList<string>> Cycles)
{
    public static ProjectGraph Create(
        IReadOnlyList<ProjectNode> nodes,
        IReadOnlyList<ProjectEdge> edges,
        IReadOnlyList<IReadOnlyList<string>> cycles)
    {
        return new ProjectGraph(
            AsReadOnly(nodes),
            AsReadOnly(edges),
            AsReadOnly(cycles));
    }

    private static IReadOnlyList<T> AsReadOnly<T>(IReadOnlyList<T> values)
    {
        if (values is ReadOnlyCollection<T>)
        {
            return values;
        }

        return new ReadOnlyCollection<T>(values.ToArray());
    }
}
