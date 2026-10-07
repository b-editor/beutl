using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Beutl.Engine.SourceGenerators.Analyzers;

public sealed partial class RenderNodeChangeMarkingAnalyzer
{
    /// <summary>
    /// The tracked state of this instance that <paramref name="alias"/> is bound to by every binding
    /// <paramref name="body"/> gives it, or null when any binding names something else or cannot be read.
    /// </summary>
    /// <remarks>
    /// The walk is flow-insensitive, so a local rebound with <c>alias = ref other</c> can reach either
    /// storage at a later write. Reporting only when every binding names the same state keeps this rule
    /// from reporting a write that may have landed elsewhere - the direction it is documented never to err
    /// in. A ref parameter is never followed: what it names on entry is the caller's storage.
    /// </remarks>
    private static ISymbol? ResolveRefTarget(
        SemanticModel model,
        SyntaxNode body,
        ILocalSymbol alias,
        ImmutableHashSet<ISymbol> trackedState,
        CancellationToken cancellationToken)
        => ResolveRefTarget(
            model,
            body,
            alias,
            trackedState,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default),
            new Dictionary<ISymbol, ISymbol?>(SymbolEqualityComparer.Default),
            cancellationToken);

    private static ISymbol? ResolveRefTarget(
        SemanticModel model,
        SyntaxNode body,
        ILocalSymbol alias,
        ImmutableHashSet<ISymbol> trackedState,
        HashSet<ISymbol> visited,
        Dictionary<ISymbol, ISymbol?> resolved,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // A local already resolved answers from the cache, so a chain of rebindings costs one resolution per
        // local however many bindings name it.
        if (resolved.TryGetValue(alias, out ISymbol? known))
            return known;

        // visited holds the chain being resolved, not every local seen, so a local reached again through a
        // sibling binding is not taken for a cycle.
        if (!visited.Add(alias))
            return null;

        try
        {
            ISymbol? target = ResolveRefTargetCore(
                model, body, alias, trackedState, visited, resolved, cancellationToken);
            resolved[alias] = target;
            return target;
        }
        finally
        {
            visited.Remove(alias);
        }
    }

    private static ISymbol? ResolveRefTargetCore(
        SemanticModel model,
        SyntaxNode body,
        ILocalSymbol alias,
        ImmutableHashSet<ISymbol> trackedState,
        HashSet<ISymbol> visited,
        Dictionary<ISymbol, ISymbol?> resolved,
        CancellationToken cancellationToken)
    {
        var bindings = new List<ExpressionSyntax>();
        foreach (SyntaxReference declaration in alias.DeclaringSyntaxReferences)
        {
            if (declaration.GetSyntax() is not VariableDeclaratorSyntax
                {
                    Initializer.Value: RefExpressionSyntax { Expression: { } initial },
                })
            {
                return null;
            }

            bindings.Add(initial);
        }

        foreach (AssignmentExpressionSyntax rebinding in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (rebinding.Right is RefExpressionSyntax { Expression: { } rebound }
                && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(rebinding.Left).Symbol, alias))
            {
                bindings.Add(rebound);
            }
        }

        ISymbol? target = null;
        foreach (ExpressionSyntax binding in bindings)
        {
            if (ResolveBindingTarget(
                    model, body, binding, trackedState, visited, resolved, cancellationToken) is not { } bound
                || (target is not null && !SymbolEqualityComparer.Default.Equals(target, bound)))
            {
                return null;
            }

            target = bound;
        }

        return target;
    }

    /// <summary>The tracked state of this instance whose storage <paramref name="binding"/> names.</summary>
    /// <remarks>
    /// An element of the state, and a field of state that is a value type, are storage inside that state,
    /// so a write through a ref to them changes it.
    /// </remarks>
    private static ISymbol? ResolveBindingTarget(
        SemanticModel model,
        SyntaxNode body,
        ExpressionSyntax binding,
        ImmutableHashSet<ISymbol> trackedState,
        HashSet<ISymbol> visited,
        Dictionary<ISymbol, ISymbol?> resolved,
        CancellationToken cancellationToken)
    {
        ExpressionSyntax storage = binding;
        while (true)
        {
            storage = storage is ParenthesizedExpressionSyntax parenthesized ? parenthesized.Expression : storage;

            if (storage is ElementAccessExpressionSyntax element)
            {
                storage = element.Expression;
                continue;
            }

            if (storage is MemberAccessExpressionSyntax { Expression: not (ThisExpressionSyntax or BaseExpressionSyntax) } member
                && model.GetTypeInfo(member.Expression).Type is { IsValueType: true })
            {
                storage = member.Expression;
                continue;
            }

            break;
        }

        SimpleNameSyntax? name = storage switch
        {
            SimpleNameSyntax simple => simple,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax } member
                => member.Name,
            _ => null,
        };

        if (name is null || model.GetSymbolInfo(name).Symbol is not { } symbol)
            return null;

        if (symbol is ILocalSymbol { RefKind: RefKind.Ref } chained)
            return ResolveRefTarget(model, body, chained, trackedState, visited, resolved, cancellationToken);

        return IsOnThisInstance(name) && trackedState.Contains(symbol.OriginalDefinition)
            ? symbol
            : null;
    }

    private static bool IsOnThisInstance(SimpleNameSyntax name)
    {
        switch (name.Parent)
        {
            case MemberAccessExpressionSyntax memberAccess when memberAccess.Name == name:
                return memberAccess.Expression is ThisExpressionSyntax or BaseExpressionSyntax;

            // A conditional access spells its receiver once, in the expression that guards the whole chain,
            // so the binding beside the name carries no receiver of its own.
            case MemberBindingExpressionSyntax binding when binding.Name == name:
                return ConditionalAccessSyntax.FindReceiver(binding)
                    is ThisExpressionSyntax or BaseExpressionSyntax;

            default:
                return true;
        }
    }

    private static bool IsSimpleAssignmentTarget(ExpressionSyntax expression)
        => (expression.Parent is AssignmentExpressionSyntax assignment
            && assignment.Left == expression
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
           || (expression.Parent is ArgumentSyntax argument && IsDeconstructionTarget(argument));

    /// <summary>Whether <paramref name="expression"/> leaves the state naming it holding something else.</summary>
    private static bool ChangesTheState(SemanticModel model, ExpressionSyntax expression)
    {
        if (ChangesTheValueBehind(expression) || MutatesInPlace(model, expression))
            return true;
        return model.GetTypeInfo(expression).Type is { IsValueType: true }
            && expression.Parent is MemberAccessExpressionSyntax member
            && member.Expression == expression
            && ChangesTheState(model, member);
    }

    /// <summary>Whether a call written on <paramref name="state"/> changes what it holds.</summary>
    /// <remarks>
    /// <para>
    /// <c>_items.Add(bounds)</c> is not a write target anywhere in the syntax, so nothing above this reads
    /// it, and a node whose <c>Process</c> walks <c>_items</c> can have the list it recorded from replaced
    /// under it. The call is read by name: a curated set of the in-place mutators the collection types are
    /// written with. That is unsound in both directions, and deliberately so - a type of one's own with a
    /// pure <c>Add</c> is reported and a mutating <c>Bump</c> is not - because the alternative measured
    /// against this repository, asking for a mark on any instance call on tracked state, reported
    /// <c>Contains</c> and <c>IndexOf</c> as mutations: every diagnostic it added was a false one, and
    /// this rule is documented never to err in that direction.
    /// </para>
    /// <para>
    /// A call that cannot write its receiver is skipped whatever it is named. A static method - an
    /// extension called in instance form - is handed the receiver by value; a <c>readonly</c> member and a
    /// member of a <c>readonly struct</c> are barred from writing it; and a member of <c>string</c>
    /// answers with a new string, which is what <c>Replace</c>, <c>Insert</c> and <c>Remove</c> on one
    /// are, and what <c>Add</c> on a date or a duration is.
    /// </para>
    /// </remarks>
    private static bool MutatesInPlace(SemanticModel model, ExpressionSyntax state)
        => FindCallOnReceiver(state) is { } call
           && model.GetSymbolInfo(call).Symbol is IMethodSymbol method
           && IsInPlaceMutator(method);

    /// <summary>The name of a call written directly on <paramref name="receiver"/>.</summary>
    /// <remarks>
    /// Directly, so that <c>_children[0].Add(x)</c> is not one: what it changes is the child the element
    /// hands back, which is another object's state and outside what this rule reads.
    /// </remarks>
    private static SimpleNameSyntax? FindCallOnReceiver(ExpressionSyntax receiver)
    {
        switch (receiver.Parent)
        {
            case MemberAccessExpressionSyntax memberAccess
                when memberAccess.Expression == receiver
                     && memberAccess.Parent is InvocationExpressionSyntax invocation
                     && invocation.Expression == memberAccess:
                return memberAccess.Name;

            // A conditional access spells its receiver in the expression that guards the chain, so the
            // call it runs is written in the binding rather than beside the name.
            case ConditionalAccessExpressionSyntax conditional
                when conditional.Expression == receiver
                     && conditional.WhenNotNull is InvocationExpressionSyntax
                     {
                         Expression: MemberBindingExpressionSyntax binding,
                     }:
                return binding.Name;

            default:
                return null;
        }
    }

    private static bool IsInPlaceMutator(IMethodSymbol method)
        => !method.IsStatic
           && !method.IsReadOnly
           && method.ContainingType is { IsReadOnly: false, SpecialType: not SpecialType.System_String }
           && InPlaceMutatorNames.Contains(method.Name);

    /// <summary>The names the collection and buffer types spell an in-place change with.</summary>
    private static readonly ImmutableHashSet<string> InPlaceMutatorNames = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Add",
        "AddAfter",
        "AddBefore",
        "AddFirst",
        "AddLast",
        "AddOrUpdate",
        "AddRange",
        "Append",
        "AppendFormat",
        "AppendJoin",
        "AppendLine",
        "Clear",
        "Dequeue",
        "Enqueue",
        "ExceptWith",
        "Fill",
        "GetOrAdd",
        "Insert",
        "InsertRange",
        "IntersectWith",
        "Pop",
        "Push",
        "Remove",
        "RemoveAll",
        "RemoveAt",
        "RemoveFirst",
        "RemoveLast",
        "RemoveRange",
        "Replace",
        "Reverse",
        "Set",
        "SetAll",
        "SetValue",
        "Sort",
        "SymmetricExceptWith",
        "TryAdd",
        "TryDequeue",
        "TryPop",
        "TryRemove",
        "TryTake",
        "TryUpdate",
        "UnionWith");

    private static bool ChangesTheValueBehind(ExpressionSyntax expression)
        => IsWriteTarget(expression)
           || (expression.Parent is ElementAccessExpressionSyntax element
               && element.Expression == expression
               && ChangesTheValueBehind(element));

    private static bool IsWriteTarget(ExpressionSyntax expression)
    {
        switch (expression.Parent)
        {
            // alias = ref other moves the name onto another storage and leaves the value in both of them
            // as it was, so the one thing it is not is a write.
            case AssignmentExpressionSyntax assignment when assignment.Left == expression:
                return assignment.Right is not RefExpressionSyntax;

            case PrefixUnaryExpressionSyntax prefix:
                return prefix.IsKind(SyntaxKind.PreIncrementExpression)
                       || prefix.IsKind(SyntaxKind.PreDecrementExpression);

            case PostfixUnaryExpressionSyntax postfix:
                return postfix.IsKind(SyntaxKind.PostIncrementExpression)
                       || postfix.IsKind(SyntaxKind.PostDecrementExpression);

            case ArgumentSyntax argument:
                return argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword)
                       || argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)
                       || IsDeconstructionTarget(argument);

            default:
                return false;
        }
    }

    private static bool IsDeconstructionTarget(ArgumentSyntax argument)
        => argument.Parent is TupleExpressionSyntax tuple && IsWriteTarget(tuple);

    private static bool IsInsideNameOf(SyntaxNode node)
    {
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                })
            {
                return true;
            }

            if (current is MemberDeclarationSyntax or AnonymousFunctionExpressionSyntax)
                return false;
        }

        return false;
    }
}
