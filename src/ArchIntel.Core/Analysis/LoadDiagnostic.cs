namespace ArchIntel.Analysis;

public sealed record LoadDiagnostic(string Kind, string Message, bool IsFatal);
