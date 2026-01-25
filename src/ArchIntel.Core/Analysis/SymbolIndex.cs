using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ArchIntel.Analysis;

public sealed record SymbolIndexEntry(
    string SymbolId,
    string Kind,
    string Name,
    string Namespace,
    string? ContainingType,
    string ProjectName,
    string ProjectId);

public sealed record NamespaceStat(string Name, int NamedTypeCount, int PublicMethodCount);

public sealed record ProjectNamespaceStats(
    string ProjectName,
    string ProjectId,
    IReadOnlyList<NamespaceStat> Namespaces);

public sealed record SymbolIndexData(
    IReadOnlyList<SymbolIndexEntry> Symbols,
    IReadOnlyList<ProjectNamespaceStats> Namespaces);

public sealed class SymbolIndex
{
    private readonly DocumentFilter _filter;
    private readonly DocumentHashService _hashService;
    private readonly DocumentCache _cache;
    private readonly int _maxDegreeOfParallelism;

    public SymbolIndex(
        DocumentFilter filter,
        DocumentHashService hashService,
        DocumentCache cache,
        int maxDegreeOfParallelism)
    {
        _filter = filter;
        _hashService = hashService;
        _cache = cache;
        _maxDegreeOfParallelism = Math.Max(1, maxDegreeOfParallelism);
    }

    public async Task<SymbolIndexData> BuildAsync(
        Solution solution,
        string analysisVersion,
        CancellationToken cancellationToken)
    {
        var symbols = new ConcurrentBag<SymbolIndexEntry>();
        var symbolIds = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        var namespaceCounters = new ConcurrentDictionary<(string ProjectId, string Namespace), NamespaceCounter>();

        var documents = solution.Projects
            .Where(project => project.Language == LanguageNames.CSharp)
            .SelectMany(project => project.Documents.Select(document => (Project: project, Document: document)));

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _maxDegreeOfParallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(documents, options, async (item, token) =>
        {
            var project = item.Project;
            var document = item.Document;
            if (string.IsNullOrWhiteSpace(document.FilePath))
            {
                return;
            }

            if (_filter.IsExcluded(document.FilePath))
            {
                return;
            }

            var text = await document.GetTextAsync(token);
            var contentHash = _hashService.GetContentHash(text.ToString());
            var cacheKey = new CacheKey(
                analysisVersion,
                project.Id.Id.ToString(),
                Path.GetFullPath(document.FilePath),
                contentHash);
            var status = await _cache.GetStatusAsync(cacheKey, token);

            var root = await document.GetSyntaxRootAsync(token);
            if (root is null)
            {
                return;
            }

            SemanticModel? semanticModel = null;
            if (status == CacheStatus.Miss)
            {
                semanticModel = await document.GetSemanticModelAsync(token);
            }

            foreach (var syntaxSymbol in CollectSyntaxSymbols(root))
            {
                var symbol = semanticModel is null ? null : semanticModel.GetDeclaredSymbol(syntaxSymbol.Node, token);
                var symbolId = SymbolIdFactory.Create(symbol, syntaxSymbol, project.Id.Id.ToString());
                if (!symbolIds.TryAdd(symbolId, 0))
                {
                    continue;
                }

                var entry = CreateEntry(symbol, syntaxSymbol, project, symbolId);
                symbols.Add(entry);

                var counterKey = (entry.ProjectId, entry.Namespace);
                var counter = namespaceCounters.GetOrAdd(counterKey, _ => new NamespaceCounter(entry.ProjectName, entry.ProjectId, entry.Namespace));
                counter.Increment(entry.Kind);
            }
        });

        var symbolList = symbols
            .OrderBy(entry => entry.ProjectName, StringComparer.Ordinal)
            .ThenBy(entry => entry.Namespace, StringComparer.Ordinal)
            .ThenBy(entry => entry.ContainingType ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.Kind, StringComparer.Ordinal)
            .ThenBy(entry => entry.SymbolId, StringComparer.Ordinal)
            .ToArray();

        var namespaceList = namespaceCounters.Values
            .GroupBy(counter => new ProjectKey(counter.ProjectId, counter.ProjectName))
            .Select(group => new ProjectNamespaceStats(
                group.Key.ProjectName,
                group.Key.ProjectId,
                group
                    .OrderBy(counter => counter.Namespace, StringComparer.Ordinal)
                    .Select(counter => new NamespaceStat(
                        string.IsNullOrWhiteSpace(counter.Namespace) ? "(global)" : counter.Namespace,
                        counter.NamedTypeCount,
                        counter.PublicMethodCount))
                    .ToArray()))
            .OrderBy(stats => stats.ProjectName, StringComparer.Ordinal)
            .ThenBy(stats => stats.ProjectId, StringComparer.Ordinal)
            .ToArray();

        return new SymbolIndexData(symbolList, namespaceList);
    }

