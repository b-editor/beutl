using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Beutl.Engine.SourceGenerators.Analyzers;

/// <summary>
/// Reports a <c>RenderNode</c> subclass that changes state its <c>Process</c> reads without marking the node
/// changed.
/// </summary>
/// <remarks>
/// <para>
/// <c>RenderNode.HasChanges</c> is public, so an out-of-tree node decides for itself when to raise it. Today
/// a node that forgets is still re-recorded every frame and only loses the pixel cache, which is why the
/// omission has been survivable. Once a recorded graph may be reused for a node that reports no changes,
/// forgetting means the node is not re-recorded at all and renders stale, and no compile error says so.
/// </para>
/// <para>
/// What a rule can decide here is bounded, and the bound is the point rather than an apology for it. This
/// reports one shape - an assignment written in the node's own type to an instance field or auto-property
/// that its <c>Process</c> reads - because that is the shape authors write, and because a rule that guessed
/// past it on a public extension point would be suppressed wholesale and then protect nothing. The runtime
/// cross-check in <c>Beutl.Engine</c> is what covers the rest; silence here is not a proof.
/// </para>
/// <para>
/// An assignment inside <c>Process</c>, or inside a method <c>Process</c> calls, is deliberately not
/// reported: memoizing a value derived from state the node already tracks is ordinary and correct there, and
/// telling it apart from a real drift needs the two recordings only the runtime check has.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RenderNodeChangeMarkingAnalyzer : DiagnosticAnalyzer
{
    private const string RenderNodeMetadataName = "Beutl.Graphics.Rendering.RenderNode";
    private const string ProcessMethodName = "Process";
    private const string HasChangesPropertyName = "HasChanges";
    private const string DisposeCallbackName = "OnDispose";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.UnmarkedRenderNodeMutation);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // A symbol action rather than a syntax action: a partial node would otherwise be analyzed once per
        // declaration, each time seeing only the members that declaration happens to hold.
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsStatic: false } type)
            return;

        INamedTypeSymbol? renderNodeType = context.Compilation.GetTypeByMetadataName(RenderNodeMetadataName);
        if (renderNodeType is null || !InheritsFrom(type, renderNodeType))
            return;

        IMethodSymbol? process = FindProcessMethod(type);
        if (process is null)
            return;

        var analysis = new TypeAnalysis(context.Compilation, type, renderNodeType);
        ImmutableHashSet<ISymbol> processClosure = analysis.CollectCallClosure(process);
        ImmutableHashSet<ISymbol> readState = analysis.CollectReadInstanceState(processClosure);
        if (readState.IsEmpty)
            return;

        foreach (ISymbol member in type.GetMembers())
        {
            if (member is not IMethodSymbol method)
                continue;

            // A constructor runs before anything is recorded and a teardown path after the last recording,
            // so neither has a later frame to invalidate. Process and the methods it calls are excluded on
            // purpose - see the remarks on this type.
            if (method.MethodKind is MethodKind.Constructor
                    or MethodKind.StaticConstructor
                    or MethodKind.Destructor
                || method.IsStatic
                || IsDisposalOverride(method, renderNodeType)
                || processClosure.Contains(method))
            {
                continue;
            }

            if (analysis.MarksChanged(method))
                continue;

            foreach (StateAssignment assignment in analysis.FindStateAssignments(method, readState))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnmarkedRenderNodeMutation,
                    assignment.Location,
                    type.Name,
                    DescribeMember(method),
                    assignment.State.Name));
            }
        }
    }

    private static bool IsDisposalOverride(IMethodSymbol method, INamedTypeSymbol renderNodeType)
    {
        if (method.Name != DisposeCallbackName)
            return false;

        for (IMethodSymbol? current = method; current is not null; current = current.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    current.ContainingType?.OriginalDefinition,
                    renderNodeType))
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeMember(IMethodSymbol method) => method.MethodKind switch
    {
        MethodKind.PropertySet or MethodKind.PropertyGet when method.AssociatedSymbol is { } associated
            => associated.Name,
        MethodKind.EventAdd or MethodKind.EventRemove when method.AssociatedSymbol is { } associated
            => associated.Name,
        _ => method.Name,
    };

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
                return true;
        }

        return false;
    }

    /// <summary>Finds the <c>Process</c> body a node of this type would actually run, if it is in source.</summary>
    /// <remarks>
    /// A node that inherits <c>Process</c> is analyzed against the base's body, so only state that body names
    /// enters the read set. Whatever the base reaches through a virtual hook does not, which is one of the
    /// places this rule stops.
    /// </remarks>
    private static IMethodSymbol? FindProcessMethod(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (ISymbol member in current.GetMembers(ProcessMethodName))
            {
                if (member is IMethodSymbol { IsStatic: false, Parameters.Length: 1 } method
                    && method.DeclaringSyntaxReferences.Length > 0)
                {
                    return method;
                }
            }
        }

        return null;
    }

    private readonly record struct StateAssignment(ISymbol State, Location Location);

    private sealed class TypeAnalysis(
        Compilation compilation,
        INamedTypeSymbol type,
        INamedTypeSymbol renderNodeType)
    {
        /// <summary>The methods reachable from <paramref name="entryPoint"/> without leaving the node's own type chain.</summary>
        /// <remarks>
        /// Property and indexer accesses are followed as well as invocations, so a node that exposes its
        /// state through a hand-written property still has the backing field in the read set.
        /// </remarks>
        public ImmutableHashSet<ISymbol> CollectCallClosure(IMethodSymbol entryPoint)
        {
            var visited = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
            var pending = new Stack<IMethodSymbol>();
            pending.Push(entryPoint);
            visited.Add(entryPoint);

            while (pending.Count > 0)
            {
                IMethodSymbol current = pending.Pop();
                foreach (BodyWithModel body in GetBodies(current))
                {
                    foreach (SyntaxNode node in body.Body.DescendantNodesAndSelf())
                    {
                        if (node is not SimpleNameSyntax name || IsInsideNameOf(name))
                            continue;

                        foreach (IMethodSymbol callee in ResolveCallees(body.Model, name))
                        {
                            if (IsOwnTypeChainMember(callee)
                                && callee.DeclaringSyntaxReferences.Length > 0
                                && visited.Add(callee))
                            {
                                pending.Push(callee);
                            }
                        }
                    }
                }
            }

            return visited.ToImmutable();
        }

        /// <summary>The instance fields and auto-properties the given bodies read.</summary>
        public ImmutableHashSet<ISymbol> CollectReadInstanceState(ImmutableHashSet<ISymbol> methods)
        {
            var read = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
            foreach (IMethodSymbol method in methods.OfType<IMethodSymbol>())
            {
                foreach (BodyWithModel body in GetBodies(method))
                {
                    foreach (SyntaxNode node in body.Body.DescendantNodesAndSelf())
                    {
                        if (node is not SimpleNameSyntax name || IsInsideNameOf(name))
                            continue;

                        ISymbol? symbol = body.Model.GetSymbolInfo(name).Symbol;
                        if (!IsTrackedInstanceState(symbol))
                            continue;

                        // A simple assignment overwrites without reading, so the target alone does not make
                        // the member part of what Process depends on.
                        if (!IsSimpleAssignmentTarget(GetAccessExpression(name)))
                            read.Add(symbol!);
                    }
                }
            }

            return read.ToImmutable();
        }

        /// <summary>Whether an assignment to <c>HasChanges</c> is reachable from <paramref name="method"/>.</summary>
        /// <remarks>
        /// Path-insensitive by design: one assignment anywhere in the member, or in a method of the same type
        /// it calls, clears every assignment in that member. A mutation on a branch that skips the mark is
        /// therefore missed, which is the direction this rule errs in.
        /// </remarks>
        public bool MarksChanged(IMethodSymbol method)
        {
            var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            return MarksChangedCore(method, visited);
        }

        public IEnumerable<StateAssignment> FindStateAssignments(
            IMethodSymbol method,
            ImmutableHashSet<ISymbol> trackedState)
        {
            foreach (BodyWithModel body in GetBodies(method))
            {
                foreach (SyntaxNode node in body.Body.DescendantNodesAndSelf())
                {
                    if (node is not SimpleNameSyntax name || IsInsideNameOf(name))
                        continue;

                    ISymbol? symbol = body.Model.GetSymbolInfo(name).Symbol;
                    if (symbol is null || !trackedState.Contains(symbol))
                        continue;

                    ExpressionSyntax access = GetAccessExpression(name);

                    // An assignment to another instance of the same type is a different object's state, and
                    // marking this node changed would say nothing about it.
                    if (!IsOnThisInstance(name))
                        continue;

                    if (IsWriteTarget(access))
                        yield return new StateAssignment(symbol, access.GetLocation());
                }
            }
        }

        private bool MarksChangedCore(IMethodSymbol method, HashSet<ISymbol> visited)
        {
            if (!visited.Add(method))
                return false;

            foreach (BodyWithModel body in GetBodies(method))
            {
                foreach (SyntaxNode node in body.Body.DescendantNodesAndSelf())
                {
                    if (node is not SimpleNameSyntax name || IsInsideNameOf(name))
                        continue;

                    ISymbol? symbol = body.Model.GetSymbolInfo(name).Symbol;
                    if (IsHasChanges(symbol) && IsWriteTarget(GetAccessExpression(name)))
                        return true;

                    foreach (IMethodSymbol callee in ResolveCallees(body.Model, name))
                    {
                        if (IsOwnTypeChainMember(callee)
                            && callee.DeclaringSyntaxReferences.Length > 0
                            && MarksChangedCore(callee, visited))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool IsHasChanges(ISymbol? symbol)
            => symbol is IPropertySymbol { Name: HasChangesPropertyName } property
               && SymbolEqualityComparer.Default.Equals(
                   property.ContainingType?.OriginalDefinition,
                   renderNodeType);

        private bool IsTrackedInstanceState(ISymbol? symbol)
        {
            if (symbol is null || symbol.IsStatic || !IsOwnTypeChainMember(symbol))
                return false;

            return symbol switch
            {
                IFieldSymbol { IsConst: false, AssociatedSymbol: null } => true,

                // A hand-written property is skipped: its setter body assigns the backing field, and that
                // assignment is what gets reported instead - once, where the value actually changes.
                IPropertySymbol property => IsAutoProperty(property),
                _ => false,
            };
        }

        private static bool IsAutoProperty(IPropertySymbol property)
        {
            foreach (SyntaxReference reference in property.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not PropertyDeclarationSyntax
                    {
                        ExpressionBody: null,
                        AccessorList: { } accessors,
                    })
                {
                    return false;
                }

                foreach (AccessorDeclarationSyntax accessor in accessors.Accessors)
                {
                    if (accessor.Body is not null || accessor.ExpressionBody is not null)
                        return false;
                }
            }

            return property.DeclaringSyntaxReferences.Length > 0;
        }

        private bool IsOwnTypeChainMember(ISymbol symbol)
        {
            INamedTypeSymbol? container = symbol.ContainingType?.OriginalDefinition;
            if (container is null)
                return false;

            for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, container))
                    return true;
            }

            return false;
        }

        private IEnumerable<BodyWithModel> GetBodies(IMethodSymbol method)
        {
            foreach (SyntaxReference reference in method.DeclaringSyntaxReferences)
            {
                SyntaxNode declaration = reference.GetSyntax();
                SyntaxNode? body = declaration switch
                {
                    BaseMethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
                    AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody,
                    LocalFunctionStatementSyntax f => (SyntaxNode?)f.Body ?? f.ExpressionBody,
                    ArrowExpressionClauseSyntax arrow => arrow,
                    _ => null,
                };

                if (body is null || !compilation.ContainsSyntaxTree(body.SyntaxTree))
                    continue;

                yield return new BodyWithModel(body, compilation.GetSemanticModel(body.SyntaxTree));
            }
        }

        private static IEnumerable<IMethodSymbol> ResolveCallees(SemanticModel model, SimpleNameSyntax name)
        {
            ISymbol? symbol = model.GetSymbolInfo(name).Symbol;
            switch (symbol)
            {
                case IMethodSymbol method:
                    yield return method;
                    break;
                case IPropertySymbol property:
                    if (property.GetMethod is { } getter)
                        yield return getter;
                    if (property.SetMethod is { } setter)
                        yield return setter;
                    break;
            }
        }

        private readonly record struct BodyWithModel(SyntaxNode Body, SemanticModel Model);
    }

    private static ExpressionSyntax GetAccessExpression(SimpleNameSyntax name)
        => name.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == name
            ? memberAccess
            : name;

    /// <remarks>
    /// A bare identifier inside an instance member is this node; a qualified one has to say so. Anything else
    /// names another object, whose staleness this node's mark does not decide.
    /// </remarks>
    private static bool IsOnThisInstance(SimpleNameSyntax name)
        => name.Parent is not MemberAccessExpressionSyntax memberAccess
           || memberAccess.Name != name
           || memberAccess.Expression is ThisExpressionSyntax or BaseExpressionSyntax;

    private static bool IsSimpleAssignmentTarget(ExpressionSyntax expression)
        => expression.Parent is AssignmentExpressionSyntax assignment
           && assignment.Left == expression
           && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);

    private static bool IsWriteTarget(ExpressionSyntax expression)
    {
        switch (expression.Parent)
        {
            case AssignmentExpressionSyntax assignment when assignment.Left == expression:
                return true;

            case PrefixUnaryExpressionSyntax prefix:
                return prefix.IsKind(SyntaxKind.PreIncrementExpression)
                       || prefix.IsKind(SyntaxKind.PreDecrementExpression);

            case PostfixUnaryExpressionSyntax postfix:
                return postfix.IsKind(SyntaxKind.PostIncrementExpression)
                       || postfix.IsKind(SyntaxKind.PostDecrementExpression);

            case ArgumentSyntax argument:
                return argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword)
                       || argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword);

            default:
                return false;
        }
    }

    private static bool IsInsideNameOf(SyntaxNode node)
    {
        for (SyntaxNode? current = node; current is not null; current = current.Parent)
        {
            if (current is InvocationExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.ValueText: "nameof" },
                })
            {
                return true;
            }

            if (current is MemberDeclarationSyntax or AnonymousFunctionExpressionSyntax)
                return false;
        }

        return false;
    }
}
