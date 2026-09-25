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
    private static readonly ImmutableArray<string> s_contractTypeNames = ImmutableArray.Create(
        "Beutl.Graphics.Rendering.RenderBoundsContract",
        "Beutl.Graphics.Rendering.RenderHitTestContract",
        "Beutl.Graphics.Rendering.RenderScaleContract",
        "Beutl.Graphics.Rendering.RenderInputDemandContract",
        "Beutl.Graphics.Rendering.OpaqueRenderBoundsContract",
        "Beutl.Graphics.Rendering.TargetCaptureScaleContract",

        // ShaderBindingBuilder retains callbacks under the same identity rules as the factories below.
        "Beutl.Graphics.Shaders.ShaderBindingBuilder");

    private static readonly ImmutableArray<(string Type, string Method)> s_contractMethodNames =
        ImmutableArray.Create(
            ("Beutl.Graphics.Rendering.RenderNodeContext", "PaintedSource"),
            ("Beutl.Graphics.Rendering.OpaqueRenderDescription", "Create"),
            ("Beutl.Graphics.Rendering.TargetScopeDescription", "Create"),
            ("Beutl.Graphics.Rendering.TargetCommandDescription", "Create"),
            ("Beutl.Graphics.Rendering.RawTargetScopeDescription", "Create"),
            ("Beutl.Graphics.Rendering.RawTargetCommandDescription", "Create"),
            ("Beutl.Graphics.Effects.GeometryDescription", "Create"));

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            DiagnosticDescriptors.CapturingMetadataCallback,
            DiagnosticDescriptors.StaticStateMetadataCallback);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // The contracts are resolved once per compilation rather than per invocation: every invocation in
        // the compilation reaches this rule, and only the handful naming a contract goes any further, so
        // the filter has to cost a symbol comparison rather than a name built out of the symbol.
        context.RegisterCompilationStartAction(start =>
        {
            if (Contracts.Resolve(start.Compilation) is not { } contracts)
                return;

            start.RegisterSyntaxNodeAction(
                node => AnalyzeInvocation(node, contracts),
                SyntaxKind.InvocationExpression);
        });
    }

    /// <summary>The contract types and factory methods of one compilation, as symbols.</summary>
    /// <remarks>
    /// A contract not in the compilation is left out rather than recorded as missing, so a compilation
    /// with none of them at all resolves to nothing and the rule registers no action.
    /// </remarks>
    private sealed class Contracts
    {
        private readonly ImmutableHashSet<INamedTypeSymbol> _types;
        private readonly ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<string>> _methods;

        private Contracts(
            ImmutableHashSet<INamedTypeSymbol> types,
            ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<string>> methods)
        {
            _types = types;
            _methods = methods;
        }

        public static Contracts? Resolve(Compilation compilation)
        {
            ImmutableHashSet<INamedTypeSymbol>.Builder types =
                ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);

            foreach (string name in s_contractTypeNames)
            {
                if (compilation.GetTypeByMetadataName(name) is { } type)
                    types.Add(type);
            }

            ImmutableDictionary<INamedTypeSymbol, ImmutableHashSet<string>>.Builder methods =
                ImmutableDictionary.CreateBuilder<INamedTypeSymbol, ImmutableHashSet<string>>(
                    SymbolEqualityComparer.Default);

            foreach ((string typeName, string methodName) in s_contractMethodNames)
            {
                if (compilation.GetTypeByMetadataName(typeName) is not { } type)
                    continue;

                methods[type] = methods.TryGetValue(type, out ImmutableHashSet<string>? names)
                    ? names.Add(methodName)
                    : ImmutableHashSet.Create(StringComparer.Ordinal, methodName);
            }

            if (types.Count == 0 && methods.Count == 0)
                return null;

            return new Contracts(types.ToImmutable(), methods.ToImmutable());
        }

        /// <summary>Whether <paramref name="method"/> is one this rule reads the callbacks of.</summary>
        /// <remarks>
        /// Asked of the unbound type, because type arguments do not affect whether a containing type is a
        /// registered contract.
        /// </remarks>
        public bool Declares(IMethodSymbol method, INamedTypeSymbol containingType)
        {
            INamedTypeSymbol declaration = containingType.OriginalDefinition;

            return _types.Contains(declaration)
                   || (_methods.TryGetValue(declaration, out ImmutableHashSet<string>? names)
                       && names.Contains(method.Name));
        }
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context, Contracts contracts)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method)
        {
            return;
        }

        if (method.ContainingType is not { } containingType
            || !contracts.Declares(method, containingType))
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
                    if (IsWrittenInAConstructor(context, field))
                        return (expression, model, "the callback field is reassigned in a constructor, so its initializer does not describe the callback used here");
                    if (!visited.Add(field))
                        return (expression, model, CyclicCallback);

                    source = GetFieldInitializer(context, field);
                    if (source is null)
                        return (expression, model, DescribeUnreadableField(field));
                    break;

                case IPropertySymbol property:
                    if (IsWrittenInAConstructor(context, property))
                        return (expression, model, "the callback property is assigned in a constructor, so its initializer does not describe the callback used here");
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
                case IInvocationOperation { TargetMethod: { MethodKind: MethodKind.LocalFunction, IsStatic: false } target }
                    when IsDeclaredOutside(target, lambda):
                    return $"the lambda calls the non-static local function '{target.Name}', which can capture mutable state";

                case ILocalReferenceOperation local
                    when !local.Local.HasConstantValue && IsDeclaredOutside(local.Local, lambda):
                    return $"the lambda closes over the local '{local.Local.Name}', which can be assigned "
                        + "after this call, so one plan compiles for the first answer and is replayed for "
                        + "the second";

                case IParameterReferenceOperation parameter
                    when IsDeclaredOutside(parameter.Parameter, lambda)
                         && !IsNodePrimaryConstructorParameter(context, parameter.Parameter):
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

    private static bool IsNodePrimaryConstructorParameter(SyntaxNodeAnalysisContext context, IParameterSymbol parameter)
        => parameter.ContainingType is { } type && IsRenderNode(type)
           && parameter.DeclaringSyntaxReferences.Any(reference =>
               reference.GetSyntax(context.CancellationToken) is ParameterSyntax { Parent.Parent: TypeDeclarationSyntax });

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
        void Report(ISymbol? member, string construct)
        {
            // A compiler-generated member, such as a positional record's Deconstruct, has no body anyone wrote.
            if (member is not null && !member.IsImplicitlyDeclared && IsDeclaredInSource(member))
                report(node, DescribeMemberKind(member), member, string.Format(NotFollowedWithoutAName, construct));
        }

        switch (node)
        {
            case ListPatternSyntax list
                when model.GetOperation(list, context.CancellationToken) is IListPatternOperation pattern:
                Report(pattern.LengthSymbol, "a list pattern");

                // Only an element pattern reads through the indexer; [] and [..] test the length alone.
                if (list.Patterns.Any(static element => element is not SlicePatternSyntax))
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
        IMethodSymbol method when RunsAStaticMethod(method) => "static method",
        _ => "method",
    };

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

    private static void FollowWithExpression(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        WithExpressionSyntax with,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (model.GetTypeInfo(with.Expression, context.CancellationToken).Type
            is INamedTypeSymbol { TypeKind: TypeKind.Class } copied)
        {
            // A record copies itself through a virtual clone, so the copy constructor that runs is the one
            // of the type the value was made as.
            INamedTypeSymbol? made = FollowHeldCreation(
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

            IMethodSymbol runs = RunsAsMade(made, dispose);
            FollowCall(context, runs, scope, "method", depth, walked, report);

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

            IMethodSymbol runs = RunsAsMade(made, rewritten);
            string kind = RunsAStaticMethod(rewritten) ? "static method" : "method";
            FollowCall(context, runs, node, kind, depth, walked, report);

            if (made is null)
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

        IMethodSymbol? Follow(ITypeSymbol? receiver, INamedTypeSymbol? made, IMethodSymbol? member, string? kind = null)
        {
            if (RunsOn(receiver, member) is not { } bound)
                return null;

            IMethodSymbol run = RunsAsMade(made, bound);
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
        Follow(advanced, exactEnumerator, iteration.MoveNextMethod);
        Follow(advanced, exactEnumerator, iteration.CurrentProperty?.GetMethod, "property");
        Follow(advanced, exactEnumerator, iteration.DisposeMethod);
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

    private static void FollowPropertyAccess(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        IPropertySymbol property,
        SyntaxNode reference,
        ExpressionSyntax access,
        int depth,
        Dictionary<ISymbol, int> walked,
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
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
        => FollowHeldCreation(
            context,
            GetReceiverCreation(context, model, reference),
            body,
            reference,
            depth,
            walked,
            report);

    /// <summary>The type <paramref name="held"/> was made as, following the constructor that made it.</summary>
    private static INamedTypeSymbol? FollowHeldCreation(
        SyntaxNodeAnalysisContext context,
        (ExpressionSyntax Creation, SemanticModel Model, INamedTypeSymbol Made)? held,
        SyntaxNode body,
        SyntaxNode reference,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (held is not { } made)
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
        => GetReceiver(reference) is { } receiver ? GetCreationHeldBy(context, model, receiver) : null;

    /// <summary>The creation the instance <paramref name="receiver"/> holds was made by.</summary>
    private static (ExpressionSyntax Creation, SemanticModel Model, INamedTypeSymbol Made)?
        GetCreationHeldBy(
            SyntaxNodeAnalysisContext context,
            SemanticModel model,
            ExpressionSyntax receiver)
    {
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

    private static bool IsWrittenInAConstructor(SyntaxNodeAnalysisContext context, ISymbol field)
    {
        INamedTypeSymbol type = field.OriginalDefinition.ContainingType!;

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
        ImplicitElementAccessSyntax element when element.Parent is AssignmentExpressionSyntax
        {
            Parent: InitializerExpressionSyntax { Parent: BaseObjectCreationExpressionSyntax made },
        } assignment && assignment.Left == element
            => made,
        ElementAccessExpressionSyntax element => element.Expression,
        _ => null,
    };

    /// <summary>The expression the pattern <paramref name="pattern"/> belongs to is matched against.</summary>
    /// <remarks>
    /// An is, a switch expression and a switch statement each spell what they match beside the pattern,
    /// and the combinators between hand that same value down unchanged. A subpattern of a nested property
    /// pattern reads off what the outer member returned rather than off a making, so it is not answered.
    /// </remarks>
    private static ExpressionSyntax? GetMatchedExpression(SyntaxNode pattern)
    {
        SyntaxNode? matched = pattern.Parent;

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
        // per-recording value onto. The slot is an identity and nothing else, and outside Beutl.Engine it is
        // a metadata class the walk below is not allowed to read. Leaving it to the walk would have the rule
        // rejecting the fix it recommends, which is the state authors suppress a rule over.
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
