using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Beutl.Engine.SourceGenerators.Analyzers;

/// <summary>Reads the receiver out of a conditional-access chain.</summary>
/// <remarks>
/// A conditional access spells its receiver once, in the expression that guards the whole chain, so the
/// binding beside a name carries none of its own. Every analyzer that asks what a call runs on has to
/// answer that question the same way, or the two rules disagree about what <c>a?.B()</c> is a call on.
/// </remarks>
internal static class ConditionalAccessSyntax
{
    /// <summary>The receiver the conditional access enclosing <paramref name="binding"/> tests and binds to.</summary>
    /// <returns>
    /// The guarding expression, or <see langword="null"/> when the binding is not inside a chain at all.
    /// </returns>
    public static ExpressionSyntax? FindReceiver(MemberBindingExpressionSyntax binding)
    {
        for (SyntaxNode? current = binding; current is not null; current = current.Parent)
        {
            if (current.Parent is ConditionalAccessExpressionSyntax conditional
                && conditional.WhenNotNull == current)
            {
                return conditional.Expression;
            }
        }

        return null;
    }
}
