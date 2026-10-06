using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Beutl.Engine.SourceGenerators.Analyzers;

public sealed partial class MetadataCallbackPurityAnalyzer
{
    private static void FollowUnnamedInvocation(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        SyntaxNode node,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        // These constructs run members this rule does not follow. A member with a body here could read
        // anything, so the walk says it did not look rather than letting silence stand for a verdict.
        if (ReportUnfollowedConstruct(context, model, node, depth, walked, report))
            return;

        // An indexer is spelled as brackets around an argument, so the name loop never sees it, and the
        // accessor it runs is a body like any other.
        if (node is ExpressionSyntax element
            && element is ElementAccessExpressionSyntax or ImplicitElementAccessSyntax)
        {
            if (model.GetSymbolInfo(element, context.CancellationToken).Symbol
                is IPropertySymbol { IsStatic: false } indexer)
            {
                FollowPropertyAccess(context, model, body, indexer, element, element, depth, walked, report);
            }

            return;
        }

        if (node is InitializerExpressionSyntax { Parent: BaseObjectCreationExpressionSyntax } elements
            && elements.IsKind(SyntaxKind.CollectionInitializerExpression))
        {
            foreach (ExpressionSyntax added in elements.Expressions)
            {
                if (model.GetCollectionInitializerSymbolInfo(added, context.CancellationToken).Symbol
                    is IMethodSymbol add)
                {
                    FollowCall(
                        context,
                        add,
                        added,
                        DescribeCallKind(add),
                        depth,
                        walked,
                        report);
                }
            }

            return;
        }

        if (node is WithExpressionSyntax with)
        {
            FollowWithExpression(context, model, body, with, depth, walked, report);
            return;
        }

        // A deconstruction spells its Deconstruct nowhere and binds to nothing, so the method is asked for
        // by GetDeconstructionInfo rather than found, exactly as an implicit conversion is.
        if (node is AssignmentExpressionSyntax { Left: TupleExpressionSyntax or DeclarationExpressionSyntax }
                deconstruction
            && deconstruction.IsKind(SyntaxKind.SimpleAssignmentExpression))
        {
            FollowDeconstruction(
                context,
                model,
                body,
                model.GetDeconstructionInfo(deconstruction),
                deconstruction.Right,
                model.GetTypeInfo(deconstruction.Right, context.CancellationToken).Type,
                node,
                depth,
                walked,
                report);
            return;
        }

        // A foreach spells none of the methods it runs: the loop asks the sequence for an enumerator and
        // advances, reads and disposes it on its own. Only the deconstructing form names anything at all -
        // the Deconstruct it runs on each element - and that is a separate question from the iteration.
        if (node is CommonForEachStatementSyntax loop)
        {
            FollowIteration(context, model, body, loop, depth, walked, report);

            if (loop is ForEachVariableStatementSyntax deconstructing)
            {
                FollowDeconstruction(
                    context,
                    model,
                    body,
                    model.GetDeconstructionInfo(deconstructing),
                    null,
                    model.GetForEachStatementInfo(deconstructing).ElementType,
                    node,
                    depth,
                    walked,
                    report);
            }

            return;
        }

        // A using scope names no method at all: the compiler picks Dispose off the resource's own type and
        // runs it where the scope ends. Only the disposal is asked for here - the resource expression is a
        // node of the body like any other and is walked as one.
        if (node is UsingStatementSyntax
            or LocalDeclarationStatementSyntax { UsingKeyword.RawKind: (int)SyntaxKind.UsingKeyword })
        {
            FollowDisposal(context, model, body, node, depth, walked, report);
            return;
        }

        // A collection expression spells its construction nowhere either: the elements are written and the
        // method that turns them into the collection is chosen from the type the expression is used as.
        if (node is CollectionExpressionSyntax collection)
        {
            FollowCollectionConstruction(context, model, collection, depth, walked, report);
            return;
        }

        // An interpolated string used as a handler spells nothing it runs either: the compiler makes the
        // handler and appends the string's own parts to it, choosing both off the type it is used as.
        if (node is InterpolatedStringExpressionSyntax interpolated)
        {
            FollowInterpolatedStringHandler(context, model, interpolated, depth, walked, report);
            return;
        }

        // A query spells no method at all: every clause is rewritten into a call on what the clause before
        // it produced, and the compiler picks each call off that value's own type by the query pattern.
        // The whole query is read at once rather than clause by clause because only its first call runs on
        // a receiver the source names - the from expression - and which clause carries that call depends on
        // the clauses before it.
        if (node is QueryExpressionSyntax query)
        {
            FollowQuery(context, model, body, query, depth, walked, report);
            return;
        }

        if (node is not (BaseObjectCreationExpressionSyntax or ConstructorInitializerSyntax
            or PrimaryConstructorBaseTypeSyntax or CastExpressionSyntax or BinaryExpressionSyntax
            or PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax or AssignmentExpressionSyntax))
        {
            return;
        }

        if (model.GetSymbolInfo(node, context.CancellationToken).Symbol is not IMethodSymbol invoked)
            return;

        switch (invoked.MethodKind)
        {
            case MethodKind.Constructor:
                FollowConstructor(context, invoked, node, depth, walked, report);
                break;

            // A built-in operator has no body anywhere and lands in FollowCall's no-source case; naming the
            // user-defined kinds here says which ones the walk is actually for.
            case MethodKind.UserDefinedOperator or MethodKind.Conversion:
                FollowCall(context, invoked, node, "static method", depth, walked, report);
                break;
        }
    }

