using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Beutl.Engine.SourceGenerators.Analyzers;

/// <summary>
/// Reports <c>RenderNode</c> state that can change without marking the node changed.
/// </summary>
/// <remarks>
/// The analyzer checks mutations outside the <c>Process</c> call graph and externally writable fields,
/// auto-properties, and field-like events. The runtime cross-check covers ambiguous mutations inside the
/// call graph, where memoization may be valid.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed partial class RenderNodeChangeMarkingAnalyzer : DiagnosticAnalyzer
{
    private const string RenderNodeMetadataName = "Beutl.Graphics.Rendering.RenderNode";
    private const string ProcessMethodName = "Process";
    private const string MarkChangedMethodName = "MarkChanged";
    private const string DisposeCallbackName = "OnDispose";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.UnmarkedRenderNodeMutation);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // RenderNode is looked up once per compilation rather than once per named type: every type
        // declared in the compilation reaches this rule, and only the few deriving from RenderNode go any
        // further.
        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(RenderNodeMetadataName) is not { } renderNodeType)
                return;

            // A symbol action sees all declarations of a partial node together.
            start.RegisterSymbolAction(
                symbol => AnalyzeNamedType(symbol, renderNodeType),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context, INamedTypeSymbol renderNodeType)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsStatic: false } type)
            return;

        if (!InheritsFrom(type, renderNodeType))
            return;

        IMethodSymbol? process = FindProcessMethod(type, renderNodeType);
        if (process is null)
            return;

        var analysis = new TypeAnalysis(context.Compilation, type, renderNodeType, context.CancellationToken);
        ImmutableHashSet<ISymbol> processClosure = analysis.CollectCallClosure(process);
        for (INamedTypeSymbol? current = type; current is not null && !SymbolEqualityComparer.Default.Equals(current, renderNodeType); current = current.BaseType)
        {
            if (current.GetMembers("ChildNodes").OfType<IPropertySymbol>().FirstOrDefault()?.GetMethod is { } getter)
            {
                processClosure = processClosure.Union(analysis.CollectCallClosure(getter));
                break;
            }
        }
        ImmutableHashSet<ISymbol> readState = analysis.CollectReadInstanceState(processClosure);
        if (readState.IsEmpty)
            return;

        ReportUnmarkedMutators(context, analysis, type, renderNodeType, processClosure, readState);
        ReportExternallyWritableState(context, analysis, type, renderNodeType, readState);
        List<ReportedByBase> reportedByBases = CollectReportsByBaseTypes(analysis, type, renderNodeType);
        foreach (IMethodSymbol callback in analysis.ConstructorSubscriptions(type))
        {
            if (analysis.MarksChanged(callback)) continue;
            foreach (StateAssignment assignment in analysis.FindStateAssignments(callback, readState))
            {
                if (IsReportedByABaseType(reportedByBases, callback, assignment.State))
                    continue;
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnmarkedRenderNodeMutation, assignment.Location,
                    type.Name, "constructor subscription", assignment.State.Name, CallMarkChanged));
            }
        }
    }

    /// <summary>Reports the writes to <paramref name="readState"/> made by the methods this node runs.</summary>
    /// <remarks>
    /// <para>
    /// The whole type chain below <c>RenderNode</c>, not just the node's own member list: what a node records
    /// is decided by everything its <c>Process</c> reads, and a base type is free to declare both the state
    /// and the mutator that writes it. A derived node with an inherited mutator goes as stale as one that
    /// declares its own, and the base cannot report it, because the read set that makes the state matter
    /// belongs to a <c>Process</c> the base does not know about.
    /// </para>
    /// <para>
    /// Which members are read at all is decided by <see cref="CollectReachedFromUnmarkedEntryPoints"/>: a
    /// write nothing can reach without marking is not a node going stale, wherever in the chain it is
    /// written.
    /// </para>
    /// <para>
    /// A write a base type's own analysis already reports is left to it, so one line is not reported once
    /// per type that inherits it. That is asked as the base would ask it - of the state the <c>Process</c>
    /// the base is analyzed under reads, and of what the base's own entry points reach - because a base
    /// that reports nothing, for either reason, leaves the write to whoever can see it.
    /// </para>
    /// </remarks>
    private static void ReportUnmarkedMutators(
        SymbolAnalysisContext context,
        TypeAnalysis analysis,
        INamedTypeSymbol type,
        INamedTypeSymbol renderNodeType,
        ImmutableHashSet<ISymbol> processClosure,
        ImmutableHashSet<ISymbol> readState)
    {
        HashSet<ISymbol> overridden = CollectOverriddenMethods(type, renderNodeType);
        ImmutableHashSet<ISymbol> reachedUnmarked =
            CollectReachedFromUnmarkedEntryPoints(analysis, type, renderNodeType, processClosure, overridden);

        List<ReportedByBase> reportedByBases = CollectReportsByBaseTypes(analysis, type, renderNodeType);

        foreach (IMethodSymbol method in EnumerateChainMethods(type, renderNodeType))
        {
            if (!RunsBetweenRecordings(method, renderNodeType, processClosure, overridden)
                || !reachedUnmarked.Contains(method.OriginalDefinition)
                || analysis.MarksChanged(method))
            {
                continue;
            }

            foreach (StateAssignment assignment in analysis.FindStateAssignments(method, readState))
            {
                if (IsReportedByABaseType(reportedByBases, method, assignment.State))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnmarkedRenderNodeMutation,
                    assignment.Location,
                    type.Name,
                    DescribeMember(method),
                    assignment.State.Name,
                    CallMarkChanged));
            }
        }
    }

    /// <summary>What a base type's own analysis of this rule reports: the state it reads, and the members it reads.</summary>
    private readonly record struct ReportedByBase(
        ImmutableHashSet<ISymbol> State,
        ImmutableHashSet<ISymbol> Members);

    private static List<ReportedByBase> CollectReportsByBaseTypes(
        TypeAnalysis analysis,
        INamedTypeSymbol type,
        INamedTypeSymbol renderNodeType)
    {
        var reported = new List<ReportedByBase>();

        for (INamedTypeSymbol? declaring = type.BaseType;
             declaring is not null
             && !SymbolEqualityComparer.Default.Equals(declaring.OriginalDefinition, renderNodeType);
             declaring = declaring.BaseType)
        {
            ImmutableHashSet<ISymbol> state = analysis.ReadStateOfProcessFor(declaring, renderNodeType);
            if (state.IsEmpty || FindProcessMethod(declaring, renderNodeType) is not { } process)
                continue;

            ImmutableHashSet<ISymbol> members = CollectReachedFromUnmarkedEntryPoints(
                analysis,
                declaring,
                renderNodeType,
                analysis.CollectCallClosure(process),
                CollectOverriddenMethods(declaring, renderNodeType));

            members = members.Union(analysis.ConstructorSubscriptions(declaring)
                .Where(callback => !analysis.MarksChanged(callback))
                .Select(callback => (ISymbol)callback.OriginalDefinition));
            if (!members.IsEmpty)
                reported.Add(new ReportedByBase(state, members));
        }

        return reported;
    }

    private static bool IsReportedByABaseType(
        List<ReportedByBase> reportedByBases,
        IMethodSymbol method,
        ISymbol state)
    {
        foreach (ReportedByBase reported in reportedByBases)
        {
            if (reported.State.Contains(state.OriginalDefinition) && reported.Members.Contains(method.OriginalDefinition))
                return true;
        }

        return false;
    }

    /// <summary>The methods reachable from a member that can be run without marking the node changed.</summary>
    /// <remarks>
    /// <para>
    /// A write is only a node going stale if something can run it and leave the mark unraised. Whether that
    /// is possible is asked of whoever can call the member, not of the member alone, because a helper that
    /// reports what it changed and leaves the marking to its caller is how a node splits an update across a
    /// type chain - the shape the engine's own brush nodes are written in, where a protected
    /// <c>Update</c> returns whether anything moved and each derived <c>Update</c> marks for the whole
    /// change. Reading the helper by itself would report every node that inherits it, for a mark each one
    /// already makes.
    /// </para>
    /// <para>
    /// An entry point is a member code outside this node's own inheritance chain can reach: public,
    /// internal, protected internal, or an explicit interface implementation. Protected and private are
    /// not, because the only callers they have are the chain's own members and the types that derive from
    /// it - and a derived type is a node of its own, analyzed with its own <c>Process</c> and its own
    /// entry points, which is where a caller of its that forgets to mark is reported. So the question this
    /// answers is narrow: does this node have a way in that reaches the write without marking?
    /// </para>
    /// </remarks>
    private static ImmutableHashSet<ISymbol> CollectReachedFromUnmarkedEntryPoints(
        TypeAnalysis analysis,
        INamedTypeSymbol type,
        INamedTypeSymbol renderNodeType,
        ImmutableHashSet<ISymbol> processClosure,
        HashSet<ISymbol> overridden)
    {
        var reached = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);

        foreach (IMethodSymbol method in EnumerateChainMethods(type, renderNodeType))
        {
            if (RunsBetweenRecordings(method, renderNodeType, processClosure, overridden)
                && IsReachableFromOutsideTheChain(method)
                && !analysis.MarksChanged(method))
            {
                reached.UnionWith(analysis.CollectCallClosure(method));
            }
        }

        return reached.ToImmutable();
    }

    /// <summary>The base methods a more derived type in the chain replaces.</summary>
    /// <remarks>
    /// An overridden body does not run for this node. Something can still reach it through <c>base</c>, and
    /// a call like that is what puts it in the caller's own call closure, so nothing is lost by leaving the
    /// declaration itself out.
    /// </remarks>
    private static HashSet<ISymbol> CollectOverriddenMethods(
        INamedTypeSymbol type,
        INamedTypeSymbol renderNodeType)
    {
        var overridden = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (IMethodSymbol method in EnumerateChainMethods(type, renderNodeType))
        {
            if (method.OverriddenMethod is { } replaced)
                overridden.Add(replaced.OriginalDefinition);
        }

        return overridden;
    }

    /// <summary>The methods the node's own type chain declares, most derived first.</summary>
    /// <remarks>
    /// <c>RenderNode</c> itself is left out. Its version counters are the mechanism this rule reports
    /// against, so a <c>Process</c> reading <c>HasChanges</c> would otherwise have <c>MarkChanged</c>
    /// reported as an unmarked mutation of the node.
    /// </remarks>
    private static IEnumerable<IMethodSymbol> EnumerateChainMethods(
        INamedTypeSymbol type,
        INamedTypeSymbol renderNodeType)
    {
        for (INamedTypeSymbol? declaring = type;
             declaring is not null
             && !SymbolEqualityComparer.Default.Equals(declaring.OriginalDefinition, renderNodeType);
             declaring = declaring.BaseType)
        {
            foreach (ISymbol member in declaring.GetMembers())
            {
                if (member is IMethodSymbol method)
                    yield return method;
            }
        }
    }

    /// <summary>Whether <paramref name="method"/> can run between one recording of this node and the next.</summary>
    private static bool RunsBetweenRecordings(
        IMethodSymbol method,
        INamedTypeSymbol renderNodeType,
        ImmutableHashSet<ISymbol> processClosure,
        HashSet<ISymbol> overridden)
        // Constructors precede recording, and teardown follows the last recording.
        => method.MethodKind is not (MethodKind.Constructor
               or MethodKind.StaticConstructor
               or MethodKind.Destructor)
           && !method.IsStatic
           && !IsDisposalOverride(method, renderNodeType)
           && !processClosure.Contains(method.OriginalDefinition)
           && !overridden.Contains(method.OriginalDefinition);

    private static bool IsReachableFromOutsideTheChain(IMethodSymbol method)
        => method.ExplicitInterfaceImplementations.Length > 0
           || method.DeclaredAccessibility is Accessibility.Public
               or Accessibility.Internal
               or Accessibility.ProtectedOrInternal;

    private const string CallMarkChanged = "Call MarkChanged() where the value changes";

    private static bool IsDisposalOverride(IMethodSymbol method, INamedTypeSymbol renderNodeType)
        => method.Name == DisposeCallbackName && OverridesRenderNodeMember(method, renderNodeType);

    private static bool OverridesRenderNodeMember(IMethodSymbol method, INamedTypeSymbol renderNodeType)
    {
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

    private static IMethodSymbol? FindProcessMethod(INamedTypeSymbol type, INamedTypeSymbol renderNodeType)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (ISymbol member in current.GetMembers(ProcessMethodName))
            {
                if (member is IMethodSymbol { IsStatic: false } method
                    && method.DeclaringSyntaxReferences.Length > 0
                    && OverridesRenderNodeMember(method, renderNodeType))
                {
                    return method;
                }
            }
        }

        return null;
    }

    private readonly record struct StateAssignment(ISymbol State, Location Location);

    private readonly record struct StateReference(ISymbol? Symbol, ExpressionSyntax Access, bool OnThisInstance);

    private static StateReference? GetStateReference(SemanticModel model, SyntaxNode node)
    {
        switch (node)
        {
            case SimpleNameSyntax name when !IsInsideNameOf(name):
                return new StateReference(
                    model.GetSymbolInfo(name).Symbol,
                    MemberAccessSyntax.GetAccessExpression(name),
                    IsOnThisInstance(name));

            // field names the backing store of the property being declared, and no receiver can be written
            // for it, so it is always this instance's.
            case FieldExpressionSyntax fieldExpression:
                return new StateReference(model.GetSymbolInfo(fieldExpression).Symbol, fieldExpression, true);

            default:
                return null;
        }
    }
}