    private static SymbolIndexEntry CreateEntry(ISymbol? symbol, SyntaxSymbol syntaxSymbol, Project project, string symbolId)
    {
        if (symbol is not null)
        {
            var (symbolNamespace, containingType) = SymbolIdFactory.GetSymbolContainer(symbol);
            return new SymbolIndexEntry(
                symbolId,
                syntaxSymbol.Kind.ToString(),
                symbol.Name,
                symbolNamespace,
                containingType,
                project.Name,
                project.Id.Id.ToString());
        }

        return new SymbolIndexEntry(
            symbolId,
            syntaxSymbol.Kind.ToString(),
            syntaxSymbol.Name,
            syntaxSymbol.Namespace,
            string.IsNullOrWhiteSpace(syntaxSymbol.ContainingType) ? null : syntaxSymbol.ContainingType,
            project.Name,
            project.Id.Id.ToString());
    }

    private static IEnumerable<SyntaxSymbol> CollectSyntaxSymbols(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case ClassDeclarationSyntax classDeclaration:
                    yield return SyntaxSymbol.CreateNamedType(classDeclaration);
                    break;
                case StructDeclarationSyntax structDeclaration:
                    yield return SyntaxSymbol.CreateNamedType(structDeclaration);
                    break;
                case InterfaceDeclarationSyntax interfaceDeclaration:
                    yield return SyntaxSymbol.CreateNamedType(interfaceDeclaration);
                    break;
                case RecordDeclarationSyntax recordDeclaration:
                    yield return SyntaxSymbol.CreateNamedType(recordDeclaration);
                    break;
                case EnumDeclarationSyntax enumDeclaration:
                    yield return SyntaxSymbol.CreateEnum(enumDeclaration);
                    break;
                case DelegateDeclarationSyntax delegateDeclaration:
                    yield return SyntaxSymbol.CreateDelegate(delegateDeclaration);
                    break;
                case MethodDeclarationSyntax methodDeclaration when IsPublicMethod(methodDeclaration):
                    yield return SyntaxSymbol.CreateMethod(methodDeclaration);
                    break;
            }
        }
    }

    private static bool IsPublicMethod(MethodDeclarationSyntax methodDeclaration)
    {
        if (methodDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)))
        {
            return true;
        }

        return methodDeclaration.Parent is InterfaceDeclarationSyntax;
    }

    private readonly record struct ProjectKey(string ProjectId, string ProjectName);

    private sealed class NamespaceCounter
    {
        public NamespaceCounter(string projectName, string projectId, string namespaceName)
        {
            ProjectName = projectName;
            ProjectId = projectId;
            Namespace = namespaceName;
        }

        public string ProjectName { get; }
        public string ProjectId { get; }
        public string Namespace { get; }
        public int NamedTypeCount => _namedTypeCount;
        public int PublicMethodCount => _publicMethodCount;

        private int _namedTypeCount;
        private int _publicMethodCount;

        public void Increment(string kind)
        {
            if (string.Equals(kind, SymbolKind.NamedType.ToString(), StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _namedTypeCount);
            }
            else
            {
                Interlocked.Increment(ref _publicMethodCount);
            }
        }
    }

    private enum SymbolKind
    {
        NamedType,
        PublicMethod
    }

    private sealed record SyntaxSymbol(
        SyntaxNode Node,
        SymbolKind Kind,
        string Name,
        string Namespace,
        string? ContainingType,
        IReadOnlyList<string> ParameterTypes)
    {
        public static SyntaxSymbol CreateNamedType(TypeDeclarationSyntax node)
        {
            return new SyntaxSymbol(
                node,
                SymbolKind.NamedType,
                node.Identifier.Text,
                NamespaceHelper.GetNamespace(node),
                NamespaceHelper.GetContainingTypes(node),
                Array.Empty<string>());
        }

        public static SyntaxSymbol CreateEnum(EnumDeclarationSyntax node)
        {
            return new SyntaxSymbol(
                node,
                SymbolKind.NamedType,
                node.Identifier.Text,
                NamespaceHelper.GetNamespace(node),
                NamespaceHelper.GetContainingTypes(node),
                Array.Empty<string>());
        }

        public static SyntaxSymbol CreateDelegate(DelegateDeclarationSyntax node)
        {
            return new SyntaxSymbol(
                node,
                SymbolKind.NamedType,
                node.Identifier.Text,
                NamespaceHelper.GetNamespace(node),
                NamespaceHelper.GetContainingTypes(node),
                Array.Empty<string>());
        }

        public static SyntaxSymbol CreateMethod(MethodDeclarationSyntax node)
        {
            var parameterTypes = node.ParameterList.Parameters
                .Select(parameter => parameter.Type?.ToString() ?? "unknown")
                .ToArray();

            return new SyntaxSymbol(
                node,
                SymbolKind.PublicMethod,
                node.Identifier.Text,
                NamespaceHelper.GetNamespace(node),
                NamespaceHelper.GetContainingTypes(node),
                parameterTypes);
        }
    }

    private static class NamespaceHelper
    {
        public static string GetNamespace(SyntaxNode node)
        {
            var namespaces = node.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Select(ns => ns.Name.ToString())
                .Reverse()
                .ToArray();

            return namespaces.Length == 0 ? string.Empty : string.Join(".", namespaces);
        }

        public static string? GetContainingTypes(SyntaxNode node)
        {
            var types = node.Ancestors()
                .OfType<TypeDeclarationSyntax>()
                .Select(type => type.Identifier.Text)
                .Reverse()
                .ToArray();

            return types.Length == 0 ? null : string.Join(".", types);
        }
    }

    private static class SymbolIdFactory
    {
        private static readonly SymbolDisplayFormat NamespaceDisplayFormat = new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

        public static string Create(ISymbol? symbol, SyntaxSymbol syntaxSymbol, string projectId)
        {
            if (symbol is not null)
            {
                var docId = symbol.GetDocumentationCommentId();
                if (!string.IsNullOrWhiteSpace(docId))
                {
                    return $"{projectId}:{docId}";
                }
            }

            var fallback = BuildDocId(syntaxSymbol);
            return $"{projectId}:{fallback}";
        }

        public static (string Namespace, string? ContainingType) GetSymbolContainer(ISymbol symbol)
        {
            var namespaceName = symbol.ContainingNamespace is null || symbol.ContainingNamespace.IsGlobalNamespace
                ? string.Empty
                : symbol.ContainingNamespace.ToDisplayString(NamespaceDisplayFormat);
            var containingType = GetContainingTypeName(symbol.ContainingType);
            return (namespaceName, containingType);
        }

        private static string BuildDocId(SyntaxSymbol syntaxSymbol)
        {
            var prefix = syntaxSymbol.Kind == SymbolKind.NamedType ? "T:" : "M:";
            var container = BuildContainerName(syntaxSymbol);
            if (syntaxSymbol.Kind == SymbolKind.PublicMethod && syntaxSymbol.ParameterTypes.Count > 0)
            {
                var parameters = string.Join(",", syntaxSymbol.ParameterTypes);
                return $"{prefix}{container}({parameters})";
            }

            return $"{prefix}{container}";
        }

        private static string BuildContainerName(SyntaxSymbol syntaxSymbol)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(syntaxSymbol.Namespace))
            {
                parts.Add(syntaxSymbol.Namespace);
            }

            if (!string.IsNullOrWhiteSpace(syntaxSymbol.ContainingType))
            {
                parts.Add(syntaxSymbol.ContainingType);
            }

            parts.Add(syntaxSymbol.Name);
            return string.Join(".", parts);
        }

        private static string? GetContainingTypeName(INamedTypeSymbol? symbol)
        {
            if (symbol is null)
            {
                return null;
            }

            var names = new Stack<string>();
            var current = symbol;
            while (current is not null)
            {
                names.Push(current.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat));
                current = current.ContainingType;
            }

            return string.Join(".", names);
        }
    }
}
