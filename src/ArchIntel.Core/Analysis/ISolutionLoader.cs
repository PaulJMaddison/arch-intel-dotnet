using Microsoft.CodeAnalysis;

namespace ArchIntel.Analysis;

public interface ISolutionLoader
{
    Task<SolutionLoadResult> LoadAsync(string solutionPathOrDirectory, CancellationToken cancellationToken);
}

public sealed record SolutionLoadResult(string SolutionPath, string RepoRootPath, Solution Solution);
