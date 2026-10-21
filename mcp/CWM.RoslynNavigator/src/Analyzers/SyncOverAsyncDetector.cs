using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// AP002: Detects synchronous blocking over async code — .Result, .Wait(), .GetAwaiter().GetResult().
/// These cause deadlocks in ASP.NET Core and thread pool starvation.
/// Semantic: the receiver must actually be a Task/ValueTask, so a codebase with its own
/// <c>Result&lt;T&gt;</c> type or a <c>.Result</c> property on a DTO is not flagged.
/// </summary>
internal sealed class SyncOverAsyncDetector : IAntiPatternDetector
{
    public bool RequiresSemanticModel => true;

    // Blocking on async is normal and harmless in test and generated code.
    public SourceKind AppliesTo => SourceKind.Production | SourceKind.Migration;

    public IEnumerable<AntiPatternViolation> Detect(DetectionContext context)
    {
        var model = context.Model;
        if (model is null)
            yield break;

        foreach (var access in context.Root.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
        {
            context.Ct.ThrowIfCancellationRequested();

            var memberName = access.Name.Identifier.Text;
            string? pattern = null;
            string? snippet = null;

            if (memberName == "Result")
            {
                // Only Task<T>.Result blocks. A domain Result<T> property does not.
                if (!IsTaskLike(model.GetTypeInfo(access.Expression, context.Ct).Type))
                    continue;

                pattern = ".Result";
                snippet = access.ToString();
            }
            else if (memberName == "Wait" && access.Parent is InvocationExpressionSyntax)
            {
                if (!IsTaskLike(model.GetTypeInfo(access.Expression, context.Ct).Type))
                    continue;

                pattern = ".Wait()";
                snippet = access.Parent.ToString();
            }
            else if (memberName == "GetResult"
                && access.Expression is InvocationExpressionSyntax innerInvocation
                && innerInvocation.Expression is MemberAccessExpressionSyntax innerAccess
                && innerAccess.Name.Identifier.Text == "GetAwaiter")
            {
                if (!IsTaskLike(model.GetTypeInfo(innerAccess.Expression, context.Ct).Type))
                    continue;

                pattern = ".GetAwaiter().GetResult()";
                snippet = access.Parent is InvocationExpressionSyntax inv ? inv.ToString() : access.ToString();
            }

            if (pattern is null)
                continue;

            var line = access.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var truncatedSnippet = snippet!.Length > 80 ? snippet[..77] + "..." : snippet;

            yield return new AntiPatternViolation(
                Id: "AP002",
                Severity: AntiPatternSeverity.Error,
                Message: $"Synchronous blocking via {pattern} causes deadlocks and thread pool starvation",
                File: context.FilePath,
                Line: line,
                Snippet: truncatedSnippet,
                Suggestion: "Use await instead of synchronous blocking",
                Confidence: AntiPatternConfidence.High,
                Member: AnalyzerHelpers.EnclosingMember(access));
        }
    }

    /// <summary>
    /// Whether a type is <c>Task</c>, <c>Task&lt;T&gt;</c>, <c>ValueTask</c>, or
    /// <c>ValueTask&lt;T&gt;</c> from System.Threading.Tasks.
    /// </summary>
    private static bool IsTaskLike(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.Name is not ("Task" or "ValueTask"))
                continue;

            if (current.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks")
                return true;
        }

        return false;
    }
}