    /// <summary>
    /// Reports the members a construct runs without naming them, where this rule does not follow that
    /// construct, and says whether <paramref name="node"/> was one.
    /// </summary>
    private static bool ReportUnfollowedConstruct(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode node,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        void Report(ISymbol? member, string construct) => ReportUnfollowedMember(node, member, construct, report);

        switch (node)
        {
            case ListPatternSyntax list
                when model.GetOperation(list, context.CancellationToken) is IListPatternOperation pattern:
                Report(pattern.LengthSymbol, "a list pattern");

                // Only an element pattern reads through the indexer; [], [..] and [_, ..] test the length alone.
                if (list.Patterns.Any(static element => element is not (SlicePatternSyntax or DiscardPatternSyntax)))
                    Report(pattern.IndexerSymbol, "a list pattern");

                return true;

            case SlicePatternSyntax slice
                when model.GetOperation(slice, context.CancellationToken) is ISlicePatternOperation pattern:
                Report(pattern.SliceSymbol, "a slice pattern");
                return true;

            case RecursivePatternSyntax { PositionalPatternClause: not null } positional
                when model.GetOperation(positional, context.CancellationToken)
                    is IRecursivePatternOperation pattern:
                if (pattern.DeconstructSymbol is IMethodSymbol { IsImplicitlyDeclared: true } generated)
                {
                    // A value of a sealed type matched against a base record's pattern is still exactly
                    // that type, so its own getters are the ones the generated body dispatches to.
                    INamedTypeSymbol? exact = pattern.InputType is INamedTypeSymbol { IsSealed: true } input
                                              && IsHeldBy(input, pattern.NarrowedType)
                        ? input
                        : null;
                    FollowGeneratedDeconstruct(
                        context,
                        generated,
                        exact,
                        exact ?? pattern.NarrowedType,
                        node,
                        depth,
                        walked,
                        report);
                }
                else
                    Report(pattern.DeconstructSymbol, "a positional pattern");
                // The property subpatterns beside it name their members and are walked as names.
                return false;

            case AwaitExpressionSyntax awaited:
                AwaitExpressionInfo info = model.GetAwaitExpressionInfo(awaited);
                Report(info.GetAwaiterMethod, "an await");
                Report(info.IsCompletedProperty, "an await");
                Report(info.GetResultMethod, "an await");

                // An awaiter that is not yet complete is handed the continuation, through UnsafeOnCompleted
                // where it implements ICriticalNotifyCompletion and through OnCompleted otherwise.
                if (info.GetAwaiterMethod?.ReturnType is { } awaiter)
                    Report(GetContinuationMethod(context, awaiter), "an await");

                return false;

            // A conditional access spells the brackets as an element binding, which runs the same members.
            case ExpressionSyntax access
                when access is ElementAccessExpressionSyntax or ElementBindingExpressionSyntax
                     && model.GetOperation(access, context.CancellationToken)
                         is IImplicitIndexerReferenceOperation indexed:
                if (ReadsLength(context, model, access))
                    Report(indexed.LengthSymbol, "an index or a range");

                Report(indexed.IndexerSymbol, "an index or a range");
                return true;

            default:
                return false;
        }
    }

    private static void ReportUnfollowedMember(
        SyntaxNode node,
        ISymbol? member,
        string construct,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        // A compiler-generated member, such as a positional record's Deconstruct, has no body anyone wrote.
        if (member is not null && !member.IsImplicitlyDeclared && IsDeclaredInSource(member))
            report(node, DescribeMemberKind(member), member, string.Format(NotFollowedWithoutAName, construct));
    }

