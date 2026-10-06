using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Beutl.Engine.SourceGenerators.Analyzers;

public sealed partial class MetadataCallbackPurityAnalyzer
{
    private static void FollowWithExpression(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        WithExpressionSyntax with,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        ITypeSymbol? copiedType = model.GetTypeInfo(with.Expression, context.CancellationToken).Type;
        INamedTypeSymbol? made = null;

        if (copiedType is INamedTypeSymbol { TypeKind: TypeKind.Class } copied)
        {
            // A record copies itself through a virtual clone, so the copy constructor that runs is the one
            // of the type the value was made as.
            made = FollowHeldCreation(
                context,
                GetCreationHeldBy(context, model, with.Expression),
                body,
                with,
                depth,
                walked,
                report);
            INamedTypeSymbol runs = made ?? copied;

            if (GetCopyConstructor(runs) is { } copy)
            {
                FollowCall(context, copy, with, "constructor", depth, walked, report);

                if (made is null && !copied.IsSealed && IsDeclaredInSource(copy))
                    report(with, "constructor", copy, OverridableWithoutAMaking);
            }
        }

        foreach (ExpressionSyntax assigned in with.Initializer.Expressions)
        {
            if (assigned is AssignmentExpressionSyntax { Left: { } target }
                && model.GetSymbolInfo(target, context.CancellationToken).Symbol
                    is IPropertySymbol { IsStatic: false, SetMethod: { } setter })
            {
                // The initializer assigns the clone, which is an instance of the type the value was made as,
                // so a virtual init accessor runs as that type overrides it.
                IMethodSymbol runs = RunsAsMade(made, setter);
                FollowCall(context, runs, target, "property", depth, walked, report);

                if (made is null)
                    ReportOverridable(target, copiedType, runs, "property", report);
            }
        }
    }

