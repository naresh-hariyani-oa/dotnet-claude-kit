using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// AP008: Detects #pragma warning disable without a corresponding #pragma warning restore.
/// Unbounded pragmas suppress warnings for the entire file, hiding potential issues.
/// A restore only closes a disable that precedes it.
/// </summary>
internal sealed class PragmaWithoutRestoreDetector : IAntiPatternDetector
{
    public bool RequiresSemanticModel => false;

    public SourceKind AppliesTo => SourceKind.Production | SourceKind.Test;

    public IEnumerable<AntiPatternViolation> Detect(DetectionContext context)
    {
        var disables = new List<(string Code, int Line)>();
        var restores = new List<(string Code, int Line)>();

        foreach (var trivia in context.Root.DescendantTrivia())
        {
            context.Ct.ThrowIfCancellationRequested();

            if (!trivia.IsKind(SyntaxKind.PragmaWarningDirectiveTrivia))
                continue;

            if (trivia.GetStructure() is not PragmaWarningDirectiveTriviaSyntax directive)
                continue;

            var isDisable = directive.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword);
            var line = trivia.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            foreach (var errorCode in directive.ErrorCodes)
            {
                var code = errorCode.ToString().Trim();

                if (isDisable)
                    disables.Add((code, line));
                else
                    restores.Add((code, line));
            }
        }

        foreach (var (code, line) in disables)
        {
            // Only a restore that comes after the disable actually closes it.
            var closed = restores.Any(r =>
                string.Equals(r.Code, code, StringComparison.Ordinal) && r.Line > line);

            if (closed)
                continue;

            yield return new AntiPatternViolation(
                Id: "AP008",
                Severity: AntiPatternSeverity.Warning,
                Message: $"#pragma warning disable {code} has no matching restore",
                File: context.FilePath,
                Line: line,
                Snippet: $"#pragma warning disable {code}",
                Suggestion: $"Add #pragma warning restore {code} after the affected code",
                Confidence: AntiPatternConfidence.High,
                Member: null);
        }
    }
}