    /// <summary>
    /// Reports a member a construct runs without naming it, where an override the rule cannot see past
    /// may replace the body it followed.
    /// </summary>
    /// <remarks>
    /// Only a receiver whose making is readable carries an instance of one known type; anywhere else the
    /// declaration the binder picked is only one of the bodies that can run. The declaration is still
    /// followed, since it is the likeliest body, and the report says that it may not be the one.
    /// </remarks>
    /// <param name="throughInterface">
    /// Whether the construct calls the member through an interface, where a derived class can reimplement
    /// the interface and run its own body even for a member that is not virtual.
    /// </param>
    private static void ReportOverridable(
        SyntaxNode node,
        ITypeSymbol? receiver,
        IMethodSymbol? member,
        string kind,
        Action<SyntaxNode, string, ISymbol, string> report,
        bool throughInterface = false)
    {
        // A receiver of a sealed type, or a value type, is that exact type, so the member it inherits is
        // the one that runs whatever the declaring type allows. A type parameter is not, even one
        // constrained to structs: each instantiation can run a different implementation.
        if (receiver is { TypeKind: not TypeKind.TypeParameter } and ({ IsSealed: true } or { IsValueType: true }))
            return;

        if (member is not null
            && (IsOverridable(member) || (throughInterface && receiver is { TypeKind: TypeKind.Class }))
            && IsDeclaredInSource(member))
        {
            report(node, kind, member, OverridableWithoutAMaking);
        }
    }

    private static bool IsOverridable(IMethodSymbol member)
        => !member.IsStatic
           && member.ContainingType is { } type
           && (type.TypeKind == TypeKind.Interface
               || (!type.IsSealed
                   && (member.IsVirtual || member.IsAbstract || (member.IsOverride && !member.IsSealed))));

    private static bool IsDeclaredInSource(ISymbol member)
    {
        foreach (Location location in member.OriginalDefinition.Locations)
        {
            if (location.IsInSource)
                return true;
        }

        return false;
    }

    private static string DescribeMemberKind(ISymbol member) => member switch
    {
        IPropertySymbol { IsIndexer: true } => "indexer",
        IPropertySymbol => "property",
        IMethodSymbol method => DescribeCallKind(method),
        _ => "method",
    };

    /// <summary>
    /// Reports the await-pattern members an <c>await foreach</c> or <c>await using</c> runs on
    /// <paramref name="awaitable"/> without an await expression anywhere in the source.
    /// </summary>
    private static void ReportImplicitAwait(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ITypeSymbol? awaitable,
        SyntaxNode node,
        string construct,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        void Report(ISymbol? member) => ReportUnfollowedMember(node, member, construct, report);

        // GetAwaiter can be an extension method, which is found where the construct is written.
        IMethodSymbol? getAwaiter = awaitable is null
            ? null
            : FindInstanceMember<IMethodSymbol>(awaitable, "GetAwaiter", static m => m.Parameters.Length == 0)
              ?? model.LookupSymbols(
                      node.SpanStart,
                      awaitable,
                      "GetAwaiter",
                      includeReducedExtensionMethods: true)
                  .OfType<IMethodSymbol>()
                  .Where(static m => m.Parameters.Length == 0)
                  // The compiler picks the extension whose receiver is nearest the awaitable's own type.
                  .OrderBy(m => DistanceTo(awaitable, m.ReceiverType))
                  .FirstOrDefault();
        if (getAwaiter is null)
            return;

        ITypeSymbol awaiter = getAwaiter.ReturnType;
        Report(getAwaiter);
        Report(FindInstanceMember<IPropertySymbol>(awaiter, "IsCompleted", static _ => true));
        Report(FindInstanceMember<IMethodSymbol>(awaiter, "GetResult", static m => m.Parameters.Length == 0));
        Report(GetContinuationMethod(context, awaiter));
    }

    /// <summary>How many base types separate <paramref name="type"/> from <paramref name="target"/>.</summary>
    /// <remarks>An interface, or a type the walk never reaches, counts as farther than any base class.</remarks>
    private static int DistanceTo(ITypeSymbol type, ITypeSymbol? target)
    {
        int distance = 0;
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType, distance++)
        {
            if (SymbolEqualityComparer.Default.Equals(current, target))
                return distance;
        }

