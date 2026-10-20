using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Beutl.Engine.SourceGenerators.Analyzers;

/// <summary>Reads the expression that a named member is accessed through.</summary>
/// <remarks>Shared so that the analyzers read an access such as <c>a.B = c</c> the same way.</remarks>
internal static class MemberAccessSyntax
{
    /// <summary>The member access <paramref name="name"/> is the member of, or the name where it is not one.</summary>
    public static ExpressionSyntax GetAccessExpression(SimpleNameSyntax name)
        => name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access : name;
}
