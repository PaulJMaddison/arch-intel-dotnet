using ArchIntel.Analysis;

namespace ArchIntel.Reports;

public interface IReportWriter
{
    Task<ReportOutcome> WriteAsync(
        AnalysisContext context,
        string reportKind,
        string? symbol,
        ReportFormat format,
        CancellationToken cancellationToken);
}
