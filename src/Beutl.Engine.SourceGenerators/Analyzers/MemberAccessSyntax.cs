using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Beutl.Engine.SourceGenerators.Analyzers;

/// <summary>Reads the expression that a named member is accessed through.</summary>
/// <remarks>
/// What an access does - whether it reads or writes, what it is an argument to - is decided by the
/// parent of the whole access, not of the name inside it. Every analyzer that asks a question about a
/// member has to climb to the same node first, or the two rules read <c>a.B = c</c> differently.
/// </remarks>
internal static class MemberAccessSyntax
{
    /// <summary>The member access <paramref name="name"/> is the member of, or the name where it is not one.</summary>
    public static ExpressionSyntax GetAccessExpression(SimpleNameSyntax name)
        => name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access : name;
}
