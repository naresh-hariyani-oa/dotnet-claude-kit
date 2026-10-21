using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// AP001: Detects async void methods (except event handlers with (object, EventArgs) signature).
/// async void swallows exceptions and prevents callers from awaiting completion.
/// </summary>
internal sealed class AsyncVoidDetector : IAntiPatternDetector
{
    public bool RequiresSemanticModel => false;

    // async void is a defect wherever it appears, including test and migration code.
    public SourceKind AppliesTo => SourceKind.Production | SourceKind.Test | SourceKind.Migration;

    public IEnumerable<AntiPatternViolation> Detect(DetectionContext context)
    {
        foreach (var method in context.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            context.Ct.ThrowIfCancellationRequested();

            if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword))
                continue;

            if (method.ReturnType is not PredefinedTypeSyntax predefined)
                continue;

            if (!predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
                continue;

            // Skip event handler signatures: (object sender, EventArgs e)
            if (IsEventHandlerSignature(method))
                continue;

            var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var snippet = $"async void {method.Identifier.Text}(...)";

            yield return new AntiPatternViolation(
                Id: "AP001",
                Severity: AntiPatternSeverity.Error,
                Message: $"async void method '{method.Identifier.Text}' swallows exceptions and cannot be awaited",
                File: context.FilePath,
                Line: line,
                Snippet: snippet,
                Suggestion: "Change return type to Task or Task<T>",
                Confidence: AntiPatternConfidence.High,
                Member: AnalyzerHelpers.EnclosingMember(method));
        }
    }

    private static bool IsEventHandlerSignature(MethodDeclarationSyntax method)
    {
        var parameters = method.ParameterList.Parameters;
        if (parameters.Count != 2)
            return false;

        var firstType = parameters[0].Type?.ToString() ?? "";
        var secondType = parameters[1].Type?.ToString() ?? "";

        return firstType is "object" or "object?"
            && (secondType.EndsWith("EventArgs", StringComparison.Ordinal)
                || secondType == "EventArgs");
    }
}
