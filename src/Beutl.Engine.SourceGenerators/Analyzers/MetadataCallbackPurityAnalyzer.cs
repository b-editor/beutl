using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Beutl.Engine.SourceGenerators.Analyzers;

/// <summary>
/// Reports render metadata callbacks whose state can change without changing their plan identity.
/// </summary>
/// <remarks>
/// Plans are keyed by callback method identity. BESG003 permits only explicit state and the declaring
/// <c>RenderNode</c>, whose changes trigger re-recording. BESG004 separately reports unproven static state.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class MetadataCallbackPurityAnalyzer : DiagnosticAnalyzer
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
