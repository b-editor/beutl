using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Beutl.Engine.SourceGenerators.Analyzers;

/// <summary>Decides which nested functions written in a body are ones that body runs.</summary>
/// <remarks>
/// A lambda and a local function are written inside the body and run only where something reaches them,
/// so a walk that reads every nested function as part of the body answers for code the body never
/// executes. Every analyzer that walks a body has to draw that line the same way, or the two rules
/// disagree about what a single method does.
/// </remarks>
internal static class NestedFunctionSyntax
{
    /// <summary>Whether <paramref name="nested"/> is a nested function that <paramref name="body"/> runs.</summary>
    /// <returns><see langword="true"/> for any node that is not a nested function at all.</returns>
    public static bool Runs(
        SemanticModel model,
        SyntaxNode body,
        SyntaxNode nested,
        CancellationToken cancellationToken)
        => nested switch
        {
            // A local function is reached only by its own name, so a name written for it somewhere else in
            // the body is the whole of what can run it.
            LocalFunctionStatementSyntax function
                => model.GetDeclaredSymbol(function, cancellationToken) is not { } declared
                   || IsNamedOutside(model, body, declared, function, cancellationToken),

            AnonymousFunctionExpressionSyntax lambda
                => RunsLambda(model, body, lambda, cancellationToken),

            _ => true,
        };

    /// <summary>Whether the body runs <paramref name="lambda"/> rather than only storing it.</summary>
    /// <remarks>
    /// A lambda assigned to a discard is written to be thrown away, and one held in a local runs only
    /// where that local is named. Any other spelling - an argument, a return, a field - hands the lambda
    /// somewhere this walk cannot follow, so it is read as one that runs.
    /// </remarks>
    public static bool RunsLambda(
        SemanticModel model,
        SyntaxNode body,
        AnonymousFunctionExpressionSyntax lambda,
        CancellationToken cancellationToken)
    {
        ExpressionSyntax stored = lambda;
        while (stored.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
            stored = (ExpressionSyntax)stored.Parent;

        if (stored.Parent is AssignmentExpressionSyntax assignment
            && assignment.Right == stored
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && model.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is IDiscardSymbol)
        {
            return false;
        }

        if (stored.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
            && model.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol local)
        {
            return IsNamedOutside(model, body, local, lambda, cancellationToken);
        }

        return true;
    }

    /// <summary>Whether <paramref name="symbol"/> is named anywhere in the body but its own declaration.</summary>
    public static bool IsNamedOutside(
        SemanticModel model,
        SyntaxNode body,
        ISymbol symbol,
        SyntaxNode declaration,
        CancellationToken cancellationToken)
    {
        foreach (SyntaxNode node in body.DescendantNodesAndSelf())
        {
            // The text test is what keeps this affordable: it costs a string compare per node and leaves a
            // semantic query only for the names that could be this one.
            if (node is not IdentifierNameSyntax name
                || name.Identifier.ValueText != symbol.Name
                || declaration.Span.Contains(name.Span))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(
                    model.GetSymbolInfo(name, cancellationToken).Symbol,
                    symbol))
            {
                return true;
            }
        }

        return false;
    }
}
