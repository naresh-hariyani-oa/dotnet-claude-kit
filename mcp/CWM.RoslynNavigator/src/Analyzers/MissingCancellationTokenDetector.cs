using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// AP009: Detects public async methods that don't accept a CancellationToken parameter.
/// Without CancellationToken, callers cannot cancel long-running operations.
///
/// Reported at Medium confidence by design: whether a token belongs on a signature depends on
/// who calls it, and a large share of public async methods have signatures fixed by a
/// framework contract, a test attribute, or a token sourced from ambient state. Skips cover
/// the mechanical cases; the rest is a judgement call, so this never drives a grade on its own.
/// </summary>
internal sealed class MissingCancellationTokenDetector : IAntiPatternDetector
{
    /// <summary>
    /// Parameter types that already carry a cancellation token, making a separate parameter
    /// redundant (HttpContext exposes RequestAborted).
    /// </summary>
    private static readonly HashSet<string> TokenBearingParameterTypes = new(StringComparer.Ordinal)
    {
        "HttpContext", "HttpContext?",
        "Microsoft.AspNetCore.Http.HttpContext"
    };

    /// <summary>
    /// Ambient token sources. A method that reaches for one of these is managing its own
    /// lifetime deliberately — adding a parameter would be strictly worse.
    /// </summary>
    private static readonly string[] AmbientTokenSources =
    [
        "ApplicationStopping",
        "ApplicationStopped",
        "RequestAborted",
        "CancellationTokenSource",
        "CancellationToken.None"
    ];

    public bool RequiresSemanticModel => true;

    public SourceKind AppliesTo => SourceKind.Production;

    public IEnumerable<AntiPatternViolation> Detect(DetectionContext context)
    {
        var model = context.Model;
        if (model is null)
            yield break;

        foreach (var method in context.Root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            context.Ct.ThrowIfCancellationRequested();

            // Only public async methods returning Task/Task<T>
            if (!method.Modifiers.Any(SyntaxKind.PublicKeyword))
                continue;

            if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword))
                continue;

            // Skip async void (handled by AP001)
            if (method.ReturnType is PredefinedTypeSyntax predefined
                && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
                continue;

            // Skip if already has CancellationToken parameter
            if (method.ParameterList.Parameters.Any(p =>
                p.Type?.ToString() is "CancellationToken" or "System.Threading.CancellationToken"))
                continue;

            // Test methods correctly take no token — the framework owns their lifetime.
            if (SourceClassifier.HasTestAttribute(method))
                continue;

            // Entry points have a fixed signature.
            if (method.Identifier.Text is "Main")
                continue;

            // A parameter that already carries a token makes another one redundant.
            if (method.ParameterList.Parameters.Any(p =>
                p.Type is not null && TokenBearingParameterTypes.Contains(p.Type.ToString())))
                continue;

            // Middleware Invoke/InvokeAsync signatures are fixed by the pipeline.
            if (method.Identifier.Text is "Invoke" or "InvokeAsync" && IsMiddlewareShaped(method))
                continue;

            // A method sourcing its own token from ambient state is doing the right thing.
            if (UsesAmbientTokenSource(method))
                continue;

            // Skip interface implementations and overrides — the contract defines the shape.
            var symbol = model.GetDeclaredSymbol(method, context.Ct);
            if (symbol is null)
                continue;

            if (symbol.IsOverride || IsInterfaceImplementation(symbol))
                continue;

            var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var snippet = $"public async {method.ReturnType} {method.Identifier.Text}(...)";

            yield return new AntiPatternViolation(
                Id: "AP009",
                Severity: AntiPatternSeverity.Warning,
                Message: $"Public async method '{method.Identifier.Text}' has no CancellationToken parameter",
                File: context.FilePath,
                Line: line,
                Snippet: snippet,
                Suggestion: "Add CancellationToken ct = default as the last parameter",
                Confidence: AntiPatternConfidence.Medium,
                Member: AnalyzerHelpers.EnclosingMember(method));
        }
    }

    private static bool IsMiddlewareShaped(MethodDeclarationSyntax method)
    {
        var type = method.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (type is null)
            return false;

        return type.Identifier.Text.EndsWith("Middleware", StringComparison.Ordinal)
            || AnalyzerHelpers.ImplementsInterface(type, "IMiddleware");
    }

    private static bool UsesAmbientTokenSource(MethodDeclarationSyntax method)
    {
        var body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        if (body is null)
            return false;

        var text = body.ToString();

        foreach (var source in AmbientTokenSources)
        {
            if (text.Contains(source, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool IsInterfaceImplementation(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        if (containingType is null)
            return false;

        foreach (var iface in containingType.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                var impl = containingType.FindImplementationForInterfaceMember(member);
                if (SymbolEqualityComparer.Default.Equals(impl, method))
                    return true;
            }
        }

        return false;
    }
}
