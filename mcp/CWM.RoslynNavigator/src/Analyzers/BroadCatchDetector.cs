using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// AP005: Detects broad catch(Exception) that swallows errors.
/// AP007: Detects empty catch blocks that silently swallow errors.
///
/// The defect is <em>swallowing</em>, not catching. A block that logs the exception or
/// rethrows is a bounded resilience wrapper — reported at Medium confidence so it can be
/// reviewed without dominating the grade. A block with an explanatory comment satisfies the
/// AP007 suggestion and is cleared.
///
/// Files named *Middleware*, *ExceptionHandler*, and Program.cs are excluded as legitimate
/// application boundaries.
/// </summary>
internal sealed class BroadCatchDetector : IAntiPatternDetector
{
    private static readonly string[] LoggerMethodPrefixes = ["Log"];

    private static readonly HashSet<string> LoggerMethodNames = new(StringComparer.Ordinal)
    {
        "Error", "Warning", "Fatal", "Critical", "Information", "Debug", "Verbose", "Trace",
        "TrackException", "CaptureException", "RecordException"
    };

    public bool RequiresSemanticModel => false;

    public SourceKind AppliesTo => SourceKind.Production;

    public IEnumerable<AntiPatternViolation> Detect(DetectionContext context)
    {
        var fileName = Path.GetFileNameWithoutExtension(context.FilePath);

        // Application boundaries are the one place a catch-all belongs.
        if (fileName.Contains("Middleware", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("ExceptionHandler", StringComparison.OrdinalIgnoreCase)
            || fileName.Equals("Program", StringComparison.Ordinal))
            yield break;

        foreach (var catchClause in context.Root.DescendantNodes().OfType<CatchClauseSyntax>())
        {
            context.Ct.ThrowIfCancellationRequested();

            var line = catchClause.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var member = AnalyzerHelpers.EnclosingMember(catchClause);

            // AP007: Empty catch blocks
            if (catchClause.Block.Statements.Count == 0)
            {
                // A comment documenting the intent is exactly what the suggestion asks for.
                if (AnalyzerHelpers.HasOnlyComments(catchClause.Block))
                    continue;

                // Swallowing a cancellation is the standard cooperative-shutdown idiom —
                // a BackgroundService whose token trips on stop has nothing to report.
                if (IsCancellationException(catchClause.Declaration?.Type.ToString()))
                    continue;

                var snippet = catchClause.Declaration is not null
                    ? $"catch ({catchClause.Declaration.Type})"
                    : "catch";

                yield return new AntiPatternViolation(
                    Id: "AP007",
                    Severity: AntiPatternSeverity.Error,
                    Message: "Empty catch block silently swallows errors",
                    File: context.FilePath,
                    Line: line,
                    Snippet: $"{snippet} {{ }}",
                    Suggestion: "Log the exception or rethrow. If intentionally ignoring, add a comment explaining why",
                    Confidence: AntiPatternConfidence.High,
                    Member: member);
                continue; // Don't also flag as AP005
            }

            var isBare = catchClause.Declaration is null;
            var typeName = catchClause.Declaration?.Type.ToString();

            if (!isBare && typeName is not ("Exception" or "System.Exception"))
                continue;

            // A handled catch-all is a resilience boundary; an unhandled one hides bugs.
            var handled = Handles(catchClause);
            var filtered = catchClause.Filter is not null;

            var confidence = handled || filtered
                ? AntiPatternConfidence.Medium
                : AntiPatternConfidence.High;

            var message = isBare
                ? "Bare catch clause catches all exceptions including OutOfMemoryException"
                : "catch(Exception) catches all exceptions including critical system exceptions";

            if (confidence == AntiPatternConfidence.Medium)
            {
                message += filtered
                    ? " (narrowed by an exception filter — verify the filter is tight enough)"
                    : " (logs or rethrows, so likely a deliberate resilience boundary)";
            }

            yield return new AntiPatternViolation(
                Id: "AP005",
                Severity: AntiPatternSeverity.Warning,
                Message: message,
                File: context.FilePath,
                Line: line,
                Snippet: isBare ? "catch { ... }" : $"catch ({typeName}) {{ ... }}",
                Suggestion: "Catch specific exception types relevant to the operation",
                Confidence: confidence,
                Member: member);
        }
    }

    /// <summary>
    /// Whether a caught type is a cancellation signal rather than a failure.
    /// </summary>
    private static bool IsCancellationException(string? typeName)
    {
        if (typeName is null)
            return false;

        var simpleName = typeName.Split('.')[^1];

        return simpleName is "OperationCanceledException" or "TaskCanceledException";
    }

    /// <summary>
    /// Whether the catch body logs the exception or rethrows — either makes the failure
    /// observable rather than swallowed.
    /// </summary>
    private static bool Handles(CatchClauseSyntax catchClause)
    {
        foreach (var node in catchClause.Block.DescendantNodes())
        {
            if (node is ThrowStatementSyntax or ThrowExpressionSyntax)
                return true;

            if (node is InvocationExpressionSyntax invocation && IsLoggingCall(invocation))
                return true;
        }

        return false;
    }

    private static bool IsLoggingCall(InvocationExpressionSyntax invocation)
    {
        var name = AnalyzerHelpers.InvokedMethodName(invocation);
        if (name is null)
            return false;

        foreach (var prefix in LoggerMethodPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length)
                return true;
        }

        if (!LoggerMethodNames.Contains(name))
            return false;

        // Names like Error/Warning are only logging calls when the receiver looks like a logger.
        return invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Expression.ToString().Contains("log", StringComparison.OrdinalIgnoreCase);
    }
}
