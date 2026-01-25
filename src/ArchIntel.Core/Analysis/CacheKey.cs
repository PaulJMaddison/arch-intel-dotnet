namespace ArchIntel.Analysis;

public sealed record CacheKey(
    string AnalysisVersion,
    string ProjectId,
    string DocumentPath,
    string ContentHash)
{
    public string ToCacheKeyString() =>
        string.Join("|", AnalysisVersion, ProjectId, DocumentPath, ContentHash);
}
