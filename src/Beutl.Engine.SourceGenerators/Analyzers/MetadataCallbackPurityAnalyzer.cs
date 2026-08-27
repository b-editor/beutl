using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Beutl.Engine.SourceGenerators.Analyzers;

/// <summary>
/// Reports a callback passed to a render metadata contract that is not a stable, state-free delegate.
/// </summary>
/// <remarks>
/// <para>
/// These callbacks are evaluated repeatedly - forward bounds, backward region of interest, scale
/// reevaluation, hit testing, cache lookup - and the compiled plan is keyed by which callback it is, not by
/// what the callback closed over. A callback that reads a captured local therefore lets one plan key stand
/// for two different answers, and the second recording replays a plan compiled for the first.
/// </para>
/// <para>
/// The engine used to walk the delegate's closure at recording time to catch this. Recording is the render
/// path, so that walk is gone; this says the same thing at compile time, before the frame that would have
/// paid for it.
/// </para>
/// <para>
/// The plan key is the delegate itself, so the callback also has to be the same delegate on every frame.
/// Only a static lambda and a static method group are: the compiler caches those in a singleton field, while
/// any conversion that needs a receiver builds a new delegate each time.
/// </para>
/// <para>
/// A static lambda clears that bar and still fails the first one when it reads static state, so that is a
/// second rule (BESG004) rather than a second reason on the first: the failure and the fix differ, and two
/// ids let an author suppress one without losing the other.
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
        "Beutl.Graphics.Rendering.TargetScopeDefinition",
        "Beutl.Graphics.Rendering.TargetCommandDefinition",
        "Beutl.Graphics.Rendering.RawTargetScopeDefinition",
        "Beutl.Graphics.Rendering.RawTargetCommandDefinition",
        "Beutl.Graphics.Effects.GeometryDefinition");

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
        if (method.ContainingType is not { } containingType
            || !s_contractTypes.Contains(
                containingType.ContainingNamespace.ToDisplayString() + "." + containingType.Name))
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
                    : "the lambda is not declared static, so it can read a local that is assigned later";

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

    /// <remarks>
    /// A method group carries no closure, but an instance method's receiver becomes the delegate's target,
    /// and the target is half the delegate's identity. A reference-typed receiver is the author's own object,
    /// so changing a field on it changes what the callback answers; a value-typed one is boxed afresh at every
    /// conversion, so the delegate is a different instance on every frame and no plan is ever reused.
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
            // An unqualified instance method is called on `this`, which the enclosing object can change.
            return "the callback is an instance method, whose receiver can be changed after this call";
        }

        ITypeSymbol? receiver = model
            .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        return receiver is { IsValueType: true }
            ? "the callback is an instance method on a value type, so every conversion boxes a fresh "
              + "receiver and the delegate is a different instance on every frame"
            : "the callback is an instance method on a reference type, and the delegate keeps that "
              + "object as its receiver";
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

        walked.Add(group.OriginalDefinition);

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
    /// Reports every static member named by <paramref name="body"/>, or by a static method it names, that is
    /// not proven to answer the same way twice.
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

            // Only a static method, because a virtual call has no one body to read and what an instance
            // receiver carries is state the field walk decides separately; and only these three kinds,
            // because a property read is already the branch below and an operator, a conversion and a
            // constructor reach the walk through FollowUnnamedInvocation instead.
            if (symbol is IMethodSymbol
                {
                    IsStatic: true,
                    MethodKind: MethodKind.Ordinary or MethodKind.LocalFunction
                        or MethodKind.ReducedExtension
                } called)
            {
                FollowCall(context, called, name, "static method", depth, walked, report);
                continue;
            }

            if (DescribeUnprovenStaticState(context, symbol) is not (string kind, string reason))
                continue;

            report(name, kind, symbol!, reason);
        }
    }

    /// <summary>
    /// Follows the member an expression invokes without naming it.
    /// </summary>
    /// <remarks>
    /// An object creation names the type and not the constructor overload it picked, a constructor
    /// initialiser is spelled <c>this</c> or <c>base</c>, and a user-defined operator or explicit conversion
    /// is spelled as punctuation. None of them reach the name loop, and each still runs a body: leaving them
    /// out let a callback move a read into a constructor and keep the rule silent, which is the one thing
    /// silence must not mean.
    /// </remarks>
    private static void FollowUnnamedInvocation(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode node,
        int depth,
        HashSet<ISymbol> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
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
        if (!walked.Add(called.OriginalDefinition))
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
