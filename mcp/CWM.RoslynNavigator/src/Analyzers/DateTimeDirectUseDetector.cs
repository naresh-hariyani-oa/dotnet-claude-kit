using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// AP004: Detects direct use of DateTime.Now, DateTime.UtcNow, and DateTimeOffset.Now.
/// Use TimeProvider for testability and consistency.
/// Seeders and migrations are downgraded — one-shot data setup has no test surface that
/// an injected clock would improve.
/// </summary>
internal sealed class DateTimeDirectUseDetector : IAntiPatternDetector
{
    private static readonly HashSet<string> ForbiddenMembers = new(StringComparer.Ordinal)
    {
        "DateTime.Now",
        "DateTime.UtcNow",
        "DateTimeOffset.Now",
        "System.DateTime.Now",
        "System.DateTime.UtcNow",
        "System.DateTimeOffset.Now"
    };

    public bool RequiresSemanticModel => false;

    public SourceKind AppliesTo => SourceKind.Production;

    public IEnumerable<AntiPatternViolation> Detect(DetectionContext context)
    {
        foreach (var access in context.Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            context.Ct.ThrowIfCancellationRequested();

            var fullText = access.ToString();
            if (!ForbiddenMembers.Contains(fullText))
                continue;

            var line = access.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var member = AnalyzerHelpers.EnclosingMember(access);

            var confidence = IsDataSetupContext(member)
                ? AntiPatternConfidence.Medium
                : AntiPatternConfidence.High;

            yield return new AntiPatternViolation(
                Id: "AP004",
                Severity: AntiPatternSeverity.Warning,
                Message: $"Direct use of {fullText} is untestable and inconsistent across time zones",
                File: context.FilePath,
                Line: line,
                Snippet: fullText,
                Suggestion: "Inject TimeProvider and use TimeProvider.GetUtcNow()",
                Confidence: confidence,
                Member: member);
        }
    }

    private static bool IsDataSetupContext(string? member) =>
        member is not null
        && (member.Contains("Seeder", StringComparison.OrdinalIgnoreCase)
            || member.Contains("Seed", StringComparison.OrdinalIgnoreCase)
            || member.Contains("Migration", StringComparison.OrdinalIgnoreCase));
}
