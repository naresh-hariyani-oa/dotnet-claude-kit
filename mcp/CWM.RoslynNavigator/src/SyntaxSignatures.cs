using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CWM.RoslynNavigator;

/// <summary>
/// Builds one-line, body-free member and type signatures from syntax nodes.
/// Shared by get_file_outline and get_symbol_source for token-cheap skeletons.
/// </summary>
internal static partial class SyntaxSignatures
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRuns();

    internal static string NormalizeWs(string text) => WhitespaceRuns().Replace(text, " ").Trim();

    internal static string GetMemberSignature(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax m =>
            NormalizeWs($"{Mods(m.Modifiers)}{m.ReturnType} {m.Identifier.Text}{m.TypeParameterList}{m.ParameterList}"),
        ConstructorDeclarationSyntax c =>
            NormalizeWs($"{Mods(c.Modifiers)}{c.Identifier.Text}{c.ParameterList}"),
        PropertyDeclarationSyntax p =>
            NormalizeWs($"{Mods(p.Modifiers)}{p.Type} {p.Identifier.Text}{Accessors(p)}"),
        IndexerDeclarationSyntax i =>
            NormalizeWs($"{Mods(i.Modifiers)}{i.Type} this{i.ParameterList}"),
        FieldDeclarationSyntax f =>
            NormalizeWs($"{Mods(f.Modifiers)}{f.Declaration.Type} {string.Join(", ", f.Declaration.Variables.Select(v => v.Identifier.Text))}"),
        EventFieldDeclarationSyntax e =>
            NormalizeWs($"{Mods(e.Modifiers)}event {e.Declaration.Type} {string.Join(", ", e.Declaration.Variables.Select(v => v.Identifier.Text))}"),
        EventDeclarationSyntax e =>
            NormalizeWs($"{Mods(e.Modifiers)}event {e.Type} {e.Identifier.Text}"),
        OperatorDeclarationSyntax o =>
            NormalizeWs($"{Mods(o.Modifiers)}{o.ReturnType} operator {o.OperatorToken.Text}{o.ParameterList}"),
        DelegateDeclarationSyntax d =>
            NormalizeWs($"{Mods(d.Modifiers)}delegate {d.ReturnType} {d.Identifier.Text}{d.ParameterList}"),
        BaseTypeDeclarationSyntax t => GetTypeHeader(t),
        _ => NormalizeWs(member.ToString().Split('\n', 2)[0])
    };

    internal static string GetTypeHeader(BaseTypeDeclarationSyntax type)
    {
        var keyword = type switch
        {
            RecordDeclarationSyntax r => r.ClassOrStructKeyword.Text == "struct" ? "record struct" : "record",
            ClassDeclarationSyntax => "class",
            InterfaceDeclarationSyntax => "interface",
            StructDeclarationSyntax => "struct",
            EnumDeclarationSyntax => "enum",
            _ => "type"
        };

        var typeParams = (type as TypeDeclarationSyntax)?.TypeParameterList?.ToString() ?? "";
        var recordParams = (type as RecordDeclarationSyntax)?.ParameterList?.ToString() ?? "";
        var baseList = type.BaseList is null ? "" : " " + NormalizeWs(type.BaseList.ToString());

        return NormalizeWs($"{Mods(type.Modifiers)}{keyword} {type.Identifier.Text}{typeParams}{recordParams}{baseList}");
    }

    internal static string GetMemberKind(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax => "method",
        ConstructorDeclarationSyntax => "constructor",
        PropertyDeclarationSyntax => "property",
        IndexerDeclarationSyntax => "indexer",
        FieldDeclarationSyntax f => f.Modifiers.Any(m => m.Text == "const") ? "constant" : "field",
        EventFieldDeclarationSyntax or EventDeclarationSyntax => "event",
        RecordDeclarationSyntax r => r.ClassOrStructKeyword.Text == "struct" ? "record struct" : "record",
        ClassDeclarationSyntax => "class",
        InterfaceDeclarationSyntax => "interface",
        StructDeclarationSyntax => "struct",
        EnumDeclarationSyntax => "enum",
        EnumMemberDeclarationSyntax => "enum member",
        DelegateDeclarationSyntax => "delegate",
        OperatorDeclarationSyntax => "operator",
        _ => "member"
    };

    private static string Mods(SyntaxTokenList modifiers) =>
        modifiers.Count == 0 ? "" : string.Join(" ", modifiers.Select(t => t.Text)) + " ";

    private static string Accessors(PropertyDeclarationSyntax p)
    {
        if (p.AccessorList is null)
            return " { get; }"; // expression-bodied property

        var kinds = p.AccessorList.Accessors.Select(a => a.Keyword.Text + ";");
        return " { " + string.Join(" ", kinds) + " }";
    }
}
