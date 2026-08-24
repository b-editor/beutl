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
        ImmutableArray.Create(DiagnosticDescriptors.CapturingMetadataCallback);

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

            if (DescribeImpurity(context, argument.Expression) is not { } reason)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.CapturingMetadataCallback,
                argument.GetLocation(),
                containingType.Name,
                method.Name,
                reason));
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

            default:
                return null;
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
}
