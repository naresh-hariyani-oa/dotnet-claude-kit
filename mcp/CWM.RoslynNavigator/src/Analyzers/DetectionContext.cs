using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// Everything a detector needs for one syntax tree. The root is parsed once and shared
/// across all detectors rather than re-fetched per detector.
/// </summary>
internal sealed class DetectionContext
{
    private SyntaxNode? _root;
    private Dictionary<int, HashSet<string>>? _inlineSuppressions;
    private List<(TextSpanRange Range, HashSet<string> Ids)>? _attributeSuppressions;

    public DetectionContext(
        SyntaxTree tree,
        SemanticModel? model,
        SourceKind kind,
        CancellationToken ct)
    {
        Tree = tree;
        Model = model;
        Kind = kind;
        Ct = ct;
    }

    public SyntaxTree Tree { get; }
    public SemanticModel? Model { get; }
    public SourceKind Kind { get; }
    public CancellationToken Ct { get; }

    public string FilePath => Tree.FilePath ?? "unknown";

    public SyntaxNode Root => _root ??= Tree.GetRoot(Ct);

    /// <summary>
    /// Whether the given anti-pattern id is suppressed at <paramref name="line"/> by an inline
    /// <c>// cwm:ignore APXXX — reason</c> comment (on the line or the line above), or by a
    /// <c>[SuppressMessage("CWM", "APXXX")]</c> attribute on an enclosing declaration.
    /// </summary>
    public bool IsSuppressedAt(string id, int line)
    {
        _inlineSuppressions ??= BuildInlineSuppressions();

        if (_inlineSuppressions.TryGetValue(line, out var onLine) && Matches(onLine, id))
            return true;

        if (_inlineSuppressions.TryGetValue(line - 1, out var above) && Matches(above, id))
            return true;

        _attributeSuppressions ??= BuildAttributeSuppressions();

        foreach (var (range, ids) in _attributeSuppressions)
        {
            if (line >= range.StartLine && line <= range.EndLine && Matches(ids, id))
                return true;
        }

        return false;
    }

    private static bool Matches(HashSet<string> ids, string id) =>
        ids.Contains(id) || ids.Contains("*");

    private Dictionary<int, HashSet<string>> BuildInlineSuppressions()
    {
        var map = new Dictionary<int, HashSet<string>>();

        foreach (var trivia in Root.DescendantTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
                continue;

            var text = trivia.ToString();
            var marker = text.IndexOf("cwm:ignore", StringComparison.OrdinalIgnoreCase);
            if (marker < 0)
                continue;

            var ids = ParseIds(text[(marker + "cwm:ignore".Length)..]);
            if (ids.Count == 0)
                continue;

            var line = trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            if (map.TryGetValue(line, out var existing))
                existing.UnionWith(ids);
            else
                map[line] = ids;
        }

        return map;
    }

    /// <summary>
    /// Reads ids from the text following the marker, stopping at the first separator that
    /// starts a human-readable reason (a dash or a colon).
    /// </summary>
    private static HashSet<string> ParseIds(string tail)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in tail.Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();

            if (token is "-" or "--" or "—" or "–" or ":")
                break;

            if (token == "*")
            {
                ids.Add("*");
                continue;
            }

            if (!IsAntiPatternId(token))
                break;

            ids.Add(token.ToUpperInvariant());
        }

        return ids;
    }

    private static bool IsAntiPatternId(string token)
    {
        if (token.Length != 5)
            return false;

        if (!token.StartsWith("AP", StringComparison.OrdinalIgnoreCase))
            return false;

        return char.IsAsciiDigit(token[2]) && char.IsAsciiDigit(token[3]) && char.IsAsciiDigit(token[4]);
    }

    private List<(TextSpanRange Range, HashSet<string> Ids)> BuildAttributeSuppressions()
    {
        var results = new List<(TextSpanRange, HashSet<string>)>();

        foreach (var attribute in Root.DescendantNodes().OfType<AttributeSyntax>())
        {
            var name = attribute.Name switch
            {
                QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
                SimpleNameSyntax simple => simple.Identifier.Text,
                _ => attribute.Name.ToString()
            };

            if (name is not ("SuppressMessage" or "SuppressMessageAttribute"))
                continue;

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var argument in attribute.ArgumentList?.Arguments ?? default)
            {
                if (argument.Expression is not LiteralExpressionSyntax literal
                    || !literal.IsKind(SyntaxKind.StringLiteralExpression))
                    continue;

                var value = literal.Token.ValueText;

                // Accept both "AP005" and the "CWM:AP005" checkId convention.
                var colon = value.LastIndexOf(':');
                var candidate = colon >= 0 ? value[(colon + 1)..] : value;

                if (IsAntiPatternId(candidate))
                    ids.Add(candidate.ToUpperInvariant());
            }

            if (ids.Count == 0)
                continue;

            // The attribute suppresses findings anywhere in the declaration it decorates.
            var target = attribute.FirstAncestorOrSelf<MemberDeclarationSyntax>();
            if (target is null)
                continue;

            var span = target.GetLocation().GetLineSpan();
            results.Add((
                new TextSpanRange(span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1),
                ids));
        }

        return results;
    }

    private readonly record struct TextSpanRange(int StartLine, int EndLine);
}
