using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator.Analyzers;

/// <summary>
/// Shared syntax helpers used by more than one detector.
/// </summary>
internal static class AnalyzerHelpers
{
    /// <summary>
    /// The <c>Type.Member</c> name enclosing a node, so a finding can be judged from the
    /// response alone without opening the file. Returns null when a node sits outside any
    /// named member (for example, a top-level statement).
    /// </summary>
    public static string? EnclosingMember(SyntaxNode node)
    {
        string? memberName = null;

        for (var current = node; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax method:
                    memberName ??= method.Identifier.Text;
                    break;
                case PropertyDeclarationSyntax property:
                    memberName ??= property.Identifier.Text;
                    break;
                case ConstructorDeclarationSyntax constructor:
                    memberName ??= ".ctor";
                    break;
                case LocalFunctionStatementSyntax localFunction:
                    memberName ??= localFunction.Identifier.Text;
                    break;
                case TypeDeclarationSyntax type:
                    return memberName is null
                        ? type.Identifier.Text
                        : $"{type.Identifier.Text}.{memberName}";
            }
        }

        return memberName;
    }

    /// <summary>
    /// The method or accessor body enclosing a node — the scope a detector inspects when it
    /// needs to know what else happens around a call (for example, whether an EF query is
    /// followed by SaveChanges).
    /// </summary>
    public static SyntaxNode? EnclosingBody(SyntaxNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case MethodDeclarationSyntax method:
                    return (SyntaxNode?)method.Body ?? method.ExpressionBody;
                case LocalFunctionStatementSyntax localFunction:
                    return (SyntaxNode?)localFunction.Body ?? localFunction.ExpressionBody;
                case AccessorDeclarationSyntax accessor:
                    return (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody;
                case ConstructorDeclarationSyntax constructor:
                    return (SyntaxNode?)constructor.Body ?? constructor.ExpressionBody;
                case AnonymousFunctionExpressionSyntax lambda:
                    return lambda.Body;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a scope contains an invocation whose method name matches any of
    /// <paramref name="names"/>.
    /// </summary>
    public static bool ContainsInvocationOf(SyntaxNode scope, params string[] names)
    {
        foreach (var invocation in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = InvokedMethodName(invocation);
            if (name is null)
                continue;

            foreach (var candidate in names)
            {
                if (name.Equals(candidate, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    /// <summary>The simple name of the method an invocation targets.</summary>
    public static string? InvokedMethodName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            _ => null
        };

    /// <summary>
    /// Whether a type declaration implements an interface whose simple name matches
    /// <paramref name="interfaceName"/>. Syntax-level so it works without a semantic model.
    /// </summary>
    public static bool ImplementsInterface(TypeDeclarationSyntax type, string interfaceName)
    {
        if (type.BaseList is null)
            return false;

        foreach (var baseType in type.BaseList.Types)
        {
            var name = baseType.Type switch
            {
                QualifiedNameSyntax qualified => qualified.Right,
                SimpleNameSyntax simple => simple,
                _ => null
            };

            if (name is null)
                continue;

            if (name.Identifier.Text.Equals(interfaceName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether a block's only content is comments — an explicitly acknowledged no-op rather
    /// than a silent swallow.
    /// </summary>
    public static bool HasOnlyComments(BlockSyntax block)
    {
        if (block.Statements.Count > 0)
            return false;

        foreach (var trivia in block.DescendantTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia)
                || trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
                return true;
        }

        return false;
    }
}
