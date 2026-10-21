using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// AP003: Detects direct <c>new HttpClient()</c> instantiation.
/// Direct instantiation causes socket exhaustion. Use IHttpClientFactory instead.
/// Test code is excluded — integration tests routinely construct a client per test against
/// a local server, where socket exhaustion is not a concern.
/// </summary>
internal sealed class HttpClientInstantiationDetector : IAntiPatternDetector
{
    public bool RequiresSemanticModel => false;

    public SourceKind AppliesTo => SourceKind.Production | SourceKind.Migration;

    public IEnumerable<AntiPatternViolation> Detect(DetectionContext context)
    {
        foreach (var creation in context.Root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            context.Ct.ThrowIfCancellationRequested();

            var typeName = creation.Type.ToString();
            if (typeName is not ("HttpClient" or "System.Net.Http.HttpClient"))
                continue;

            var line = creation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

            // A client built over an injected handler is the documented IHttpClientFactory
            // composition pattern, not ad-hoc instantiation.
            var confidence = creation.ArgumentList?.Arguments.Count > 0
                ? AntiPatternConfidence.Medium
                : AntiPatternConfidence.High;

            yield return new AntiPatternViolation(
                Id: "AP003",
                Severity: AntiPatternSeverity.Warning,
                Message: "Direct HttpClient instantiation causes socket exhaustion under load",
                File: context.FilePath,
                Line: line,
                Snippet: $"new {typeName}()",
                Suggestion: "Use IHttpClientFactory via dependency injection",
                Confidence: confidence,
                Member: AnalyzerHelpers.EnclosingMember(creation));
        }
    }
}
