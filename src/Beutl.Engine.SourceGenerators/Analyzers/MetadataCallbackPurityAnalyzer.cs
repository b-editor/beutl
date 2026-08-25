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
        "Beutl.Graphics.Effects.ShaderBindingBuilder");

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

            ExpressionSyntax callback = Unwrap(argument.Expression);

            if (DescribeImpurity(context, callback) is { } reason)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.CapturingMetadataCallback,
                    argument.GetLocation(),
                    containingType.Name,
                    method.Name,
                    reason));
            }

            ReportUnprovenStaticStateReads(context, containingType, method, callback);
        }
    }

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
    private static string? DescribeImpurity(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
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
                    ISymbol? symbol = context.SemanticModel
                        .GetSymbolInfo(expression, context.CancellationToken).Symbol;
                    return symbol switch
                    {
                        IMethodSymbol method => DescribeReceiverImpurity(context, method, expression),

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

        ITypeSymbol? receiver = context.SemanticModel
            .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        return receiver is { IsValueType: true }
            ? "the callback is an instance method on a value type, so every conversion boxes a fresh "
              + "receiver and the delegate is a different instance on every frame"
            : "the callback is an instance method on a reference type, and the delegate keeps that "
              + "object as its receiver";
    }

    /// <remarks>
    /// A static lambda satisfies the capture rule and can still read static state, which changes the answer
    /// without changing the delegate. Only what the body names is visible here; a static method it calls can
    /// read anything, and that is the bound this rule is documented to stop at rather than paper over.
    /// Within that bound the burden runs the other way: a read is reported unless the member is proven to
    /// answer the same way twice, because a callback reading state nobody can pin down is the hazard.
    /// </remarks>
    private static void ReportUnprovenStaticStateReads(
        SyntaxNodeAnalysisContext context,
        INamedTypeSymbol containingType,
        IMethodSymbol method,
        ExpressionSyntax callback)
    {
        if (callback is not AnonymousFunctionExpressionSyntax { Body: { } body })
            return;

        foreach (SimpleNameSyntax name in body.DescendantNodesAndSelf().OfType<SimpleNameSyntax>())
        {
            // A nameof argument names a member without reading it.
            if (IsInsideNameOf(name))
                continue;

            ISymbol? symbol = context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol;
            if (DescribeUnprovenStaticState(context, symbol) is not (string kind, string reason))
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.StaticStateMetadataCallback,
                name.GetLocation(),
                containingType.Name,
                method.Name,
                kind,
                symbol!.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                reason));
        }
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
            IFieldSymbol { IsStatic: true, IsConst: false, IsReadOnly: false } =>
                ("field", "a static field that is neither const nor readonly can be assigned between two "
                    + "recordings while the plan key stays the same"),

            IPropertySymbol { IsStatic: true } property => DescribeUnprovenGetter(context, property),

            _ => null,
        };
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
            return ("property", "its setter can change what it answers");

        ImmutableArray<SyntaxReference> declarations = property.DeclaringSyntaxReferences;
        if (declarations.IsEmpty)
        {
            return ("property", "its getter has no source in this compilation, so what the getter reads "
                + "cannot be seen, and having no setter is not on its own evidence that it answers the "
                + "same way twice");
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

        return ("property", "its getter is not a shape this rule can prove yields the same value on every "
            + "read, and having no setter is not on its own evidence that it does");
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
    /// A static readonly field is accepted on the same terms the field rule accepts one directly, so routing
    /// the read through a property does not make it stricter. That carries the field rule's limit with it:
    /// mutation of the object such a field holds stays invisible, which is why the type has to be one whose
    /// instances cannot be mutated at all.
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

    private static bool IsImmutableType(ITypeSymbol type) => type switch
    {
        { TypeKind: TypeKind.Enum } => true,

        // A readonly struct has no instance member that can write it, so a readonly field holding one keeps
        // the value it was given; a struct without the modifier can be written through an instance method.
        { IsValueType: true, IsReadOnly: true } => true,

        // The framework types with no mutable state at all. Anything else is reported, including a sealed
        // type an author considers immutable, because nothing in the symbol says so.
        {
            SpecialType: SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_SByte
                or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64
                or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double
                or SpecialType.System_Decimal or SpecialType.System_String or SpecialType.System_IntPtr
                or SpecialType.System_UIntPtr or SpecialType.System_DateTime
        } => true,

        _ => false,
    };

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

            if (current is AnonymousFunctionExpressionSyntax)
                return false;
        }

        return false;
    }
}