    private static IMethodSymbol? GetCopyConstructor(INamedTypeSymbol type)
        => type.InstanceConstructors.FirstOrDefault(
            constructor => constructor.Parameters.Length == 1
                           && SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, type));

    /// <summary>Follows the disposal a <c>using</c> scope runs when it ends.</summary>
    /// <remarks>
    /// A declaration form declares one local per resource and every one of them is disposed, so every one
    /// is followed. What the resource expression itself reads is not asked for here: that expression is a
    /// node of the body and the walk reaches it on its own.
    /// </remarks>
    private static void FollowDisposal(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        SyntaxNode scope,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        VariableDeclarationSyntax? declaration;
        ExpressionSyntax? resource;
        bool asynchronous;

        switch (scope)
        {
            case UsingStatementSyntax statement:
                (declaration, resource) = (statement.Declaration, statement.Expression);
                asynchronous = !statement.AwaitKeyword.IsKind(SyntaxKind.None);
                break;

            case LocalDeclarationStatementSyntax local:
                (declaration, resource) = (local.Declaration, null);
                asynchronous = !local.AwaitKeyword.IsKind(SyntaxKind.None);
                break;

            default:
                return;
        }

        void Follow(ITypeSymbol? type, ExpressionSyntax? value)
        {
            if (type is null || GetDisposeMethod(context, type, asynchronous) is not { } dispose)
                return;

            INamedTypeSymbol? made = value is null
                ? null
                : FollowHeldCreation(
                    context,
                    GetCreationHeldBy(context, model, value),
                    body,
                    scope,
                    depth,
                    walked,
                    report);

            // Resolved again on the made type rather than only mapped from the static one, because the made
            // type can reimplement the disposal interface with a body the static type's mapping never names.
            IMethodSymbol runs = made is null
                ? dispose
                : RunsAsMade(made, GetDisposeMethod(context, made, asynchronous) ?? dispose);
            FollowCall(context, runs, scope, "method", depth, walked, report);

            // An await using awaits what DisposeAsync hands back, with no await written anywhere.
            if (asynchronous)
                ReportImplicitAwait(context, model, runs.ReturnType, scope, "an await using", report);

            if (made is null)
            {
                bool throughInterface =
                    context.Compilation.GetTypeByMetadataName(
                        asynchronous ? AsyncDisposableTypeName : DisposableTypeName) is { } disposable
                    && type.AllInterfaces.Contains(disposable, SymbolEqualityComparer.Default);
                ReportOverridable(scope, type, runs, "method", report, throughInterface);
            }
        }

        if (declaration is not null)
        {
            foreach (VariableDeclaratorSyntax declarator in declaration.Variables)
            {
                if (model.GetDeclaredSymbol(declarator, context.CancellationToken) is ILocalSymbol declared)
                    Follow(declared.Type, declarator.Initializer?.Value);
            }
        }

        if (resource is not null)
            Follow(model.GetTypeInfo(resource, context.CancellationToken).Type, resource);
    }

    /// <summary>The disposal the compiler runs on a resource of <paramref name="type"/>.</summary>
    /// <remarks>
    /// No public operation carries the chosen method, so it is resolved the way the compiler chooses it:
    /// through <see cref="IDisposable"/> where the type implements it - which is also the only spelling
    /// that finds an explicit implementation - and otherwise by the name alone, which is how a
    /// <c>ref struct</c>, the one shape disposed without naming the interface, declares its own.
    /// </remarks>
    private static IMethodSymbol? GetDisposeMethod(
        SyntaxNodeAnalysisContext context,
        ITypeSymbol type,
        bool asynchronous)
    {
        if (type is INamedTypeSymbol named
            && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            type = named.TypeArguments[0];
        }

        string name = asynchronous
            ? WellKnownMemberNames.DisposeAsyncMethodName
            : WellKnownMemberNames.DisposeMethodName;

        string disposableName = asynchronous ? AsyncDisposableTypeName : DisposableTypeName;

        if (context.Compilation.GetTypeByMetadataName(disposableName) is { } disposable
            && disposable.GetMembers(name).FirstOrDefault() is { } declared
            && type.FindImplementationForInterfaceMember(declared) is IMethodSymbol implementation)
        {
            return implementation;
        }

        foreach (ISymbol member in type.GetMembers(name))
        {
            if (member is IMethodSymbol { IsStatic: false, Parameters.Length: 0 } pattern)
                return pattern;
        }

        return null;
    }

    private const string DisposableTypeName = "System.IDisposable";

    private const string AsyncDisposableTypeName = "System.IAsyncDisposable";

    /// <summary>Follows the method a collection expression is built through.</summary>
    /// <remarks>
    /// That method is the <c>[CollectionBuilder]</c> builder for a builder type and the collection type's
    /// own constructor otherwise, so both kinds are dispatched here; an array, a span and a type parameter
    /// are built by the compiler itself and carry no method to follow.
    /// </remarks>
    private static void FollowCollectionConstruction(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        CollectionExpressionSyntax collection,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (model.GetOperation(collection, context.CancellationToken)
            is not ICollectionExpressionOperation { ConstructMethod: { } construct })
        {
            return;
        }

        if (construct.MethodKind == MethodKind.Constructor)
        {
            FollowConstructor(context, construct, collection, depth, walked, report);
            return;
        }

        FollowCall(
            context,
            construct,
            collection,
            RunsAStaticMethod(construct) ? "static method" : "method",
            depth,
            walked,
            report);
    }

    /// <summary>Follows the handler an interpolated string is filled through.</summary>
    /// <remarks>
    /// A string used as a string carries no handler the walk can read: the compiler fills it through
    /// <c>DefaultInterpolatedStringHandler</c> or <c>string.Concat</c>, neither of which has source here,
    /// so only an argument a handler declared in this compilation is asked for reaches a body at all. The
    /// binder has already chosen the constructor and every append off that type, so both are read off its
    /// answer rather than resolved a second time.
    /// </remarks>
    private static void FollowInterpolatedStringHandler(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        InterpolatedStringExpressionSyntax interpolated,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        IOperation? operation = model.GetOperation(interpolated, context.CancellationToken);

        // The handler creation, the conversion to it and the string it fills all carry this same syntax,
        // so which of the three the model answers with is not fixed.
        while (operation?.Parent is { } outer && outer.Syntax == interpolated)
            operation = outer;

        while (operation is IConversionOperation or IParenthesizedOperation)
        {
            operation = operation is IConversionOperation conversion
                ? conversion.Operand
                : ((IParenthesizedOperation)operation).Operand;
        }

        if (operation is not IInterpolatedStringHandlerCreationOperation creation)
            return;

        if (creation.HandlerCreation is IObjectCreationOperation { Constructor: { } constructor })
            FollowConstructor(context, constructor, interpolated, depth, walked, report);

        foreach (IOperation part in creation.Descendants())
        {
            if (part is not IInterpolatedStringAppendOperation
                { AppendCall: IInvocationOperation { TargetMethod: { } appended } })
            {
                continue;
            }

            FollowCall(
                context,
                appended,
                interpolated,
                RunsAStaticMethod(appended) ? "static method" : "method",
                depth,
                walked,
                report);
        }
    }

    /// <summary>Follows the query-pattern operators a query expression is rewritten into.</summary>
    /// <remarks>
    /// The binder has already applied every rule a query is allowed to pick its operators by - an instance
    /// method, an extension one, a generic one whose arguments it inferred - so each method is read off its
    /// answer rather than resolved a second time here. A clause the compiler removes rather than rewrites,
    /// the identity <c>select</c> of a query that has any other clause, answers with nothing and is
    /// followed nowhere, which is correct: no such call runs. A LINQ-to-objects query answers with
    /// <c>Enumerable</c>'s methods, which have no source here and stop the walk as any other such callee
    /// does.
    /// <para>
    /// An operator that runs on a receiver written in the query is re-resolved against the making of that
    /// receiver, on the same terms an instance call named outright is: a query over a local declared as a
    /// base and made as a derived runs the derived operator, and reading the base body would answer with
    /// one an override replaces. Two kinds of operator have such a receiver - the first of the chain the
    /// query builds on its own from expression, and the <c>Cast</c> an explicitly typed clause runs on the
    /// source that clause names, which for a second from or a join is that clause's own source and not the
    /// query's. Everything else runs on what the operator before it returned, which no creation here makes
    /// and no declaration names, so it is followed as bound.
    /// </para>
    /// </remarks>
    private static void FollowQuery(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        QueryExpressionSyntax query,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        // The receiver the chain has yet to run its first operator on. A Cast on the query's own from
        // expression is that operator, and everything after it runs on what the Cast handed back.
        ExpressionSyntax? chain = query.FromClause.Expression;

        // What the operator before this one handed back, which is the receiver of every operator the
        // source does not spell a receiver for.
        ITypeSymbol? previousResult = null;

        foreach ((SyntaxNode node, ISymbol? chosen, ExpressionSyntax? cast) in
                 GetQueryOperators(context, model, query))
        {
            if (chosen is not IMethodSymbol rewritten)
                continue;

            ExpressionSyntax? on;

            if (cast is not null)
            {
                on = cast;

                if (cast == query.FromClause.Expression)
                    chain = null;
            }
            else
            {
                on = chain;
                chain = null;
            }

            INamedTypeSymbol? made = on is null
                ? null
                : FollowHeldCreation(
                    context,
                    GetCreationHeldBy(context, model, on),
                    body,
                    node,
                    depth,
                    walked,
                    report);

            // An operator the chain runs on what the one before it handed back runs on exactly that type when
            // it is sealed, and may be an override that type declares.
            INamedTypeSymbol? exact = made
                ?? (on is null && previousResult is INamedTypeSymbol { IsSealed: true } narrowed ? narrowed : null);

            // An unsealed narrowed result still guarantees at least its own overrides; the report below says
            // that a type derived from it may replace them again.
            INamedTypeSymbol? dispatch = exact ?? (on is null ? previousResult as INamedTypeSymbol : null);
            IMethodSymbol runs = RunsAsMade(dispatch, rewritten);
            string kind = RunsAStaticMethod(rewritten) ? "static method" : "method";
            FollowCall(context, runs, node, kind, depth, walked, report);

            if (exact is null)
            {
                ITypeSymbol? receiver = on is null
                    ? previousResult
                    : model.GetTypeInfo(on, context.CancellationToken).Type;
                ReportOverridable(node, receiver, runs, kind, report);
            }

            // A Cast a second from or a join runs on its own source hands its result to that clause, not
            // to the chain the operators after it run on.
            if (cast is null || cast == query.FromClause.Expression)
                previousResult = runs.ReturnType;
        }
    }

    /// <summary>The operators <paramref name="query"/> runs, in the order it runs them.</summary>
    /// <remarks>
    /// A query is written in the order it is rewritten, so document order is that order: the from and its
    /// <c>Cast</c>, then each body clause, an <c>orderby</c> contributing one operator per ordering, then
    /// the select or group, then whatever an <c>into</c> continues with. A <c>Cast</c> is handed back with
    /// the source its own clause names, because a second from and a join run theirs on the sequence that
    /// clause introduces rather than on the one the chain has reached. A query written inside this one is
    /// left for the walk to reach as the expression it is, so that its own operators are resolved against
    /// its own sources.
    /// </remarks>
    private static IEnumerable<(SyntaxNode Node, ISymbol? Chosen, ExpressionSyntax? Cast)> GetQueryOperators(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        QueryExpressionSyntax query)
    {
        // The predicate is asked of the query itself before it is asked of anything inside it, so the
        // query has to answer for itself that its own clauses are to be read.
        foreach (SyntaxNode node in query.DescendantNodes(
            child => child == query || child is not QueryExpressionSyntax))
        {
            switch (node)
            {
                // A clause can carry two operators: naming the element type turns the source that clause
                // names into a Cast it runs ahead of the operator the clause itself is.
                case QueryClauseSyntax clause:
                    QueryClauseInfo chosen = model.GetQueryClauseInfo(clause, context.CancellationToken);
                    yield return (clause, chosen.CastInfo.Symbol, GetCastSource(clause));
                    yield return (clause, chosen.OperationInfo.Symbol, null);
                    break;

                // A select or a group names the value it produces rather than the Select or GroupBy
                // producing it, and an ordering names the key rather than the OrderBy or ThenBy it is
                // handed to.
                case SelectOrGroupClauseSyntax or OrderingSyntax:
                    yield return (node, model.GetSymbolInfo(node, context.CancellationToken).Symbol, null);
                    break;
            }
        }
    }

    /// <summary>The sequence a clause's <c>Cast</c> runs on, where the clause names one.</summary>
    /// <remarks>
    /// Only a from and a join can be explicitly typed, and each runs its <c>Cast</c> on the sequence it
    /// introduces. Any other clause carries no <c>Cast</c>, so none of them has a source to name.
    /// </remarks>
    private static ExpressionSyntax? GetCastSource(QueryClauseSyntax clause)
        => clause switch
        {
            FromClauseSyntax from => from.Expression,
            JoinClauseSyntax join => join.InExpression,
            _ => null,
        };

    /// <summary>Follows the members a <c>foreach</c> runs on the enumerator it makes.</summary>
    /// <remarks>
    /// The binder has already applied every rule the loop is allowed to pick a sequence apart by - a
    /// pattern <c>GetEnumerator</c>, an extension one, <see cref="IEnumerable{T}"/>, a <c>ref struct</c>
    /// enumerator, <c>await foreach</c> - so the members are read off its answer rather than resolved a
    /// second time here. An array, a string and a span answer with framework members that have no source,
    /// which <see cref="FollowCall"/> already stops at, so none of them needs a case of its own.
    /// </remarks>
    private static void FollowIteration(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        CommonForEachStatementSyntax loop,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        ForEachStatementInfo iteration = model.GetForEachStatementInfo(loop);
        ITypeSymbol? sequence = model.GetTypeInfo(loop.Expression, context.CancellationToken).Type;
        ITypeSymbol? enumerator = iteration.GetEnumeratorMethod?.ReturnType;

        // Only the sequence is a value the callback can be shown the making of; the enumerator is whatever
        // GetEnumerator handed back.
        INamedTypeSymbol? madeSequence = FollowHeldCreation(
            context,
            GetCreationHeldBy(context, model, loop.Expression),
            body,
            loop,
            depth,
            walked,
            report);

        IMethodSymbol? Follow(
            ITypeSymbol? receiver,
            INamedTypeSymbol? made,
            IMethodSymbol? member,
            string? kind = null,
            INamedTypeSymbol? dispatch = null)
        {
            // A member reached through an interface is resolved on the type the value was made as, which can
            // reimplement the interface and run a body the static type's mapping never names.
            ITypeSymbol? dispatchedOn = made is not null && member?.ContainingType is { TypeKind: TypeKind.Interface }
                ? made
                : receiver;
            if (RunsOn(dispatchedOn, member) is not { } bound)
                return null;

            IMethodSymbol run = RunsAsMade(made ?? dispatch, bound);
            kind ??= RunsAStaticMethod(run) ? "static method" : "method";
            FollowCall(context, run, loop, kind, depth, walked, report);

            if (made is null)
            {
                ReportOverridable(
                    loop,
                    receiver,
                    run,
                    kind,
                    report,
                    member?.ContainingType is { TypeKind: TypeKind.Interface });
            }

            return run;
        }

        // An override can narrow GetEnumerator's return type. A sealed one is exactly the enumerator the loop
        // then advances, whatever the binder's declaration returned.
        ITypeSymbol? advanced = Follow(sequence, madeSequence, iteration.GetEnumeratorMethod)?.ReturnType
                                ?? enumerator;
        INamedTypeSymbol? exactEnumerator =
            advanced is INamedTypeSymbol { IsSealed: true } narrowed
            && !SymbolEqualityComparer.Default.Equals(narrowed, enumerator)
                ? narrowed
                : null;
        // An unsealed narrowed enumerator still guarantees at least its own overrides.
        INamedTypeSymbol? narrowedEnumerator =
            exactEnumerator is null
            && advanced is INamedTypeSymbol unsealed
            && !SymbolEqualityComparer.Default.Equals(unsealed, enumerator)
                ? unsealed
                : null;
        IMethodSymbol? advance = Follow(
            advanced, exactEnumerator, iteration.MoveNextMethod, dispatch: narrowedEnumerator);
        Follow(advanced, exactEnumerator, iteration.CurrentProperty?.GetMethod, "property", narrowedEnumerator);
        IMethodSymbol? disposal = Follow(
            advanced, exactEnumerator, iteration.DisposeMethod, dispatch: narrowedEnumerator);

        // An await foreach awaits what MoveNextAsync and DisposeAsync hand back, with no await written.
        if (iteration.IsAsynchronous)
        {
            ReportImplicitAwait(context, model, advance?.ReturnType, loop, "an await foreach", report);
            ReportImplicitAwait(context, model, disposal?.ReturnType, loop, "an await foreach", report);
        }
    }

    /// <summary>The member that runs where the loop names one an interface declares.</summary>
    /// <remarks>
    /// A sequence reached through <see cref="IEnumerable{T}"/>, and an enumerator disposed through
    /// <see cref="IDisposable"/>, are named by the interface declaration, which has a body nowhere. The
    /// implementation is what runs, and asking the receiver for it is also the only spelling that finds an
    /// explicit one. Everything picked by pattern - a <c>ref struct</c> disposing itself, an extension
    /// <c>GetEnumerator</c> - already is the member that runs and is handed back unchanged.
    /// </remarks>
    private static IMethodSymbol? RunsOn(ITypeSymbol? receiver, IMethodSymbol? member)
    {
        if (member is null || receiver is null || member.ContainingType is not { TypeKind: TypeKind.Interface })
            return member;

        return receiver.FindImplementationForInterfaceMember(member) as IMethodSymbol ?? member;
    }

    /// <param name="value">
    /// The value being deconstructed where the source spells it, so a making it holds can pick the override
    /// that runs; null for a loop's elements and for the parts of a nested deconstruction.
    /// </param>
    private static void FollowDeconstruction(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        DeconstructionInfo deconstruction,
        ExpressionSyntax? value,
        ITypeSymbol? receiver,
        SyntaxNode node,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (deconstruction.Method is { } deconstruct)
        {
            INamedTypeSymbol? made = value is null
                ? null
                : FollowHeldCreation(
                    context,
                    GetCreationHeldBy(context, model, value),
                    body,
                    node,
                    depth,
                    walked,
                    report);
            IMethodSymbol runs = RunsAsMade(made, deconstruct);
            string kind = RunsAStaticMethod(runs) ? "static method" : "method";

            if (runs.IsImplicitlyDeclared)
                FollowGeneratedDeconstruct(context, runs, made, receiver, node, depth, walked, report);
            else
                FollowCall(context, runs, node, kind, depth, walked, report);

            if (made is null)
                ReportOverridable(node, receiver, runs, kind, report);
        }

        // Each nested part is deconstructed as the static type its slot hands out: a Deconstruct's out
        // parameter, or a tuple's element.
        ImmutableArray<ITypeSymbol> parts = deconstruction.Method is { } method
            ? method.Parameters.Skip(RunsAStaticMethod(method) && method.ReducedFrom is null ? 1 : 0)
                .Select(static parameter => parameter.Type)
                .ToImmutableArray()
            : receiver is INamedTypeSymbol { IsTupleType: true } tuple
                ? tuple.TupleElements.Select(static element => element.Type).ToImmutableArray()
                : ImmutableArray<ITypeSymbol>.Empty;

        for (int index = 0; index < deconstruction.Nested.Length; index++)
        {
            ITypeSymbol? part = parts.Length == deconstruction.Nested.Length ? parts[index] : null;
            FollowDeconstruction(
                context, model, body, deconstruction.Nested[index], null, part, node, depth, walked, report);
        }
    }
}
