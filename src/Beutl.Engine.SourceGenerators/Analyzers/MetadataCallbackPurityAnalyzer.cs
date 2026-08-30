using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Beutl.Engine.SourceGenerators.Analyzers;

/// <summary>
/// Reports a callback passed to a render metadata contract that reads more than the render node declaring it.
/// </summary>
/// <remarks>
/// <para>
/// These callbacks are evaluated repeatedly - forward bounds, backward region of interest, scale
/// reevaluation, hit testing, cache lookup - and the compiled plan is keyed by which method the callback is,
/// not by what the callback closed over. A callback that reads a captured local therefore lets one plan key
/// stand for two different answers, and the second recording replays a plan compiled for the first.
/// </para>
/// <para>
/// The engine used to walk the delegate's closure at recording time to catch this. Recording is the render
/// path, so that walk is gone; this says the same thing at compile time, before the frame that would have
/// paid for it.
/// </para>
/// <para>
/// One reader is admitted: a callback may read the <c>RenderNode</c> it is written inside. That node arrives
/// as the delegate's own target rather than as a closure field, marking it changed re-records it, and an
/// answer of the node's that moves between recording and graph-wide metadata resolution fails the request at
/// the recorded-answer cross-check. A local, a parameter, and an enclosing instance that is not a node have
/// none of that: nothing re-records when one is assigned, and the runtime identity validator never sees
/// them, because a closure over anything besides <see langword="this"/> arrives as a compiler display class
/// that none of its type tests answer for. This rule is the whole of what stands there.
/// </para>
/// <para>
/// The exemption is about which instance the callback reads and not about how the callback was written, so
/// both spellings of that one reader take it: a lambda closing over nothing but its own node, and a method
/// group naming a method of that node. They hand the runtime the same delegate - the node as its target and
/// a method of the node's type as the structural identity the plan is keyed by - and the method group is the
/// narrower of the two, having no enclosing scope to reach into. A method group bound to any other instance
/// is still reported, and so is one bound to an enclosing instance that is not a node.
/// </para>
/// <para>
/// A callback clearing this rule can still read static state, which changes what it answers without
/// changing which method it is, so that is a second rule (BESG004) rather than a second reason on this one:
/// the failure and the fix differ, and two ids let an author suppress one without losing the other.
/// </para>
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

        // A shader binding's value provider and resource binder are read the same way and keyed the same
        // way, so the same rule decides them. The generic builder is the one an out-of-tree author writes
        // against; the non-generic one is what the engine's own calls go through.
        "Beutl.Graphics.Effects.ShaderDefinitionBuilder",
        "Beutl.Graphics.Effects.ShaderBindingBuilder",

        // A definition builder retains its execution callback and the description is fingerprinted by that
        // delegate, so it is keyed and re-run on the same terms a metadata callback is. Each of these
        // already carries a state-passing parameter, which is where a per-recording value belongs.
        //
        // ShaderDefinition is deliberately absent: its factories take a binding-declaration action that is
        // invoked once while the definition is built and never retained, and the callbacks it registers
        // reach the rule through ShaderDefinitionBuilder above.
        "Beutl.Graphics.Rendering.OpaqueRenderDefinition",
        "Beutl.Graphics.Rendering.PaintedSourceDefinition",
        "Beutl.Graphics.Rendering.TargetScopeDefinition",
        "Beutl.Graphics.Rendering.TargetCommandDefinition",
        "Beutl.Graphics.Rendering.RawTargetScopeDefinition",
        "Beutl.Graphics.Rendering.RawTargetCommandDefinition",
        "Beutl.Graphics.Effects.GeometryDefinition");

    /// <remarks>
    /// One method rather than its whole type, because the type declares both kinds. A recording context's
    /// painted source retains its draw callback exactly as a definition builder retains its execute, and
    /// declares its hit test in the same argument list - so leaving it out let one call report the mapping
    /// and stay silent about the drawing beside it. Its other delegate-taking member, the input mapper, is
    /// invoked while the call is being made and never retained, which is the shape this rule has nothing to
    /// say about.
    /// </remarks>
    private static readonly ImmutableHashSet<string> s_contractMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Beutl.Graphics.Rendering.RenderNodeContext.PaintedSource");

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

        // Name rather than ToDisplayString: a generic builder displays with its type arguments, and the
        // rule is about the builder, not about what it was constructed with.
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

    /// <summary>
    /// Follows a member that only names a delegate - a readonly field, a get-only property - to the
    /// expression that decides which delegate it holds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// readonly fixes the reference, which is what the plan key needs, and says nothing about the delegate
    /// the field holds: the closure it carries and the state it reads are decided by whatever built it. So
    /// the member is only a name for the callback, and the rules apply to what it was given.
    /// </para>
    /// <para>
    /// A member this rule cannot read the source of is reported, on the same ground the getter rule already
    /// stands on: not being able to look proves nothing, and silence has to mean the rule looked.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The delegate expression to classify and the model that binds it, or why the member could not be
    /// followed.
    /// </returns>
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

                // Everything else is classified where it stands: a non-readonly field, a local, a
                // parameter, and a method group each have their own answer in DescribeImpurity.
                default:
                    return (expression, model, null);
            }

            model = GetSemanticModel(context, source.SyntaxTree);
            expression = Unwrap(source);
        }

        return (expression, model, null);
    }

    /// <remarks>
    /// Two members may name each other, which the compiler allows and which would otherwise walk for ever.
    /// </remarks>
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

    /// <remarks>
    /// A getter that runs more than one expression is not reduced to the delegate it returns, so it takes
    /// the same answer a getter with no source takes. This reuses the shape the static-state rule already
    /// trusts to stand for a getter's whole result.
    /// </remarks>
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

    /// <summary>
    /// Strips the syntax an author can write around a callback without changing which delegate arrives.
    /// </summary>
    /// <remarks>
    /// Each of these forms leaves the same delegate value underneath, so classifying the expression as
    /// written would let a capturing callback past on nothing but how it was spelled.
    /// </remarks>
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

    /// <returns>Why the callback can read changing state, or <see langword="null"/> when it cannot.</returns>
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

    /// <summary>
    /// Decides what a non-static lambda closes over: the render node declaring it, which is admitted, or
    /// anything else, which is not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A capture is read off the semantic model rather than off the syntax, so an instance member named
    /// without a receiver counts as reading <see langword="this"/> the same way an explicit <c>this.X</c>
    /// does, and a variable a nested lambda reads counts against the outer one that holds it.
    /// </para>
    /// <para>
    /// The <c>RenderNode</c> test is what the runtime identity validator's exemption is written against, so
    /// this reports a lambda reading an enclosing instance that is not a node rather than admitting a
    /// capture the engine would reject; and a lambda reading nothing at all is accepted whatever it is
    /// written inside, there being no instance for either side to disagree about.
    /// </para>
    /// </remarks>
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

    /// <remarks>
    /// A lambda's own parameters, and everything declared in its body, are written inside its span; a
    /// symbol with no declaration to point at - a setter's <c>value</c> - came from outside it.
    /// </remarks>
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

    /// <remarks>
    /// <para>
    /// A method group carries no closure, but an instance method's receiver becomes the delegate's target,
    /// and that receiver is whatever the author named. A reference-typed one the author holds somewhere
    /// else is their own object, so changing a field on it changes what the callback answers; a value-typed
    /// one is boxed at the conversion, so the delegate answers from a copy of whatever the receiver held
    /// right there.
    /// </para>
    /// <para>
    /// One receiver is admitted, and it is the instance the closure rule already admits: the enclosing one,
    /// when that instance is a <c>RenderNode</c>. Both spellings hand the runtime the same delegate - the
    /// node as its target, and a method of the node's type as the structural identity the plan is keyed by
    /// - so reporting one while admitting the other would be judging how the mapping was spelled. A method
    /// group is the narrower form at that: an instance method reads its receiver and its arguments, where a
    /// lambda has the whole enclosing scope to reach into and needs the closure walk to say it did not.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Decides a method group whose receiver is the instance the call is written inside: the render node
    /// declaring it, which is admitted, or anything else, which is not.
    /// </summary>
    /// <remarks>
    /// The type is read at the call rather than off the method, so a method a base type declares is judged
    /// by the instance that runs it, which is the one that becomes the delegate's target. That is the type
    /// the closure walk reads for a lambda and the object the runtime identity validator is handed.
    /// </remarks>
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

    /// <remarks>
    /// <para>
    /// A callback satisfies the capture rule and can still read static state, which changes the answer
    /// without changing the delegate. What the body names is what is visible here, and a method group names
    /// its body as surely as a lambda writes one out, so both are read: exempting the method group left the
    /// form BESG003's own message recommends checked by nothing at all.
    /// </para>
    /// <para>
    /// Within the body the burden runs the other way from the walk that follows it: a read is reported
    /// unless the member is proven to answer the same way twice, because a callback reading state nobody
    /// can pin down is the hazard.
    /// </para>
    /// </remarks>
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

    /// <remarks>
    /// The body is the whole of what a method group contributes, so a method group with no body to read is
    /// a callback this rule has inspected nothing of, and silence would say it looked. That is where the
    /// static field rule already stands for a type whose state was never imported, and a callback is where
    /// the reasoning bites hardest: there is no second half left to check.
    /// </remarks>
    private const string UnreadableCallbackBody =
        "the callback is a method whose body has no source in this compilation, so nothing it reads can be "
        + "seen, and being static says only that the delegate is the same one every frame, not that it "
        + "answers the same way; declare the method where this rule can read it, or write the callback as a "
        + "static lambda at the call site";

    /// <remarks>
    /// The same shape <see cref="MaxImmutableFieldDepth"/> takes and for the same reason: a chain longer
    /// than the walk is reported rather than accepted, so the bound can only ever cost a diagnostic and
    /// never hide one. Eight is past any callback written by hand, and a metadata callback needing a ninth
    /// hop is doing more than one of these should.
    /// </remarks>
    private const int MaxCallbackCallDepth = 8;

    private const string DeeperThanTheWalk =
        "the callback reaches it through a chain of calls longer than this rule walks, so what the rest of "
        + "that chain reads was never looked at, and a call chain nobody can follow to its end is not "
        + "evidence that the callback answers the same way twice";

    /// <summary>
    /// Reports every static member named by <paramref name="body"/>, or by a static member it names or an
    /// instance member it makes the receiver for, that is not proven to answer the same way twice.
    /// </summary>
    /// <remarks>
    /// <paramref name="walked"/> spans the whole callback rather than one path through it: two calls
    /// reaching the same method want one diagnostic, not one each, and a method that names itself would
    /// otherwise walk for ever.
    /// </remarks>
    private static void WalkBody(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        int depth,
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        foreach (SyntaxNode node in body.DescendantNodesAndSelf())
        {
            // A user-defined implicit conversion is spelled nowhere at all - it is implied by the type the
            // expression is used as - so it is asked for rather than found.
            if (node is ExpressionSyntax converted)
                FollowImplicitConversion(context, model, converted, depth, walked, report);

            if (node is not SimpleNameSyntax name)
            {
                FollowUnnamedInvocation(context, model, node, depth, walked, report);
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
                if (runsStatic
                    || FollowReceiverCreation(context, model, name, depth, walked, report))
                {
                    FollowCall(
                        context,
                        called,
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
                    instanceProperty,
                    name,
                    GetAccessExpression(name),
                    depth,
                    walked,
                    report);
                continue;
            }

            if (DescribeUnprovenStaticState(context, symbol) is not (string kind, string reason))
                continue;

            report(name, kind, symbol!, reason);
        }
    }

    /// <summary>
    /// Whether the method a call runs is static, whatever the call site spells it as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An extension method written in instance form - <c>value.Shift()</c> - binds to a reduced symbol
    /// whose <see cref="ISymbol.IsStatic"/> is <see langword="false"/>, so reading that alone left a body
    /// the author wrote, and could put any static read into, behind nothing more than a dot: the
    /// staticness gate said no, and the receiver gate could not say yes either, because the receiver is a
    /// value the callback was handed rather than one it made.
    /// </para>
    /// <para>
    /// Following it asks nothing about a receiver, which is what separates it from the instance members
    /// this rule stops at. What runs is <see cref="IMethodSymbol.ReducedFrom"/>, a static method whose
    /// every parameter - the receiver included - is an argument the call site passes, so the walk is
    /// reading a static body over its own arguments, exactly as it does for the same call written as
    /// <c>Extensions.Shift(value)</c>. Answering the two spellings differently would have made the rule
    /// one an author escapes by adding a dot.
    /// </para>
    /// </remarks>
    private static bool RunsAStaticMethod(IMethodSymbol method)
        => method.IsStatic || method.ReducedFrom is { IsStatic: true };

    /// <summary>
    /// Follows the member an expression invokes without naming it.
    /// </summary>
    /// <remarks>
    /// An object creation names the type and not the constructor overload it picked, a constructor
    /// initialiser is spelled <c>this</c> or <c>base</c>, an indexer is spelled as brackets, and a
    /// user-defined operator or explicit conversion is spelled as punctuation. None of them reach the name
    /// loop, and each still runs a body: leaving them out let a callback move a read into a constructor and
    /// keep the rule silent, which is the one thing silence must not mean.
    /// </remarks>
    private static void FollowUnnamedInvocation(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
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
                FollowPropertyAccess(context, model, indexer, element, element, depth, walked, report);
            }

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

    /// <remarks>
    /// A user-defined implicit conversion runs a static method the source never spells, so an author can
    /// move a read behind one by changing nothing but a declared type. Asking every expression what it was
    /// converted to costs a semantic query per node, which is affordable because this walk runs only for the
    /// callbacks a contract call passes and not over the compilation at large.
    /// </remarks>
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

    /// <summary>
    /// Follows the accessor an instance property or indexer reference runs.
    /// </summary>
    /// <remarks>
    /// A read runs the getter and a plain assignment runs the setter, while a compound assignment and an
    /// increment run each in turn. Following both for every reference would report a static read written in
    /// a setter that a bare read never reaches, which is a diagnostic about code the callback does not run.
    /// A static property is not routed here: it is judged by the stricter question of whether its value is
    /// the same on every read, which is more than what its getter happens to name.
    /// </remarks>
    private static void FollowPropertyAccess(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        IPropertySymbol property,
        SyntaxNode reference,
        ExpressionSyntax access,
        int depth,
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (!FollowReceiverCreation(context, model, reference, depth, walked, report))
            return;

        if (RunsGetter(access) && property.GetMethod is { } getter)
            FollowCall(context, getter, reference, "property", depth, walked, report);

        if (RunsSetter(access) && property.SetMethod is { } setter)
            FollowCall(context, setter, reference, "property", depth, walked, report);
    }

    /// <summary>
    /// Walks the creation that made the instance the call runs on, and says whether there was one this
    /// rule could point at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole of what an instance member has to clear, and what clears it is an object creation
    /// this rule can point at. An object creation names the exact type it makes, so the member the call
    /// binds to is the member that runs even when it is virtual; and the instance carries only what its
    /// constructor and initialisers put there. Nothing has to be known about a receiver held anywhere else,
    /// which is the identity this rule does not model.
    /// </para>
    /// <para>
    /// The creation is walked and not merely identified, because "carries only what its constructor put
    /// there" is a reason to read that constructor rather than to trust it. A constructor that reads a
    /// mutable static is exactly the impurity this rule is for, and the member called on the instance hands
    /// the captured value back without naming the static anywhere the walk over the callback would see it.
    /// A creation written at the call site is already reached as an expression of the body, so following it
    /// here is what makes a helper held in a field answer the same as one made in the expression; walking
    /// it from both places costs nothing, the constructor being keyed in the same walked set.
    /// </para>
    /// <para>
    /// The creation does not have to be written in the expression. A readonly field, and a local written
    /// once where it is declared, name one creation and can never name another, so the type they hold is as
    /// exactly known as the type spelled at the call - and a helper kept in a field is how a callback most
    /// naturally reaches one. Two things have to be checked that a creation at the call site answers for
    /// itself. The initialiser has to be the whole story: readonly leaves the declaring type's constructors
    /// free to put a different instance there, so a field one of them writes is not followed. And the
    /// creation has to make the declared type exactly, because the member is bound against the declaration
    /// rather than against the instance: a field declared as a base of what it holds would have the walk
    /// read the body an override replaces, and report a read the instance never makes.
    /// </para>
    /// <para>
    /// A receiver the callback was handed is where the walk stops, and the reason is not only that it
    /// cannot be identified. The callbacks under this rule are handed the objects they work through - a
    /// session, a canvas, a context - so walking a member called on one of those is walking the engine
    /// behind it, and the mutable statics a render backend keeps say nothing about whether the callback
    /// answers the same way twice. Following them reported the whole backend and would have taught authors
    /// to suppress the id. That is why a parameter is not resolved here, and why a field is followed only
    /// where the callback reaches it on its own - a bare name, <c>this</c>, or a static reached through its
    /// type - and never as the state of some receiver the callback did not make.
    /// </para>
    /// </remarks>
    private static bool FollowReceiverCreation(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode reference,
        int depth,
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (GetReceiverCreation(context, model, reference) is not { } made)
            return false;

        if (made.Model.GetSymbolInfo(made.Creation, context.CancellationToken).Symbol
            is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            FollowConstructor(context, constructor, reference, depth, walked, report);
        }

        return true;
    }

    /// <returns>
    /// The object creation that made the instance the call runs on, and the model that binds it, or
    /// <see langword="null"/> when this rule cannot point at one.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A type parameter is left out on purpose: <c>new T()</c> makes whatever T was substituted with, so
    /// the member found on the constraint is not necessarily the member that runs.
    /// </para>
    /// <para>
    /// The exact type is required rather than an assignable one, and that is the last of the three things
    /// a creation written at the call site answers for itself. The member the call binds to is chosen by
    /// the type of the expression, so a field declared as a base of what its initialiser makes would have
    /// the walk read the body an override replaces, and report a read the instance never makes.
    /// </para>
    /// </remarks>
    private static (ExpressionSyntax Creation, SemanticModel Model)? GetReceiverCreation(
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

        return SymbolEqualityComparer.Default.Equals(
            created, model.GetTypeInfo(receiver, context.CancellationToken).Type)
            ? creation
            : null;
    }

    /// <returns>
    /// The object creation a member holding one instance for good was given, and the model that binds it,
    /// or <see langword="null"/> when the expression names no such member.
    /// </returns>
    /// <remarks>
    /// One hop and no chain: the member has to be given a creation itself, not a name for a member that was
    /// given one. A chain would have to answer for every hop what this answers for one - that nothing can
    /// put a second instance there - and each hop it could not read would be a type assumed rather than
    /// known.
    /// </remarks>
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
            IFieldSymbol { IsReadOnly: true } field
                when ReachesFieldOnItsOwn(context, model, expression, field)
                    && !IsWrittenInAConstructor(context, field)
                => GetFieldInitializer(context, field),

            ILocalSymbol local => GetUnreassignedLocalInitializer(context, local),

            // A parameter, a settable field, a property, a method group: each is a receiver whose making
            // this rule was not shown, and the walk stops at every one of them.
            _ => null,
        };

        if (initializer is null)
            return null;

        SemanticModel initializerModel = GetSemanticModel(context, initializer.SyntaxTree);
        ExpressionSyntax value = StripParentheses(initializer);
        return value is BaseObjectCreationExpressionSyntax ? (value, initializerModel) : null;
    }

    /// <returns>
    /// Whether the callback reaches the field without going through a receiver of its own.
    /// </returns>
    /// <remarks>
    /// A field read off another instance is that instance's state, and following it is exactly the walk
    /// into the engine behind a handed-in session or canvas that this rule stops at. A bare name, an
    /// explicit <c>this</c>, and a static reached through the type that declares it carry no such instance.
    /// </remarks>
    private static bool ReachesFieldOnItsOwn(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ExpressionSyntax reference,
        IFieldSymbol field)
    {
        if (reference is not MemberAccessExpressionSyntax access)
            return true;

        ExpressionSyntax qualifier = StripParentheses(access.Expression);

        return field.IsStatic
            ? model.GetSymbolInfo(qualifier, context.CancellationToken).Symbol is ITypeSymbol
            : qualifier is ThisExpressionSyntax;
    }

    /// <remarks>
    /// readonly stops every assignment outside the declaring type's constructors, so those are the whole of
    /// what has to be read, and one write in any of them means the instance the callback reaches is not the
    /// one the initialiser made. A primary constructor is declared by the type and can write no field of
    /// it, so it is skipped rather than read: taking its declaration would put every method body of the
    /// type inside a constructor's span.
    /// </remarks>
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

    /// <returns>
    /// The declaration's initialiser when nothing else writes the local, or <see langword="null"/> when it
    /// has no initialiser or is assigned again.
    /// </returns>
    /// <remarks>
    /// A local cannot carry the readonly modifier, so what stands in its place is the assignment list: a
    /// local written once, where it is declared, names the same instance everywhere it is read. The scope
    /// searched is the member the declaration is written in, which is as far as a local can be reached at
    /// all.
    /// </remarks>
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

    /// <returns>Whether anything in <paramref name="scope"/> writes <paramref name="symbol"/>.</returns>
    /// <remarks>
    /// Every form of a write counts, because each one replaces the instance a name stands for: an
    /// assignment, a deconstruction naming it as one of its targets, an argument passed by reference, and
    /// an increment, which a user-defined operator makes reachable on a receiver too.
    /// </remarks>
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

    /// <returns>
    /// The expression the call is made on, or <see langword="null"/> when the reference carries no receiver
    /// of its own - a bare name, which is this instance or a static.
    /// </returns>
    /// <remarks>
    /// A conditional access is the same question spelled differently, not a different question: the chain
    /// writes its receiver once, at the head, so the binding beside the name carries none of its own.
    /// Reading only the receiver written beside the name let an author move a read behind a question mark -
    /// <c>new Helper()?.Map(value)</c> - and take the whole instance walk out of the rule, which is the one
    /// thing silence must not mean.
    /// </remarks>
    private static ExpressionSyntax? GetReceiver(SyntaxNode reference) => reference switch
    {
        SimpleNameSyntax name when name.Parent is MemberAccessExpressionSyntax access && access.Name == name
            => access.Expression,
        SimpleNameSyntax name when name.Parent is MemberBindingExpressionSyntax binding && binding.Name == name
            => ConditionalAccessSyntax.FindReceiver(binding),
        ElementAccessExpressionSyntax element => element.Expression,
        _ => null,
    };

    /// <remarks>
    /// Deliberately not <see cref="Unwrap"/>, which also strips a cast: a cast is what changes the type a
    /// call is bound against, so stripping one would read the exact type off an expression the call was
    /// never bound to.
    /// </remarks>
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
            || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);

    private static bool RunsSetter(ExpressionSyntax access) => access.Parent switch
    {
        AssignmentExpressionSyntax assignment => assignment.Left == access,
        PrefixUnaryExpressionSyntax prefix => prefix.IsKind(SyntaxKind.PreIncrementExpression)
            || prefix.IsKind(SyntaxKind.PreDecrementExpression),
        PostfixUnaryExpressionSyntax postfix => postfix.IsKind(SyntaxKind.PostIncrementExpression)
            || postfix.IsKind(SyntaxKind.PostDecrementExpression),
        _ => false,
    };

    /// <remarks>
    /// A callee with no body to read is where the walk stops without reporting, and that is not the answer
    /// the method group itself gets. The difference is what the rule has already done: here it read the
    /// callback and is one layer past it, so the callee is a bound on an inspected callback rather than an
    /// uninspected one, and reporting it would reject every callback that names <c>Math.Clamp</c>.
    /// </remarks>
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

    /// <summary>
    /// Follows a constructor the callback reaches, on the same terms a called method is followed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same bound and the same answers: a constructor with no source here stops the walk without
    /// reporting, because the rule did inspect the callback and this is a bound on an inspected one; a chain
    /// longer than the bound is reported rather than accepted.
    /// </para>
    /// <para>
    /// What a constructor runs is more than one syntax node. The instance field and property initialisers of
    /// the type run before the body of whichever constructor does not chain to another of the same type, and
    /// a constructor with no initialiser of its own still runs its base type's parameterless one. None of
    /// that is written where the name loop could reach it.
    /// </para>
    /// </remarks>
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

    /// <returns>
    /// Every syntax node the constructor runs whose names this rule can read - its initialiser call, its own
    /// body, and the instance field and property initialisers it runs - or an empty list when it has no
    /// source in this compilation.
    /// </returns>
    /// <remarks>
    /// A primary constructor is declared by the type, whose members are not what it runs, so only its base
    /// argument list is taken from there. Parameter defaults are left out on purpose: a default has to be a
    /// constant expression, which cannot read state at all.
    /// </remarks>
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

    /// <remarks>
    /// An auto-property's backing field carries the same initialiser the property declares, so only the
    /// members an author wrote are read; taking both would report one read twice.
    /// </remarks>
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

    /// <returns>
    /// The base constructor this one runs without saying so, or <see langword="null"/> when it says so
    /// itself or when the base type has no source here.
    /// </returns>
    /// <remarks>
    /// The question asked of the base is whether the type has source here, not whether its constructor does:
    /// a type declared in this compilation whose constructor is the one the compiler writes still runs its
    /// own base's, so stopping at it would lose a chain the rule can read. A base with no source at all -
    /// object, which every class reaches - is where the walk stops, and returning it would have the depth
    /// bound report a chain that ends at nothing.
    /// </remarks>
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

    /// <returns>
    /// The syntax whose names stand for everything the method reads, or <see langword="null"/> when the
    /// method has no body in this compilation - one compiled into another assembly, or abstract, extern or
    /// partial with no implementing declaration.
    /// </returns>
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

    /// <returns>
    /// What kind of static member this is and why it is not proven to answer the same way twice, or
    /// <see langword="null"/> when the read is proven stable.
    /// </returns>
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

            // An event is a delegate field whose value is its subscriber list, and += and -= are its
            // assignments. Reading that list back is legal only inside the declaring type - where a
            // callback written beside the event, and any helper the walk follows into it, both sit -
            // while writing it binds from anywhere, so the one case covers both spellings.
            IEventSymbol { IsStatic: true } =>
                ("static event", "a static event holds a subscriber list that any += or -= "
                    + "anywhere rewrites, so what the callback reads off it, or does to it, can "
                    + "differ between two recordings while the plan key stays the same"),

            _ => null,
        };
    }

    /// <remarks>
    /// The two ways a field's type fails this test want different fixes and so are said apart. A type
    /// carrying writable state has to lose it or move behind the state-passing parameter; a type this rule
    /// was never shown the state of has to be declared where the rule can read it, and telling an author
    /// their type "carries writable state" when the rule simply could not see it would send them looking for
    /// a field that is not there. Only a class can be in the second case - a struct's fields are imported
    /// whatever its assembly - and only when everything that was visible passed, which is exactly the shape
    /// the walk used to clear.
    /// </remarks>
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

    /// <remarks>
    /// Having no setter says only that this declaration does not write the property; it does not say the
    /// getter answers the same way twice, because a get-only getter is free to compute its result from
    /// anything. So the getter itself has to prove the value, and a getter this rule cannot read - one
    /// whose source is not in the compilation - proves nothing and is reported.
    /// </remarks>
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

    /// <returns>
    /// The single expression the getter can ever answer with, or <see langword="null"/> when the getter runs
    /// enough code that no one expression stands for its result.
    /// </returns>
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

    /// <remarks>
    /// A static readonly field is accepted on the same terms the field rule accepts one directly, through
    /// the same test, so routing the read through a property makes it neither stricter nor looser.
    /// </remarks>
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

    /// <summary>
    /// Decides whether the value a static readonly field holds carries state something can still write.
    /// </summary>
    /// <remarks>
    /// This is the single test both places that accept a static readonly field are judged by - the field a
    /// callback names directly, and the one a proven getter hands back - because readonly says the same
    /// thing in both: the reference is fixed, and what it points at is not.
    /// </remarks>
    private static bool IsImmutableType(ITypeSymbol type) => IsImmutableType(type, MaxImmutableFieldDepth);

    /// <remarks>
    /// A class can hold a reference to its own type, so the walk below has a real cycle to end, and a
    /// value-type layout that would cycle is only rejected while the code still compiles - which an
    /// analyzer reading a half-written file cannot assume. Eight is past any type written by hand, and the
    /// framework's own bottom out after one or two levels; a type nested deeper is reported rather than
    /// assumed, so the bound can only ever cost a diagnostic, never hide one.
    /// </remarks>
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

    /// <summary>
    /// Decides whether the field list this rule can read is the whole of what an instance carries.
    /// </summary>
    /// <remarks>
    /// An empty field list says two different things and the walk cannot tell them apart on its own: this
    /// type has no state, and this rule was not shown its state. Which one it is turns on where the type was
    /// read from, so that is asked rather than guessed.
    /// </remarks>
    private static bool HasCompleteFieldList(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (!IsFieldListReadable(current))
                return false;
        }

        return true;
    }

    /// <remarks>
    /// A type declared in this compilation is read from its declaration, where every field is present
    /// whatever its accessibility. A type that arrives as metadata is imported down to its public and
    /// protected members, so a class's private and internal state is simply not there - which is how
    /// System.Text.StringBuilder reaches an analyzer as a sealed class with no fields at all, and how a
    /// reference assembly, having dropped the private fields on the way out, arrives too. There the list is
    /// a floor and not the type, and the rule refuses what it cannot prove.
    /// <para>
    /// A struct is the exception, and not by accident: a compilation cannot decide definite assignment, an
    /// unmanaged constraint or a layout cycle without every field of a metadata struct, so it imports them
    /// whatever their accessibility. The private field of a referenced readonly struct is therefore readable
    /// where a class's is not.
    /// </para>
    /// <para>
    /// object, ValueType and Enum are what a base chain ends at and declare no instance field in any build
    /// of them, so reaching one is not reaching an unread type.
    /// </para>
    /// </remarks>
    private static bool IsFieldListReadable(INamedTypeSymbol type)
        => type.IsValueType
            || type.SpecialType is SpecialType.System_Object or SpecialType.System_ValueType
                or SpecialType.System_Enum
            || !type.DeclaringSyntaxReferences.IsEmpty;

    /// <remarks>
    /// Only worth reading once <see cref="HasCompleteFieldList"/> says the list is the whole type; on its
    /// own it answers about the fields it was given, not about the type they came from.
    /// </remarks>
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

    /// <remarks>
    /// The property whose getter decides this is routinely declared in another file, and the context's model
    /// only covers the tree the callback is written in. RS1030 warns because a second model costs memory;
    /// this reaches for one only for a static property a metadata callback names, which is rare enough that
    /// the alternative - reporting every property declared elsewhere - would cost far more.
    /// </remarks>
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
