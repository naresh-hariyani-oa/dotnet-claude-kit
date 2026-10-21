using Microsoft.CodeAnalysis;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// Severity levels for anti-pattern violations.
/// </summary>
internal enum AntiPatternSeverity
{
    Warning,
    Error
}

/// <summary>
/// How certain a detector is that a finding is a genuine defect.
/// <see cref="High"/> means the pattern is wrong regardless of context and can be graded
/// directly. <see cref="Medium"/> means the pattern is suspicious but has legitimate uses
/// the detector cannot rule out — these need human judgement and never feed a grade.
/// </summary>
internal enum AntiPatternConfidence
{
    Medium,
    High
}

/// <summary>
/// A single anti-pattern violation found in source code.
/// </summary>
internal sealed record AntiPatternViolation(
    string Id,
    AntiPatternSeverity Severity,
    string Message,
    string File,
    int Line,
    string Snippet,
    string Suggestion,
    AntiPatternConfidence Confidence = AntiPatternConfidence.High,
    string? Member = null);

/// <summary>
/// Detects specific .NET anti-patterns using Roslyn analysis.
/// Syntax detectors operate on <see cref="SyntaxTree"/> only (fast, no compilation needed).
/// Semantic detectors require a <see cref="SemanticModel"/> for type resolution.
/// </summary>
internal interface IAntiPatternDetector
{
    /// <summary>
    /// Whether this detector requires a <see cref="SemanticModel"/> (semantic) or only a <see cref="SyntaxTree"/> (syntax).
    /// </summary>
    bool RequiresSemanticModel { get; }

    /// <summary>
    /// The source kinds this detector produces meaningful findings for. Trees of any other
    /// kind are skipped entirely — for example, a missing CancellationToken on an xUnit
    /// <c>[Fact]</c> method is not a defect, so that detector applies to production only.
    /// </summary>
    SourceKind AppliesTo { get; }

    /// <summary>
    /// Analyze a syntax tree for anti-pattern violations.
    /// </summary>
    IEnumerable<AntiPatternViolation> Detect(DetectionContext context);
}
