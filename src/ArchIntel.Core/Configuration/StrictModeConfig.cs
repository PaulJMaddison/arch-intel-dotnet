namespace ArchIntel.Configuration;

public sealed class StrictModeConfig
{
    public bool? FailOnLoadIssues { get; init; }
    public bool? FailOnViolations { get; init; }
}
