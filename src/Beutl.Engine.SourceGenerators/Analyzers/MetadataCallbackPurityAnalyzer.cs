using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Beutl.Engine.SourceGenerators.Analyzers;

/// <summary>
/// Reports render metadata callbacks whose state can change without changing their plan identity.
/// </summary>
/// <remarks>
/// Plans are keyed by callback method identity. BESG003 permits only explicit state and the declaring
/// <c>RenderNode</c>, whose changes trigger re-recording. BESG004 separately reports unproven static state.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MetadataCallbackPurityAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> s_contractTypes = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Beutl.Graphics.Rendering.RenderBoundsContract",
        "Beutl.Graphics.Rendering.RenderHitTestContract",
        "Beutl.Graphics.Rendering.RenderScaleContract",
        "Beutl.Graphics.Rendering.RenderInputDemandContract",
        "Beutl.Graphics.Rendering.OpaqueRenderBoundsContract",
        "Beutl.Graphics.Rendering.TargetCaptureScaleContract",

        // ShaderBindingBuilder retains callbacks under the same identity rules as the factories below.
        "Beutl.Graphics.Shaders.ShaderBindingBuilder");

    private static readonly ImmutableHashSet<string> s_contractMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Beutl.Graphics.Rendering.RenderNodeContext.PaintedSource",
        "Beutl.Graphics.Rendering.OpaqueRenderDescription.Create",
        "Beutl.Graphics.Rendering.TargetScopeDescription.Create",
        "Beutl.Graphics.Rendering.TargetCommandDescription.Create",
        "Beutl.Graphics.Rendering.RawTargetScopeDescription.Create",
        "Beutl.Graphics.Rendering.RawTargetCommandDescription.Create",
        "Beutl.Graphics.Effects.GeometryDescription.Create");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            DiagnosticDescriptors.CapturingMetadataCallback,
            DiagnosticDescriptors.StaticStateMetadataCallback);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method)
        {
            return;
        }

        // Type arguments do not affect whether a containing type is a registered contract.
        if (method.ContainingType is not { } containingType)
            return;

        string typeName = containingType.ContainingNamespace.ToDisplayString() + "." + containingType.Name;
        if (!s_contractTypes.Contains(typeName)
            && !s_contractMethods.Contains(typeName + "." + method.Name))
        {
            return;
        }

        foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
        {
            if (!IsDelegateArgument(context, argument))
                continue;

            Location location = argument.GetLocation();
            (ExpressionSyntax callback, SemanticModel model, string? unresolved) =
                ResolveCallback(context, Unwrap(argument.Expression));

            if ((unresolved ?? DescribeImpurity(context, model, callback)) is { } reason)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.CapturingMetadataCallback,
                    location,
                    containingType.Name,
                    method.Name,
                    reason));
            }

            ReportUnprovenStaticStateReads(context, model, containingType, method, callback, location);
        }
    }

    private static (ExpressionSyntax Callback, SemanticModel Model, string? Unresolved) ResolveCallback(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax expression)
    {
        SemanticModel model = context.SemanticModel;
        HashSet<ISymbol> visited = new(SymbolEqualityComparer.Default);

        while (expression is IdentifierNameSyntax or MemberAccessExpressionSyntax)
        {
            ISymbol? symbol = model.GetSymbolInfo(expression, context.CancellationToken).Symbol;
            ExpressionSyntax? source;

            switch (symbol)
            {
                case IFieldSymbol { IsReadOnly: true } field:
                    if (!visited.Add(field))
                        return (expression, model, CyclicCallback);

                    source = GetFieldInitializer(context, field);
                    if (source is null)
                        return (expression, model, DescribeUnreadableField(field));
                    break;

                case IPropertySymbol property:
                    if (property.SetMethod is not null)
                    {
                        return (expression, model, "the callback comes from a property with a setter, so "
                            + "the delegate the call sees is whatever was last assigned to it");
                    }

                    if (!visited.Add(property))
                        return (expression, model, CyclicCallback);

                    source = GetGetterResult(context, property);
                    if (source is null)
                        return (expression, model, DescribeUnreadableProperty(property));
                    break;

                default:
                    return (expression, model, null);
            }

            model = GetSemanticModel(context, source.SyntaxTree);
            expression = Unwrap(source);
        }

        return (expression, model, null);
    }

    private const string CyclicCallback =
        "the callback comes from a member that resolves back to itself, so what delegate it ends up "
        + "holding cannot be determined";

    private static ExpressionSyntax? GetFieldInitializer(
        SyntaxNodeAnalysisContext context,
        IFieldSymbol field)
    {
        foreach (SyntaxReference declaration in field.DeclaringSyntaxReferences)
        {
            if (declaration.GetSyntax(context.CancellationToken)
                is VariableDeclaratorSyntax { Initializer.Value: { } value })
            {
                return value;
            }
        }

        return null;
    }

    private static ExpressionSyntax? GetGetterResult(
        SyntaxNodeAnalysisContext context,
        IPropertySymbol property)
    {
        foreach (SyntaxReference declaration in property.DeclaringSyntaxReferences)
        {
            if (declaration.GetSyntax(context.CancellationToken) is PropertyDeclarationSyntax syntax
                && GetInvariantCandidate(syntax) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private static string DescribeUnreadableField(IFieldSymbol field)
        => field.DeclaringSyntaxReferences.IsEmpty
            ? "the callback comes from a readonly field compiled into another assembly, so what its "
              + "initialiser gave the delegate cannot be seen, and readonly only fixes the reference"
            : "the callback comes from a readonly field with no initialiser, so the delegate is built in a "
              + "constructor, where it can close over that constructor's arguments";

    private static string DescribeUnreadableProperty(IPropertySymbol property)
        => property.DeclaringSyntaxReferences.IsEmpty
            ? "the callback comes from a get-only property compiled into another assembly, so what its "
              + "getter returns cannot be seen, and having no setter says only that this declaration does "
              + "not write it"
            : "the callback comes from a get-only property whose getter is not a single returned expression "
              + "this rule can read, so which delegate it hands back cannot be determined";

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    break;

                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    break;

                case CheckedExpressionSyntax @checked:
                    expression = @checked.Expression;
                    break;

                case PostfixUnaryExpressionSyntax suppression
                    when suppression.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = suppression.Operand;
                    break;

                case BinaryExpressionSyntax cast when cast.IsKind(SyntaxKind.AsExpression):
                    expression = cast.Left;
                    break;

                default:
                    return expression;
            }
        }
    }

    private static bool IsDelegateArgument(SyntaxNodeAnalysisContext context, ArgumentSyntax argument)
        => context.SemanticModel.GetTypeInfo(argument.Expression, context.CancellationToken).ConvertedType
            is { TypeKind: TypeKind.Delegate };

    private static string? DescribeImpurity(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ExpressionSyntax expression)
    {
        switch (expression)
        {
            case AnonymousFunctionExpressionSyntax lambda:
                // A static lambda cannot reach a local, a parameter, or this; the compiler says so.
                return lambda.Modifiers.Any(SyntaxKind.StaticKeyword)
                    ? null
                    : DescribeCaptureImpurity(context, model, lambda);

            case IdentifierNameSyntax or MemberAccessExpressionSyntax:
                {
                    ISymbol? symbol = model.GetSymbolInfo(expression, context.CancellationToken).Symbol;
                    return symbol switch
                    {
                        IMethodSymbol method => DescribeReceiverImpurity(context, model, method, expression),

                        // A forwarded callback is not checked anywhere else: the caller passes it to this
                        // helper, not to a contract, so nothing there is analyzed. The forwarder is the
                        // last place that knows a contract is involved.
                        IParameterSymbol =>
                            "the callback arrives as a parameter, so what the caller closed over is not "
                            + "visible here and the caller's own call is not a contract call",
                        ILocalSymbol =>
                            "the callback comes from a local, so what it closed over is not visible here",
                        IFieldSymbol { IsReadOnly: false } =>
                            "the callback comes from a field that can be assigned later",
                        _ => null,
                    };
                }

            // A callback that is neither delegate-typed state nor a delegate at all carries nothing to
            // read and no identity to key a plan by, so there is nothing for either rule to say.
            case LiteralExpressionSyntax literal
                when literal.IsKind(SyntaxKind.NullLiteralExpression)
                    || literal.IsKind(SyntaxKind.DefaultLiteralExpression):
            case DefaultExpressionSyntax:
                return null;

            // Reporting an unhandled shape rather than accepting it is what keeps silence meaningful: an
            // author reads no diagnostic as "the rule looked at this", not as "the rule ran out of cases".
            default:
                return $"the callback is written as a {expression.Kind()}, which this rule cannot trace to "
                    + "a delegate it can classify, and an unclassified callback is reported rather than "
                    + "assumed stable";
        }
    }

    private const string RenderNodeTypeName = "Beutl.Graphics.Rendering.RenderNode";

    private static string? DescribeCaptureImpurity(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        AnonymousFunctionExpressionSyntax lambda)
    {
        IOperation? operation = model.GetOperation(lambda, context.CancellationToken);

        // The conversion to the delegate type shares the lambda's syntax, so what comes back can be the
        // conversion rather than the function underneath it.
        while (operation is IDelegateCreationOperation or IConversionOperation or IParenthesizedOperation)
        {
            operation = operation switch
            {
                IDelegateCreationOperation creation => creation.Target,
                IConversionOperation conversion => conversion.Operand,
                _ => ((IParenthesizedOperation)operation).Operand,
            };
        }

        if (operation is not IAnonymousFunctionOperation function)
        {
            // Silence has to mean the rule looked, and here it could not.
            return "the lambda is not declared static and this rule could not read what it closed over, "
                + "so it is reported rather than assumed to close over nothing";
        }

        ITypeSymbol? enclosingInstance = null;
        foreach (IOperation node in function.Descendants())
        {
            switch (node)
            {
                case ILocalReferenceOperation local when IsDeclaredOutside(local.Local, lambda):
                    return $"the lambda closes over the local '{local.Local.Name}', which can be assigned "
                        + "after this call, so one plan compiles for the first answer and is replayed for "
                        + "the second";

                case IParameterReferenceOperation parameter
                    when IsDeclaredOutside(parameter.Parameter, lambda):
                    return $"the lambda closes over the parameter '{parameter.Parameter.Name}', which the "
                        + "caller decides per call, so one plan compiles for the first answer and is "
                        + "replayed for the second";

                case IInstanceReferenceOperation
                {
                    ReferenceKind: InstanceReferenceKind.ContainingTypeInstance, Type: { } instance
                }:
                    enclosingInstance = instance;
                    break;
            }
        }

        if (enclosingInstance is null || IsRenderNode(enclosingInstance))
            return null;

        return $"the lambda reads the enclosing '{enclosingInstance.Name}', which is not a RenderNode: "
            + "change marking and the recorded-answer cross-check are a node's, so nothing holds what this "
            + "reads to one answer";
    }

    private static bool IsDeclaredOutside(ISymbol symbol, AnonymousFunctionExpressionSyntax lambda)
    {
        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree == lambda.SyntaxTree && lambda.Span.Contains(reference.Span))
                return false;
        }

        return true;
    }

    private static bool IsRenderNode(ITypeSymbol type)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.ContainingNamespace?.ToDisplayString() + "." + current.Name == RenderNodeTypeName)
                return true;
        }

        return false;
    }

    private static string? DescribeReceiverImpurity(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        IMethodSymbol method,
        ExpressionSyntax expression)
    {
        if (method.IsStatic)
            return null;

        if (expression is not MemberAccessExpressionSyntax memberAccess)
        {
            // A bare name is an instance method on `this` or a local function, and only the first has a
            // receiver at all. A local function not declared static reaches the scope it is written in the
            // way a lambda does, and nothing here reads which locals it took, so it keeps the answer the
            // closure walk gives an unreadable lambda.
            return method.MethodKind == MethodKind.LocalFunction
                ? "the callback is a local function that is not declared static, so it can read a local or "
                  + "a parameter of the method it is written in and this rule does not read which"
                : DescribeEnclosingReceiverImpurity(context, model, expression);
        }

        // Parentheses and nothing else: a cast is what changes the member the call binds to, so stripping
        // one would read a receiver the call was never bound against.
        if (StripParentheses(memberAccess.Expression) is ThisExpressionSyntax or BaseExpressionSyntax)
            return DescribeEnclosingReceiverImpurity(context, model, expression);

        ITypeSymbol? receiver = model
            .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        return receiver is { IsValueType: true }
            ? "the callback is an instance method on a value type, so the delegate carries a boxed copy of "
              + "whatever the receiver held at this call"
            : "the callback is an instance method on a reference type, and the delegate keeps that "
              + "object as its receiver";
    }

    private static string? DescribeEnclosingReceiverImpurity(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ExpressionSyntax expression)
    {
        ITypeSymbol? enclosingInstance = model
            .GetEnclosingSymbol(expression.SpanStart, context.CancellationToken)?.ContainingType;

        if (enclosingInstance is null)
        {
            // Silence has to mean the rule looked, and here it could not.
            return "the callback is an instance method and this rule could not read what type it is "
                + "written inside, so it is reported rather than assumed to be a node's";
        }

        // A RenderNode is a class, so this decides nothing the node test would have decided otherwise; it
        // is here to name what actually happens to a struct's `this`.
        if (enclosingInstance.IsValueType)
        {
            return "the callback is an instance method on a value type, so the delegate carries a boxed "
                + "copy of whatever the receiver held at this call";
        }

        if (IsRenderNode(enclosingInstance))
            return null;

        return $"the callback is an instance method of the enclosing '{enclosingInstance.Name}', which is "
            + "not a RenderNode: change marking and the recorded-answer cross-check are a node's, so "
            + "nothing holds what its receiver reads to one answer";
    }

    private static void ReportUnprovenStaticStateReads(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        INamedTypeSymbol containingType,
        IMethodSymbol method,
        ExpressionSyntax callback,
        Location callSite)
    {
        void Report(SyntaxNode node, string kind, ISymbol symbol, string reason)
        {
            // A callback reached through a readonly field, and any body the walk follows into, can be
            // written in another file, and a syntax node action may only report where it was asked to
            // look. The call is where the author chose this callback, so it is the location that survives.
            Location location = node.SyntaxTree == context.Node.SyntaxTree
                ? node.GetLocation()
                : callSite;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.StaticStateMetadataCallback,
                location,
                containingType.Name,
                method.Name,
                kind,
                symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                reason));
        }

        HashSet<ISymbol> walked = new(SymbolEqualityComparer.Default);

        if (callback is AnonymousFunctionExpressionSyntax { Body: { } lambdaBody })
        {
            WalkBody(context, model, lambdaBody, MaxCallbackCallDepth, walked, Report);
            return;
        }

        // Every other shape is BESG003's to explain: a local, a parameter, a settable field, a member this
        // rule could not follow. Naming it again here would say the same thing under a second id.
        if (callback is not (IdentifierNameSyntax or MemberAccessExpressionSyntax)
            || model.GetSymbolInfo(callback, context.CancellationToken).Symbol is not IMethodSymbol group)
        {
            return;
        }

        walked.Add((group.ReducedFrom ?? group).OriginalDefinition);

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
        HashSet<ISymbol> walked,
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
                    GetAccessExpression(name),
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
                && GetRunAccessor(staticEvent, GetAccessExpression(name)) is { } accessor)
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
        switch (child)
        {
            // A local function is reached only by its own name, so a name written for it somewhere else in
            // the body is the whole of what can run it.
            case LocalFunctionStatementSyntax function:
                return model.GetDeclaredSymbol(function, context.CancellationToken) is not { } declared
                       || IsNamedOutside(context, model, body, declared, function);

            case AnonymousFunctionExpressionSyntax lambda:
                return RunsLambda(context, model, body, lambda);

            // A call the build removes takes the whole expression with it, arguments and receiver included,
            // so nothing written inside one runs - which is why the question is asked here, where it also
            // covers what the arguments read, rather than where the callee's own body is followed.
            case InvocationExpressionSyntax invocation:
                return model.GetSymbolInfo(invocation, context.CancellationToken).Symbol
                           is not IMethodSymbol called
                       || ConditionalCompilation.IsCallCompiled(
                           context.Compilation,
                           called,
                           invocation.SyntaxTree);

            default:
                return true;
        }
    }

    private static bool RunsLambda(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        AnonymousFunctionExpressionSyntax lambda)
    {
        ExpressionSyntax stored = lambda;
        while (stored.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax)
            stored = (ExpressionSyntax)stored.Parent;

        if (stored.Parent is AssignmentExpressionSyntax assignment
            && assignment.Right == stored
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && model.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol is IDiscardSymbol)
        {
            return false;
        }

        if (stored.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
            && model.GetDeclaredSymbol(declarator, context.CancellationToken) is ILocalSymbol local)
        {
            return IsNamedOutside(context, model, body, local, lambda);
        }

        return true;
    }

    private static bool IsNamedOutside(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        ISymbol symbol,
        SyntaxNode declaration)
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
                    model.GetSymbolInfo(name, context.CancellationToken).Symbol,
                    symbol))
            {
                return true;
            }
        }

        return false;
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

    private static void FollowUnnamedInvocation(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        SyntaxNode node,
        int depth,
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        // An indexer is spelled as brackets around an argument, so the name loop never sees it, and the
        // accessor it runs is a body like any other.
        if (node is ElementAccessExpressionSyntax element)
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
                        RunsAStaticMethod(add) ? "static method" : "method",
                        depth,
                        walked,
                        report);
                }
            }

            return;
        }

        if (node is WithExpressionSyntax with)
        {
            FollowWithExpression(context, model, with, depth, walked, report);
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
                model.GetDeconstructionInfo(deconstruction),
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
            FollowIteration(context, model, loop, depth, walked, report);

            if (loop is ForEachVariableStatementSyntax deconstructing)
            {
                FollowDeconstruction(
                    context,
                    model.GetDeconstructionInfo(deconstructing),
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
            FollowDisposal(context, model, node, depth, walked, report);
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

        // A query names none of the operators it runs: the compiler picks Where, Select, OrderBy and the
        // rest off the source's own type and calls them in the order the clauses are written.
        if (node is QueryClauseSyntax or SelectOrGroupClauseSyntax or OrderingSyntax)
        {
            FollowQueryOperators(context, model, node, depth, walked, report);
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

    private static void FollowWithExpression(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        WithExpressionSyntax with,
        int depth,
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (model.GetTypeInfo(with.Expression, context.CancellationToken).Type
            is INamedTypeSymbol { TypeKind: TypeKind.Class } copied
            && GetCopyConstructor(copied) is { } copy)
        {
            FollowCall(context, copy, with, "constructor", depth, walked, report);
        }

        foreach (ExpressionSyntax assigned in with.Initializer.Expressions)
        {
            if (assigned is AssignmentExpressionSyntax { Left: { } target }
                && model.GetSymbolInfo(target, context.CancellationToken).Symbol
                    is IPropertySymbol { IsStatic: false, SetMethod: { } setter })
            {
                FollowCall(context, setter, target, "property", depth, walked, report);
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
        SyntaxNode scope,
        int depth,
        HashSet<ISymbol> walked,
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

        void Follow(ITypeSymbol? type)
        {
            if (type is not null && GetDisposeMethod(context, type, asynchronous) is { } dispose)
                FollowCall(context, dispose, scope, "method", depth, walked, report);
        }

        if (declaration is not null)
        {
            foreach (VariableDeclaratorSyntax declarator in declaration.Variables)
            {
                if (model.GetDeclaredSymbol(declarator, context.CancellationToken) is ILocalSymbol declared)
                    Follow(declared.Type);
            }
        }

        if (resource is not null)
            Follow(model.GetTypeInfo(resource, context.CancellationToken).Type);
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
        HashSet<ISymbol> walked,
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
        HashSet<ISymbol> walked,
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

    /// <summary>Follows the query-pattern operator a clause runs.</summary>
    /// <remarks>
    /// The binder has already chosen each operator off the source it is written over, so it is read off
    /// that answer rather than resolved a second time. An <c>orderby</c> carries nothing itself - each
    /// ordering under it carries its own <c>OrderBy</c> or <c>ThenBy</c> - and a range variable written
    /// with a type adds the <c>Cast</c> that gives it that type. A query over a framework sequence
    /// resolves to <c>Enumerable</c> or <c>Queryable</c>, which have no source here and stop at
    /// <see cref="FollowCall"/>.
    /// </remarks>
    private static void FollowQueryOperators(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode clause,
        int depth,
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        void Follow(ISymbol? symbol)
        {
            if (symbol is not IMethodSymbol chosen)
                return;

            FollowCall(
                context,
                chosen,
                clause,
                RunsAStaticMethod(chosen) ? "static method" : "method",
                depth,
                walked,
                report);
        }

        switch (clause)
        {
            case QueryClauseSyntax query:
                QueryClauseInfo written = model.GetQueryClauseInfo(query, context.CancellationToken);
                Follow(written.CastInfo.Symbol);
                Follow(written.OperationInfo.Symbol);
                break;

            case SelectOrGroupClauseSyntax selectOrGroup:
                Follow(model.GetSymbolInfo(selectOrGroup, context.CancellationToken).Symbol);
                break;

            case OrderingSyntax ordering:
                Follow(model.GetSymbolInfo(ordering, context.CancellationToken).Symbol);
                break;
        }
    }

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
        CommonForEachStatementSyntax loop,
        int depth,
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        ForEachStatementInfo iteration = model.GetForEachStatementInfo(loop);
        ITypeSymbol? sequence = model.GetTypeInfo(loop.Expression, context.CancellationToken).Type;
        ITypeSymbol? enumerator = iteration.GetEnumeratorMethod?.ReturnType;

        void Follow(ITypeSymbol? receiver, IMethodSymbol? member, string? kind = null)
        {
            if (RunsOn(receiver, member) is not { } run)
                return;

            FollowCall(
                context,
                run,
                loop,
                kind ?? (RunsAStaticMethod(run) ? "static method" : "method"),
                depth,
                walked,
                report);
        }

        Follow(sequence, iteration.GetEnumeratorMethod);
        Follow(enumerator, iteration.MoveNextMethod);
        Follow(enumerator, iteration.CurrentProperty?.GetMethod, "property");
        Follow(enumerator, iteration.DisposeMethod);
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

    private static void FollowDeconstruction(
        SyntaxNodeAnalysisContext context,
        DeconstructionInfo deconstruction,
        SyntaxNode node,
        int depth,
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (deconstruction.Method is { } deconstruct)
        {
            FollowCall(
                context,
                deconstruct,
                node,
                RunsAStaticMethod(deconstruct) ? "static method" : "method",
                depth,
                walked,
                report);
        }

        foreach (DeconstructionInfo nested in deconstruction.Nested)
            FollowDeconstruction(context, nested, node, depth, walked, report);
    }

    private static void FollowImplicitConversion(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ExpressionSyntax expression,
        int depth,
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (model.GetConversion(expression, context.CancellationToken)
            is { IsUserDefined: true, MethodSymbol: { } method })
        {
            FollowCall(context, method, expression, "static method", depth, walked, report);
        }
    }

    private static void FollowPropertyAccess(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        IPropertySymbol property,
        SyntaxNode reference,
        ExpressionSyntax access,
        int depth,
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (FollowReceiverCreation(context, model, body, reference, depth, walked, report)
            is not { } made)
        {
            return;
        }

        if (RunsGetter(access) && property.GetMethod is { } getter)
            FollowCall(context, RunsAsMade(made, getter), reference, "property", depth, walked, report);

        if (RunsSetter(access) && property.SetMethod is { } setter)
            FollowCall(context, RunsAsMade(made, setter), reference, "property", depth, walked, report);
    }

    /// <summary>The type the receiver was made as, or null where its making cannot be read.</summary>
    private static INamedTypeSymbol? FollowReceiverCreation(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        SyntaxNode reference,
        int depth,
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (GetReceiverCreation(context, model, reference) is not { } made)
            return null;

        if (RunsWith(body, made.Creation)
            && made.Model.GetSymbolInfo(made.Creation, context.CancellationToken).Symbol
                is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            FollowConstructor(context, constructor, reference, depth, walked, report);
        }

        return made.Made;
    }

    /// <summary>The member an instance made as <paramref name="made"/> runs.</summary>
    /// <remarks>
    /// A receiver whose making is readable carries an instance of exactly one type for its whole life, so
    /// a virtual member reached through a base declaration, and an interface member reached through the
    /// interface, both run a body the declaration the call bound to does not name - and that body, not the
    /// declaration, is what the callback answers with. A member nothing overrides, and one on a receiver
    /// whose making was not read, are already what runs and are handed back unchanged.
    /// </remarks>
    private static IMethodSymbol RunsAsMade(INamedTypeSymbol? made, IMethodSymbol member)
    {
        if (made is null)
            return member;

        if (member.ContainingType is { TypeKind: TypeKind.Interface })
            return RunsOn(made, member) ?? member;

        if (!member.IsVirtual && !member.IsAbstract && !member.IsOverride)
            return member;

        IMethodSymbol declaration = member.OriginalDefinition;

        for (INamedTypeSymbol? current = made; current is not null; current = current.BaseType)
        {
            foreach (ISymbol candidate in current.GetMembers(member.Name))
            {
                if (candidate is IMethodSymbol overriding && Overrides(overriding, declaration))
                    return overriding;
            }
        }

        return member;
    }

    private static bool Overrides(IMethodSymbol candidate, IMethodSymbol declaration)
    {
        for (IMethodSymbol? current = candidate; current is not null; current = current.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, declaration))
                return true;
        }

        return false;
    }

    private static bool RunsWith(SyntaxNode body, ExpressionSyntax expression)
        => expression.SyntaxTree == body.SyntaxTree && body.Span.Contains(expression.Span);

    private static (ExpressionSyntax Creation, SemanticModel Model, INamedTypeSymbol Made)?
        GetReceiverCreation(
            SyntaxNodeAnalysisContext context,
            SemanticModel model,
            SyntaxNode reference)
    {
        if (GetReceiver(reference) is not { } receiver)
            return null;

        ExpressionSyntax expression = StripParentheses(receiver);
        (ExpressionSyntax Creation, SemanticModel Model)? made =
            expression is BaseObjectCreationExpressionSyntax written
                ? (written, model)
                : GetHeldCreation(context, model, expression);

        if (made is not { } creation
            || creation.Model.GetTypeInfo(creation.Creation, context.CancellationToken).Type
                is not INamedTypeSymbol created)
        {
            return null;
        }

        return IsHeldBy(created, model.GetTypeInfo(receiver, context.CancellationToken).Type)
            ? (creation.Creation, creation.Model, created)
            : null;
    }

    /// <summary>Whether an instance made as <paramref name="created"/> is what the receiver holds.</summary>
    /// <remarks>
    /// A making that reaches a receiver of a base type or of an interface it implements is the instance
    /// that receiver holds, and the derived body is what a call on it runs. One that reaches a receiver of
    /// an unrelated type got there through a user-defined conversion, which hands back something else
    /// entirely and says nothing about what the receiver ends up holding.
    /// </remarks>
    private static bool IsHeldBy(INamedTypeSymbol created, ITypeSymbol? receiver)
    {
        if (receiver is null)
            return false;

        for (INamedTypeSymbol? current = created; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, receiver))
                return true;
        }

        foreach (INamedTypeSymbol implemented in created.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented, receiver))
                return true;
        }

        return false;
    }

    private static (ExpressionSyntax Creation, SemanticModel Model)? GetHeldCreation(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ExpressionSyntax expression)
    {
        if (expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
            return null;

        ISymbol? symbol = model.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        ExpressionSyntax? initializer = symbol switch
        {
            IFieldSymbol field when HoldsOneCreation(context, model, expression, field)
                => GetFieldInitializer(context, field),

            ILocalSymbol local => GetUnreassignedLocalInitializer(context, local),

            IPropertySymbol { SetMethod: null } property
                when ReachesMemberOnItsOwn(context, model, expression, property)
                => GetAliasedFieldInitializer(context, property),

            // A parameter, a settable field, a property that computes, a method group: each is a receiver
            // whose making this rule was not shown, and the walk stops at every one of them.
            _ => null,
        };

        if (initializer is null)
            return null;

        SemanticModel initializerModel = GetSemanticModel(context, initializer.SyntaxTree);
        ExpressionSyntax value = StripParentheses(initializer);
        return value is BaseObjectCreationExpressionSyntax ? (value, initializerModel) : null;
    }

    private static bool HoldsOneCreation(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ExpressionSyntax reference,
        IFieldSymbol field)
        => field.IsReadOnly
           && ReachesMemberOnItsOwn(context, model, reference, field)
           && !IsWrittenInAConstructor(context, field);

    private static ExpressionSyntax? GetAliasedFieldInitializer(
        SyntaxNodeAnalysisContext context,
        IPropertySymbol property)
    {
        foreach (SyntaxReference declaration in property.DeclaringSyntaxReferences)
        {
            if (declaration.GetSyntax(context.CancellationToken) is not PropertyDeclarationSyntax syntax
                || GetInvariantCandidate(syntax) is not { } candidate)
            {
                continue;
            }

            ExpressionSyntax named = StripParentheses(candidate);
            if (named is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
                continue;

            SemanticModel getterModel = GetSemanticModel(context, named.SyntaxTree);
            if (getterModel.GetSymbolInfo(named, context.CancellationToken).Symbol is IFieldSymbol field
                && HoldsOneCreation(context, getterModel, named, field))
            {
                return GetFieldInitializer(context, field);
            }
        }

        return null;
    }

    private static bool ReachesMemberOnItsOwn(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ExpressionSyntax reference,
        ISymbol member)
    {
        if (reference is not MemberAccessExpressionSyntax access)
            return true;

        ExpressionSyntax qualifier = StripParentheses(access.Expression);

        return member.IsStatic
            ? model.GetSymbolInfo(qualifier, context.CancellationToken).Symbol is ITypeSymbol
            : qualifier is ThisExpressionSyntax;
    }

    private static bool IsWrittenInAConstructor(SyntaxNodeAnalysisContext context, IFieldSymbol field)
    {
        INamedTypeSymbol type = field.OriginalDefinition.ContainingType;

        foreach (IMethodSymbol constructor in type.InstanceConstructors.Concat(type.StaticConstructors))
        {
            foreach (SyntaxReference declaration in constructor.OriginalDefinition.DeclaringSyntaxReferences)
            {
                if (declaration.GetSyntax(context.CancellationToken) is ConstructorDeclarationSyntax syntax
                    && IsWrittenWithin(context, syntax, field))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ExpressionSyntax? GetUnreassignedLocalInitializer(
        SyntaxNodeAnalysisContext context,
        ILocalSymbol local)
    {
        foreach (SyntaxReference declaration in local.DeclaringSyntaxReferences)
        {
            if (declaration.GetSyntax(context.CancellationToken)
                is not VariableDeclaratorSyntax { Initializer.Value: { } value } declarator)
            {
                continue;
            }

            SyntaxNode scope = declarator.FirstAncestorOrSelf<MemberDeclarationSyntax>()
                ?? declarator.SyntaxTree.GetRoot(context.CancellationToken);

            if (!IsWrittenWithin(context, scope, local))
                return value;
        }

        return null;
    }

    private static bool IsWrittenWithin(SyntaxNodeAnalysisContext context, SyntaxNode scope, ISymbol symbol)
    {
        SemanticModel model = GetSemanticModel(context, scope.SyntaxTree);
        ISymbol declared = symbol.OriginalDefinition;

        foreach (SyntaxNode node in scope.DescendantNodes())
        {
            foreach (ExpressionSyntax written in GetWriteTargets(node))
            {
                ISymbol? target = model.GetSymbolInfo(written, context.CancellationToken).Symbol;
                if (SymbolEqualityComparer.Default.Equals(target?.OriginalDefinition, declared))
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<ExpressionSyntax> GetWriteTargets(SyntaxNode node)
    {
        switch (node)
        {
            // A deconstruction writes the elements of the tuple on its left and not the tuple itself.
            case AssignmentExpressionSyntax { Left: TupleExpressionSyntax tuple }:
                foreach (ArgumentSyntax element in tuple.Arguments)
                    yield return element.Expression;

                break;

            case AssignmentExpressionSyntax assignment:
                yield return assignment.Left;

                break;

            case ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None):
                yield return argument.Expression;

                break;

            case PrefixUnaryExpressionSyntax prefix
                when prefix.IsKind(SyntaxKind.PreIncrementExpression)
                    || prefix.IsKind(SyntaxKind.PreDecrementExpression):
                yield return prefix.Operand;

                break;

            case PostfixUnaryExpressionSyntax postfix
                when postfix.IsKind(SyntaxKind.PostIncrementExpression)
                    || postfix.IsKind(SyntaxKind.PostDecrementExpression):
                yield return postfix.Operand;

                break;
        }
    }

    private static ExpressionSyntax? GetReceiver(SyntaxNode reference) => reference switch
    {
        SimpleNameSyntax name when name.Parent is MemberAccessExpressionSyntax access && access.Name == name
            => access.Expression,
        SimpleNameSyntax name when name.Parent is MemberBindingExpressionSyntax binding && binding.Name == name
            => ConditionalAccessSyntax.FindReceiver(binding),
        // An object initializer writes its member with no receiver beside it, because the receiver is the
        // object the initializer belongs to. A member of a nested initializer has no such spelling: the
        // object it writes into is whatever the outer member handed back, which is not a making.
        SimpleNameSyntax name when name.Parent is AssignmentExpressionSyntax
        {
            Parent: InitializerExpressionSyntax { Parent: BaseObjectCreationExpressionSyntax made },
        } member && member.Left == name
            => made,
        // A property pattern names its member with no receiver beside it either, because the receiver is
        // whatever the pattern is matched against, which the is or the switch spells beside it.
        SimpleNameSyntax name when name.Parent is BaseExpressionColonSyntax
        {
            Parent: SubpatternSyntax subpattern,
        }
            => GetMatchedExpression(subpattern),
        ElementAccessExpressionSyntax element => element.Expression,
        _ => null,
    };

    /// <summary>The expression the pattern a subpattern belongs to is matched against.</summary>
    /// <remarks>
    /// An is, a switch expression and a switch statement each spell what they match beside the pattern,
    /// and the combinators between hand that same value down unchanged. A subpattern of a nested property
    /// pattern reads off what the outer member returned, and one of a positional or list pattern reads off
    /// an element a Deconstruct or an indexer produced; none of those is a making, so none is answered.
    /// </remarks>
    private static ExpressionSyntax? GetMatchedExpression(SubpatternSyntax subpattern)
    {
        SyntaxNode? matched = subpattern.Parent;

        while (matched is PropertyPatternClauseSyntax or RecursivePatternSyntax
               or ParenthesizedPatternSyntax or BinaryPatternSyntax or UnaryPatternSyntax)
        {
            matched = matched.Parent;
        }

        return matched switch
        {
            IsPatternExpressionSyntax match => match.Expression,
            SwitchExpressionArmSyntax { Parent: SwitchExpressionSyntax chosen } => chosen.GoverningExpression,
            CasePatternSwitchLabelSyntax { Parent.Parent: SwitchStatementSyntax statement }
                => statement.Expression,
            _ => null,
        };
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression;
    }

    private static ExpressionSyntax GetAccessExpression(SimpleNameSyntax name)
        => name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access : name;

    private static bool RunsGetter(ExpressionSyntax access)
        => access.Parent is not AssignmentExpressionSyntax assignment
            || assignment.Left != access
            || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            || WritesThroughTheMember(assignment);

    private static bool RunsSetter(ExpressionSyntax access) => access.Parent switch
    {
        AssignmentExpressionSyntax assignment
            => assignment.Left == access && !WritesThroughTheMember(assignment),
        PrefixUnaryExpressionSyntax prefix => prefix.IsKind(SyntaxKind.PreIncrementExpression)
            || prefix.IsKind(SyntaxKind.PreDecrementExpression),
        PostfixUnaryExpressionSyntax postfix => postfix.IsKind(SyntaxKind.PostIncrementExpression)
            || postfix.IsKind(SyntaxKind.PostDecrementExpression),
        _ => false,
    };

    /// <summary>
    /// Whether an assignment writes into what its left side hands back rather than replacing it.
    /// </summary>
    /// <remarks>
    /// A nested initializer - <c>new A { B = { C = 1 } }</c> - is written as an assignment but sets
    /// nothing: it reads <c>B</c> and writes into the object that read returns. It is the one assignment
    /// whose left side runs the getter, and the only place an initializer body appears on the right.
    /// </remarks>
    private static bool WritesThroughTheMember(AssignmentExpressionSyntax assignment)
        => assignment.Right is InitializerExpressionSyntax;

    private static void FollowCall(
        SyntaxNodeAnalysisContext context,
        IMethodSymbol called,
        SyntaxNode node,
        string kind,
        int depth,
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        // Keyed on the method that declares the body rather than on the symbol the call site bound to, so
        // an extension reached in both its spellings is walked - and reported - once.
        if (!walked.Add((called.ReducedFrom ?? called).OriginalDefinition))
            return;

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
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (!walked.Add(constructor.OriginalDefinition))
            return;

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

    private static (string Kind, string Reason)? DescribeUnprovenStaticState(
        SyntaxNodeAnalysisContext context,
        ISymbol? symbol)
    {
        return symbol switch
        {
            // A const can only ever hold a primitive, a string, an enum member, or null, and the value is
            // burned into every read of it.
            IFieldSymbol { IsStatic: true, IsConst: true } => null,

            IFieldSymbol { IsStatic: true, IsReadOnly: false } =>
                ("static field", "a static field that is neither const nor readonly can be assigned "
                    + "between two recordings while the plan key stays the same"),

            IFieldSymbol { IsStatic: true } field when !IsImmutableType(field.Type) =>
                ("static field", DescribeUnprovableFieldType(field.Type)),

            IPropertySymbol { IsStatic: true } property => DescribeUnprovenGetter(context, property),

            // An event whose accessors this rule can read is routed to those accessors before this is
            // asked, so only these two reach here. Metadata says nothing about which of the two an event
            // declared elsewhere is, and reading its unread accessors as storing nothing would clear every
            // static event in every referenced assembly.
            IEventSymbol { IsStatic: true, DeclaringSyntaxReferences.IsEmpty: true } =>
                ("static event", "its accessors have no source in this compilation, so what a subscription "
                    + "does with the handler cannot be seen, and an event that stores it holds a subscriber "
                    + "list any += or -= anywhere rewrites"),

            // A field-like event is the delegate field its subscriber list lives in, and += and -= are that
            // field's assignments. Reading the list back is legal only inside the declaring type - where a
            // callback written beside the event, and any helper the walk follows into it, both sit - while
            // writing it binds from anywhere, so the one case covers both spellings.
            IEventSymbol { IsStatic: true } =>
                ("static event", "a field-like static event is the delegate field its subscriber list lives "
                    + "in, and any += or -= anywhere rewrites that list, so what the callback reads off it, "
                    + "or does to it, can differ between two recordings while the plan key stays the same"),

            _ => null,
        };
    }

    private static string DescribeUnprovableFieldType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Class, IsSealed: true } named
            && !HasCompleteFieldList(named)
            && HasOnlyImmutableFields(named, MaxImmutableFieldDepth))
        {
            return "readonly stops the field being assigned and not the value it holds being mutated, and "
                + "this field's type is a class declared outside this compilation, which imports its public "
                + "and protected members and not the private state behind them, so the fields visible here "
                + "are a floor and not the type, and nothing shows whether its instances hold something "
                + "writable";
        }

        return "readonly stops the field being assigned and not the value it holds being mutated, and this "
            + "field's type is not one whose instances this rule can prove carry no writable state, so what "
            + "the callback reads through it can change between two recordings while the plan key stays the "
            + "same";
    }

    private static (string Kind, string Reason)? DescribeUnprovenGetter(
        SyntaxNodeAnalysisContext context,
        IPropertySymbol property)
    {
        if (property.SetMethod is not null)
            return ("static property", "its setter can change what it answers");

        ImmutableArray<SyntaxReference> declarations = property.DeclaringSyntaxReferences;
        if (declarations.IsEmpty)
        {
            return ("static property", "its getter has no source in this compilation, so what the getter "
                + "reads cannot be seen, and having no setter is not on its own evidence that it answers "
                + "the same way twice");
        }

        foreach (SyntaxReference declaration in declarations)
        {
            if (declaration.GetSyntax(context.CancellationToken) is PropertyDeclarationSyntax syntax
                && GetInvariantCandidate(syntax) is { } value
                && IsProvenConstant(context, value))
            {
                return null;
            }
        }

        return ("static property", "its getter is not a shape this rule can prove yields the same value "
            + "on every read, and having no setter is not on its own evidence that it does");
    }

    private static ExpressionSyntax? GetInvariantCandidate(PropertyDeclarationSyntax syntax)
    {
        if (syntax.ExpressionBody is { } propertyBody)
            return propertyBody.Expression;

        AccessorDeclarationSyntax? getter = syntax.AccessorList?.Accessors
            .FirstOrDefault(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
        if (getter is null)
            return null;

        if (getter.ExpressionBody is { } getterBody)
            return getterBody.Expression;

        if (getter.Body is { Statements.Count: 1 } block
            && block.Statements[0] is ReturnStatementSyntax { Expression: { } returned })
        {
            return returned;
        }

        // An auto-implemented get-only getter reads a backing field only the initialiser and the static
        // constructor can write, so the initialiser is the whole of what the getter can be shown to answer.
        return getter.Body is null ? syntax.Initializer?.Value : null;
    }

    private static bool IsProvenConstant(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {
        SemanticModel model = GetSemanticModel(context, expression.SyntaxTree);

        // Covers a literal, a const, an enum member, and any expression the compiler folds out of them.
        if (model.GetConstantValue(expression, context.CancellationToken).HasValue)
            return true;

        ExpressionSyntax value = Unwrap(expression);

        // default on a struct is not a constant value to the compiler and is still the same value each read.
        if (value is DefaultExpressionSyntax
            || (value is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.DefaultLiteralExpression)))
        {
            return true;
        }

        return model.GetSymbolInfo(value, context.CancellationToken).Symbol
                is IFieldSymbol { IsStatic: true, IsReadOnly: true } field
            && IsImmutableType(field.Type);
    }

    private static bool IsImmutableType(ITypeSymbol type) => IsImmutableType(type, MaxImmutableFieldDepth);

    private const int MaxImmutableFieldDepth = 8;

    private static bool IsImmutableType(ITypeSymbol type, int depth) => type switch
    {
        { TypeKind: TypeKind.Enum } => true,

        // The framework types with no mutable state at all, decided before the walk below because the walk
        // has nothing to tell about them: every primitive carries an instance of itself, string's own
        // fields are not readonly, and IntPtr carries a pointer at memory the type says nothing about.
        {
            SpecialType: SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_SByte
                or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64
                or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double
                or SpecialType.System_Decimal or SpecialType.System_String or SpecialType.System_IntPtr
                or SpecialType.System_UIntPtr or SpecialType.System_DateTime
        } => true,

        // The engine's own resource address, which the BESG004 message tells authors to move a
        // per-recording value onto. The slot is an identity and nothing else - it is sealed and neither it
        // nor its base declares an instance field, which RenderResourceSlotStateTests pins - and outside
        // Beutl.Engine it is a metadata class the walk below is not allowed to read. Leaving it to the walk
        // would have the rule rejecting the fix it recommends, which is the state authors suppress a rule
        // over. The abstract base is deliberately not named here: it is a base the engine derives a stateful
        // slot from, so only the sealed one is an address and nothing else.
        INamedTypeSymbol { Name: "RenderResourceSlot", IsSealed: true } slot
            when slot.ContainingNamespace.ToDisplayString() == "Beutl.Graphics.Rendering" => true,

        // A readonly struct has no instance member that can write it and a sealed class has no derived
        // instance that can add one, so in both the declared fields are the whole of what an instance
        // carries - which is what makes the walk mean anything, once the fields can be read at all.
        // Holding a reference is not itself an answer, so the fields are put the same question their type
        // just was.
        INamedTypeSymbol named when named is { IsValueType: true, IsReadOnly: true }
                or { TypeKind: TypeKind.Class, IsSealed: true } =>
            depth > 0 && HasCompleteFieldList(named) && HasOnlyImmutableFields(named, depth),

        // Anything else is reported. A struct without the modifier can be written through an instance
        // method; an unsealed class is a base a subclass can add state to; a delegate carries a target this
        // rule can no more read than the static method it already says it cannot follow. A pointer, a type
        // parameter, an array, and a type that failed to bind land here too, rather than in the walk, where
        // having no fields to read would pass for having no state.
        _ => false,
    };

    private static bool HasCompleteFieldList(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (!IsFieldListReadable(current))
                return false;
        }

        return true;
    }

    private static bool IsFieldListReadable(INamedTypeSymbol type)
        => type.IsValueType
            || type.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType
                or SpecialType.System_Enum
            || !type.DeclaringSyntaxReferences.IsEmpty;

    private static bool HasOnlyImmutableFields(INamedTypeSymbol type, int depth)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (ISymbol member in current.GetMembers())
            {
                // A field-like event is a delegate field that is not readonly, written by the compiler
                // and left out of a source type's member list, which carries the event and its
                // accessors in its place. Asking about fields alone therefore let a type whose whole
                // mutable state is an event pass for one carrying none, while the same state spelled
                // as a plain delegate field failed. An event declared with its own accessors stores
                // nothing of itself, and whatever those accessors do write is a field this loop sees.
                if (member is IEventSymbol { IsStatic: false, AddMethod.IsImplicitlyDeclared: true })
                    return false;

                // An auto-property's backing field is implicitly declared and is still part of the value,
                // so every instance field counts regardless of how it was written.
                if (member is not IFieldSymbol { IsStatic: false } field)
                    continue;

                if (!field.IsReadOnly || !IsImmutableType(field.Type, depth - 1))
                    return false;
            }
        }

        return true;
    }

    private static SemanticModel GetSemanticModel(SyntaxNodeAnalysisContext context, SyntaxTree tree)
    {
        if (tree == context.SemanticModel.SyntaxTree)
            return context.SemanticModel;

#pragma warning disable RS1030
        return context.SemanticModel.Compilation.GetSemanticModel(tree);
#pragma warning restore RS1030
    }

    private static bool IsInsideNameOf(SyntaxNode node)
    {
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" } })
                return true;

            if (current is AnonymousFunctionExpressionSyntax or MemberDeclarationSyntax
                or LocalFunctionStatementSyntax)
            {
                return false;
            }
        }

        return false;
    }
}
