using Beutl.Engine.SourceGenerators.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Beutl.Engine.SourceGenerators.Analyzers;

public sealed partial class MetadataCallbackPurityAnalyzer
{
    private static void ReportUnprovenStaticStateReads(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        INamedTypeSymbol containingType,
        IMethodSymbol method,
        ExpressionSyntax callback,
        Location callSite)
    {
        Dictionary<ISymbol, int> walked = new(SymbolEqualityComparer.Default);
        var depthReports = new List<(SyntaxNode Node, string Kind, ISymbol Symbol)>();
        var reported = new HashSet<(SyntaxTree? Tree, Microsoft.CodeAnalysis.Text.TextSpan Span, string Symbol, string Reason)>();

        void Emit(SyntaxNode node, string kind, ISymbol symbol, string reason)
        {
            Location location = node.SyntaxTree == context.Node.SyntaxTree ? node.GetLocation() : callSite;
            string display = symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
            if (!reported.Add((location.SourceTree, location.SourceSpan, display, reason))) return;
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.StaticStateMetadataCallback, location,
                containingType.Name, method.Name, kind, display, reason));
        }

        void Report(SyntaxNode node, string kind, ISymbol symbol, string reason)
        {
            if (reason == DeeperThanTheWalk)
                depthReports.Add((node, kind, symbol));
            else
                Emit(node, kind, symbol, reason);
        }

        void CompleteDepthReports()
        {
            foreach (var report in depthReports)
                if (!walked.TryGetValue(report.Symbol.OriginalDefinition, out int depth) || depth <= 0)
                    Emit(report.Node, report.Kind, report.Symbol, DeeperThanTheWalk);
        }

        if (callback is AnonymousFunctionExpressionSyntax { Body: { } lambdaBody })
        {
            WalkBody(context, model, lambdaBody, MaxCallbackCallDepth, walked, Report);
            CompleteDepthReports();
            return;
        }

        // Every other shape is BESG003's to explain: a local, a parameter, a settable field, a member this
        // rule could not follow. Naming it again here would say the same thing under a second id.
        if (callback is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)
            || model.GetSymbolInfo(callback, context.CancellationToken).Symbol is not IMethodSymbol group)
        {
            return;
        }

        walked[(group.ReducedFrom ?? group).OriginalDefinition] = MaxCallbackCallDepth;

        if (GetBody(context, group) is not { } groupBody)
        {
            Report(callback, "method", group, UnreadableCallbackBody);
            return;
        }

        WalkBody(
            context,
            GetSemanticModel(context, groupBody.SyntaxTree),
            groupBody,
            MaxCallbackCallDepth,
            walked,
            Report);
        CompleteDepthReports();
    }

    private const string UnreadableCallbackBody =
        "the callback is a method whose body has no source in this compilation, so nothing it reads can be "
        + "seen, and being static says only that the delegate is the same one every frame, not that it "
        + "answers the same way; declare the method where this rule can read it, or write the callback as a "
        + "static lambda at the call site";

    private const int MaxCallbackCallDepth = 8;

    private const string DeeperThanTheWalk =
        "the callback reaches it through a chain of calls longer than this rule walks, so what the rest of "
        + "that chain reads was never looked at, and a call chain nobody can follow to its end is not "
        + "evidence that the callback answers the same way twice";

    private static void WalkBody(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        foreach (SyntaxNode node in body.DescendantNodesAndSelf(
            child => Runs(context, model, body, child)))
        {
            // A user-defined implicit conversion is spelled nowhere at all - it is implied by the type the
            // expression is used as - so it is asked for rather than found.
            if (node is ExpressionSyntax converted)
                FollowImplicitConversion(context, model, converted, depth, walked, report);

            if (node is not SimpleNameSyntax name)
            {
                FollowUnnamedInvocation(context, model, body, node, depth, walked, report);
                continue;
            }

            // A nameof argument names a member without reading it.
            if (IsInsideNameOf(name))
                continue;

            ISymbol? symbol = model.GetSymbolInfo(name, context.CancellationToken).Symbol;

            // A static method, or one called on an instance whose creation this rule can point at. Only
            // these three kinds, because an operator, a conversion and a constructor reach the walk
            // through FollowUnnamedInvocation instead.
            if (symbol is IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary or MethodKind.LocalFunction
                        or MethodKind.ReducedExtension
                } called)
            {
                bool runsStatic = RunsAStaticMethod(called);

                // A static method has no receiver to read, so the follow is not even asked for.
                INamedTypeSymbol? made = runsStatic
                    ? null
                    : FollowReceiverCreation(context, model, body, name, depth, walked, report);

                if (runsStatic || made is not null)
                {
                    FollowCall(
                        context,
                        RunsAsMade(made, called),
                        name,
                        runsStatic ? "static method" : "method",
                        depth,
                        walked,
                        report);
                }

                continue;
            }

            // A static property is the branch below, which asks the stricter question of whether the value
            // is the same on every read rather than only what the getter names.
            if (symbol is IPropertySymbol { IsStatic: false } instanceProperty)
            {
                FollowPropertyAccess(
                    context,
                    model,
                    body,
                    instanceProperty,
                    name,
                    MemberAccessSyntax.GetAccessExpression(name),
                    depth,
                    walked,
                    report);
                continue;
            }

            // An event written with its own accessors keeps nothing of itself: what += and -= do with the
            // handler is in the accessor body, which is read there, exactly as a property is read through
            // the accessor a reference runs. A field-like event has no body anywhere and is the subscriber
            // list itself, which the branch below reports.
            if (symbol is IEventSymbol { IsStatic: true } staticEvent
                && GetRunAccessor(staticEvent, MemberAccessSyntax.GetAccessExpression(name)) is { } accessor)
            {
                FollowCall(context, accessor, name, "event", depth, walked, report);
                continue;
            }

            if (DescribeUnprovenStaticState(context, symbol) is not (string kind, string reason))
                continue;

            report(name, kind, symbol!, reason);
        }
    }

    /// <summary>Whether what is written inside <paramref name="child"/> runs when the body does.</summary>
    private static bool Runs(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        SyntaxNode child)
    {
        // A call the build removes takes the whole expression with it, arguments and receiver included,
        // so nothing written inside one runs - which is why the question is asked here, where it also
        // covers what the arguments read, rather than where the callee's own body is followed.
        if (child is InvocationExpressionSyntax invocation)
        {
            return model.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                       is not IMethodSymbol called
                   || ConditionalCompilation.IsCallCompiled(
                       context.Compilation,
                       called,
                       invocation.SyntaxTree);
        }

        return NestedFunctionSyntax.Runs(model, body, child, context.CancellationToken);
    }

    private static IMethodSymbol? GetRunAccessor(IEventSymbol @event, ExpressionSyntax access)
    {
        if (@event.AddMethod is not { IsImplicitlyDeclared: false }
            || @event.DeclaringSyntaxReferences.Length == 0
            || access.Parent is not AssignmentExpressionSyntax assignment
            || assignment.Left != access)
        {
            return null;
        }

        if (assignment.IsKind(SyntaxKind.AddAssignmentExpression))
            return @event.AddMethod;

        return assignment.IsKind(SyntaxKind.SubtractAssignmentExpression) ? @event.RemoveMethod : null;
    }

    private static bool RunsAStaticMethod(IMethodSymbol method)
        => method.IsStatic || method.ReducedFrom is { IsStatic: true };

    private static void FollowCall(
        SyntaxNodeAnalysisContext context,
        IMethodSymbol called,
        SyntaxNode node,
        string kind,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        // Keyed on the method that declares the body rather than on the symbol the call site bound to, so
        // an extension reached in both its spellings is walked - and reported - once.
        ISymbol declaration = (called.ReducedFrom ?? called).OriginalDefinition;
        if (walked.TryGetValue(declaration, out int previousDepth) && previousDepth >= depth) return;
        walked[declaration] = depth;

        if (GetBody(context, called) is not { } body)
            return;

        if (depth == 0)
        {
            report(node, kind, called, DeeperThanTheWalk);
            return;
        }

        WalkBody(context, GetSemanticModel(context, body.SyntaxTree), body, depth - 1, walked, report);
    }

    private static void FollowConstructor(
        SyntaxNodeAnalysisContext context,
        IMethodSymbol constructor,
        SyntaxNode node,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (walked.TryGetValue(constructor.OriginalDefinition, out int previousDepth) && previousDepth >= depth) return;
        walked[constructor.OriginalDefinition] = depth;

        List<SyntaxNode> bodies = GetConstructorBodies(context, constructor);
        IMethodSymbol? implicitBase = GetImplicitBaseConstructor(context, constructor);

        if (bodies.Count == 0 && implicitBase is null)
            return;

        if (depth == 0)
        {
            report(node, "constructor", constructor, DeeperThanTheWalk);
            return;
        }

        foreach (SyntaxNode body in bodies)
            WalkBody(context, GetSemanticModel(context, body.SyntaxTree), body, depth - 1, walked, report);

        if (implicitBase is not null)
            FollowConstructor(context, implicitBase, node, depth - 1, walked, report);
    }

    private static List<SyntaxNode> GetConstructorBodies(
        SyntaxNodeAnalysisContext context,
        IMethodSymbol constructor)
    {
        List<SyntaxNode> bodies = [];
        bool chainsToThis = false;

        foreach (SyntaxReference declaration in constructor.OriginalDefinition.DeclaringSyntaxReferences)
        {
            switch (declaration.GetSyntax(context.CancellationToken))
            {
                case ConstructorDeclarationSyntax syntax:
                    if (syntax.Initializer is { } initializer)
                    {
                        bodies.Add(initializer);
                        chainsToThis |= initializer.IsKind(SyntaxKind.ThisConstructorInitializer);
                    }

                    if (syntax.Body is { } block)
                        bodies.Add(block);
                    else if (syntax.ExpressionBody?.Expression is { } expression)
                        bodies.Add(expression);
                    break;

                case TypeDeclarationSyntax { BaseList.Types: { } baseTypes }:
                    foreach (BaseTypeSyntax baseType in baseTypes)
                    {
                        if (baseType is PrimaryConstructorBaseTypeSyntax primaryBase)
                            bodies.Add(primaryBase);
                    }

                    break;
            }
        }

        // A constructor that chains to another of the same type leaves the initialisers to that one, and
        // adding them here would walk the same expression twice.
        if (!chainsToThis)
            AddInstanceInitializers(context, constructor.OriginalDefinition.ContainingType, bodies);

        return bodies;
    }

    private static void AddInstanceInitializers(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol? type,
        List<SyntaxNode> bodies)
    {
        if (type is null)
            return;

        foreach (ISymbol member in type.GetMembers())
        {
            if (member is not (IFieldSymbol { IsStatic: false, IsImplicitlyDeclared: false }
                or IPropertySymbol { IsStatic: false, IsImplicitlyDeclared: false }))
            {
                continue;
            }

            foreach (SyntaxReference declaration in member.DeclaringSyntaxReferences)
            {
                switch (declaration.GetSyntax(context.CancellationToken))
                {
                    case VariableDeclaratorSyntax { Initializer.Value: { } field }:
                        bodies.Add(field);
                        break;

                    case PropertyDeclarationSyntax { Initializer.Value: { } property }:
                        bodies.Add(property);
                        break;
                }
            }
        }
    }

    private static IMethodSymbol? GetImplicitBaseConstructor(
        SyntaxNodeAnalysisContext context,
        IMethodSymbol constructor)
    {
        foreach (SyntaxReference declaration in constructor.OriginalDefinition.DeclaringSyntaxReferences)
        {
            switch (declaration.GetSyntax(context.CancellationToken))
            {
                case ConstructorDeclarationSyntax { Initializer: not null }:
                    return null;

                case TypeDeclarationSyntax { BaseList.Types: { } baseTypes }
                    when baseTypes.Any(static type => type is PrimaryConstructorBaseTypeSyntax):
                    return null;
            }
        }

        if (constructor.OriginalDefinition.ContainingType?.BaseType is not { } baseType
            || baseType.DeclaringSyntaxReferences.IsEmpty)
        {
            return null;
        }

        return baseType.InstanceConstructors
            .FirstOrDefault(static candidate => candidate.Parameters.Length == 0);
    }

    private static SyntaxNode? GetBody(SyntaxNodeAnalysisContext context, IMethodSymbol method)
    {
        // An extension method called in reduced form and a constructed generic both carry the declaration
        // of the method they came from, and a partial method's body is on the implementing part.
        IMethodSymbol declared = (method.ReducedFrom ?? method).OriginalDefinition;
        declared = declared.PartialImplementationPart ?? declared;

        foreach (SyntaxReference declaration in declared.DeclaringSyntaxReferences)
        {
            switch (declaration.GetSyntax(context.CancellationToken))
            {
                // An operator and a conversion are declared by their own node kinds rather than as methods,
                // and their bodies are as much a body as any. A constructor is a base method declaration too
                // and never arrives here: it is followed through GetConstructorBodies, which reads the
                // initialisers this would miss.
                case BaseMethodDeclarationSyntax { Body: { } block }:
                    return block;

                case BaseMethodDeclarationSyntax { ExpressionBody.Expression: { } expression }:
                    return expression;

                case LocalFunctionStatementSyntax { Body: { } block }:
                    return block;

                case LocalFunctionStatementSyntax { ExpressionBody.Expression: { } expression }:
                    return expression;

                // An accessor is a body too. An expression-bodied property declares its getter as the arrow
                // clause itself rather than as an accessor, and an auto-property's accessor has no body at
                // all - which is the no-source answer, and the right one: what such a getter hands back was
                // put there by a constructor or an initialiser, which the constructor walk reads.
                case AccessorDeclarationSyntax { Body: { } block }:
                    return block;

                case AccessorDeclarationSyntax { ExpressionBody.Expression: { } expression }:
                    return expression;

                case ArrowExpressionClauseSyntax { Expression: { } expression }:
                    return expression;
            }
        }

        return null;
    }
}