        return int.MaxValue;
    }

    private static T? FindInstanceMember<T>(ITypeSymbol? type, string name, Func<T, bool> accepts)
        where T : class, ISymbol
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (ISymbol member in current.GetMembers(name))
            {
                if (member is T { IsStatic: false } typed && accepts(typed))
                    return typed;
            }
        }

        return null;
    }

    /// <summary>Whether an implicit index or range reads the receiver's length.</summary>
    /// <remarks>
    /// A range whose both ends count from the start - <c>[1..2]</c>, <c>[..2]</c> - is lowered to a slice of
    /// those bounds without asking for the length. Anything that can count from the end, or leaves the end
    /// open, needs it.
    /// </remarks>
    private static bool ReadsLength(SyntaxNodeAnalysisContext context, SemanticModel model, ExpressionSyntax access)
    {
        BracketedArgumentListSyntax? arguments = access switch
        {
            ElementAccessExpressionSyntax element => element.ArgumentList,
            ElementBindingExpressionSyntax binding => binding.ArgumentList,
            _ => null,
        };

        if (arguments is not { Arguments.Count: 1 }
            || arguments.Arguments[0].Expression is not RangeExpressionSyntax range
            || range.RightOperand is null)
        {
            return true;
        }

        return !CountsFromStart(range.LeftOperand) || !CountsFromStart(range.RightOperand);

        bool CountsFromStart(ExpressionSyntax? bound)
            => bound is null
               || model.GetTypeInfo(bound, context.CancellationToken).Type?.SpecialType == SpecialType.System_Int32;
    }

    /// <summary>The method an await hands its continuation to on an awaiter of <paramref name="awaiter"/>.</summary>
    private static IMethodSymbol? GetContinuationMethod(SyntaxNodeAnalysisContext context, ITypeSymbol awaiter)
    {
        foreach ((string Interface, string Method) candidate in s_continuationMethods)
        {
            if (context.Compilation.GetTypeByMetadataName(candidate.Interface) is not { } completion
                || !awaiter.AllInterfaces.Contains(completion, SymbolEqualityComparer.Default)
                || completion.GetMembers(candidate.Method).FirstOrDefault() is not { } declared)
            {
                continue;
            }

            return awaiter.FindImplementationForInterfaceMember(declared) as IMethodSymbol;
        }

        return null;
    }

    private static readonly ImmutableArray<(string Interface, string Method)> s_continuationMethods =
        ImmutableArray.Create(
            ("System.Runtime.CompilerServices.ICriticalNotifyCompletion", "UnsafeOnCompleted"),
            ("System.Runtime.CompilerServices.INotifyCompletion", "OnCompleted"));

    /// <summary>Follows what a positional record's generated Deconstruct reads: the properties it hands out.</summary>
    /// <remarks>
    /// The compiler writes no body anyone can read, but it reads each positional property through its
    /// getter, and a getter the author wrote is as much a body as any.
    /// </remarks>
    private static void FollowGeneratedDeconstruct(
        SyntaxNodeAnalysisContext context,
        IMethodSymbol deconstruct,
        INamedTypeSymbol? made,
        ITypeSymbol? receiver,
        SyntaxNode node,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        foreach (IParameterSymbol parameter in deconstruct.Parameters)
        {
            for (INamedTypeSymbol? type = deconstruct.ContainingType; type is not null; type = type.BaseType)
            {
                if (type.GetMembers(parameter.Name).OfType<IPropertySymbol>().FirstOrDefault()
                    is not { GetMethod: { } getter })
                {
                    continue;
                }

                // A positional property can be virtual, and the generated body reads it through dispatch.
                IMethodSymbol runs = RunsAsMade(made, getter);
                FollowCall(context, runs, node, "property", depth, walked, report);

                if (made is null)
                    ReportOverridable(node, receiver, runs, "property", report);

                break;
            }
        }
    }

    private const string NotFollowedWithoutAName =
        "the callback runs it without naming it, through {0}, which this rule does not follow, so what it "
        + "reads was never looked at; spell the call out where the callback can be read, or keep the "
        + "member free of state that changes";

    private const string OverridableWithoutAMaking =
        "the callback runs it without naming it, on a value whose making this rule cannot read, and an "
        + "override can replace it, so the body this rule read is not necessarily the one that runs; make "
        + "the value where the callback can see it, or seal the member";

    private static void FollowImplicitConversion(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ExpressionSyntax expression,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (model.GetConversion(expression, context.CancellationToken)
            is { IsUserDefined: true, MethodSymbol: { } method })
        {
            FollowCall(context, method, expression, "static method", depth, walked, report);
        }
    }
}
