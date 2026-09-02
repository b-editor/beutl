using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
public sealed class RenderNodeChangeMarkingAnalyzer : DiagnosticAnalyzer
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

        // A symbol action sees all declarations of a partial node together.
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol { TypeKind: TypeKind.Class, IsStatic: false } type)
            return;

        INamedTypeSymbol? renderNodeType = context.Compilation.GetTypeByMetadataName(RenderNodeMetadataName);
        if (renderNodeType is null || !InheritsFrom(type, renderNodeType))
            return;

        IMethodSymbol? process = FindProcessMethod(type, renderNodeType);
        if (process is null)
            return;

        var analysis = new TypeAnalysis(context.Compilation, type, renderNodeType);
        ImmutableHashSet<ISymbol> processClosure = analysis.CollectCallClosure(process);
        ImmutableHashSet<ISymbol> readState = analysis.CollectReadInstanceState(processClosure);
        if (readState.IsEmpty)
            return;

        ReportUnmarkedMutators(context, analysis, type, renderNodeType, processClosure, readState);
        ReportExternallyWritableState(context, type, readState);
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
                || !reachedUnmarked.Contains(method)
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
            if (reported.State.Contains(state) && reported.Members.Contains(method))
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
           && !processClosure.Contains(method)
           && !overridden.Contains(method.OriginalDefinition);

    private static bool IsReachableFromOutsideTheChain(IMethodSymbol method)
        => method.ExplicitInterfaceImplementations.Length > 0
           || method.DeclaredAccessibility is Accessibility.Public
               or Accessibility.Internal
               or Accessibility.ProtectedOrInternal;

    private static void ReportExternallyWritableState(
        SymbolAnalysisContext context,
        INamedTypeSymbol type,
        ImmutableHashSet<ISymbol> readState)
    {
        foreach (ISymbol member in type.GetMembers())
        {
            if (!readState.Contains(member) || GetExternalWrite(member) is not { } write)
                continue;

            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.UnmarkedRenderNodeMutation,
                write.Location,
                type.Name,
                write.Writer,
                member.Name,
                write.Fix));
        }
    }

    private readonly record struct ExternalWrite(string Writer, string Fix, Location Location);

    private static ExternalWrite? GetExternalWrite(ISymbol member) => member switch
    {
        IPropertySymbol property when IsAutoProperty(property)
            && property.SetMethod is
            {
                IsInitOnly: false,
                DeclaredAccessibility: not Accessibility.Private,
            } setter
            => new ExternalWrite(
                property.Name + ".set",
                MarkFromTheSetter,
                setter.Locations.FirstOrDefault() ?? property.Locations.FirstOrDefault() ?? Location.None),

        IEventSymbol { DeclaredAccessibility: not Accessibility.Private } @event when IsFieldLikeEvent(@event)
            => new ExternalWrite(
                @event.Name + ".add",
                MarkFromTheAccessors,
                @event.Locations.FirstOrDefault() ?? Location.None),

        IFieldSymbol
        {
            IsReadOnly: false,
            AssociatedSymbol: null,
            DeclaredAccessibility: not Accessibility.Private,
        } field
            => new ExternalWrite(
                field.Name,
                NarrowTheField,
                field.Locations.FirstOrDefault() ?? Location.None),

        _ => null,
    };

    private const string CallMarkChanged = "Call MarkChanged() where the value changes";

    private const string MarkFromTheSetter =
        "Give the setter a body that assigns the value and calls MarkChanged(), or make it private or "
        + "init-only so that only this node's own code can assign it";

    private const string MarkFromTheAccessors =
        "Give the event add and remove accessors that subscribe and call MarkChanged(), or make it private "
        + "so that only this node's own code can subscribe to it";

    private const string NarrowTheField =
        "Make the field private or readonly so that code outside this node cannot assign it, or replace it "
        + "with a property whose setter assigns the value and calls MarkChanged()";

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

    private static bool IsFieldLikeEvent(IEventSymbol @event)
        => @event.AddMethod is { IsImplicitlyDeclared: true };

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
                    GetAccessExpression(name),
                    IsOnThisInstance(name));

            // field names the backing store of the property being declared, and no receiver can be written
            // for it, so it is always this instance's.
            case FieldExpressionSyntax fieldExpression:
                return new StateReference(model.GetSymbolInfo(fieldExpression).Symbol, fieldExpression, true);

            default:
                return null;
        }
    }

    private sealed class TypeAnalysis(
        Compilation compilation,
        INamedTypeSymbol type,
        INamedTypeSymbol renderNodeType)
    {
        private readonly Dictionary<IMethodSymbol, ImmutableHashSet<ISymbol>> _readStateByProcess =
            new(SymbolEqualityComparer.Default);

        private readonly Dictionary<IMethodSymbol, bool> _marksChanged = new(SymbolEqualityComparer.Default);

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

        /// <summary>The instance state read by the <c>Process</c> <paramref name="declaring"/> is analyzed under.</summary>
        /// <remarks>
        /// What a base type's own run of this rule would have as its read set, which is what makes a write it
        /// declares already reported there. A base with no <c>Process</c> of its own answers with the nearest
        /// one above it, and an abstract <c>Process</c> - or one whose source is in another assembly - has no
        /// body and so contributes nothing, which is exactly the case that leaves the base unanalyzed.
        /// </remarks>
        public ImmutableHashSet<ISymbol> ReadStateOfProcessFor(
            INamedTypeSymbol declaring,
            INamedTypeSymbol renderNode)
        {
            if (FindProcessMethod(declaring, renderNode) is not { } process)
                return ImmutableHashSet<ISymbol>.Empty;

            if (_readStateByProcess.TryGetValue(process, out ImmutableHashSet<ISymbol>? cached))
                return cached;

            ImmutableHashSet<ISymbol> read = CollectReadInstanceState(CollectCallClosure(process));
            _readStateByProcess[process] = read;
            return read;
        }

        /// <summary>The instance state the given bodies read.</summary>
        public ImmutableHashSet<ISymbol> CollectReadInstanceState(ImmutableHashSet<ISymbol> methods)
        {
            var read = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
            foreach (IMethodSymbol method in methods.OfType<IMethodSymbol>())
            {
                foreach (BodyWithModel body in GetBodies(method))
                {
                    foreach (SyntaxNode node in body.Body.DescendantNodesAndSelf())
                    {
                        if (GetStateReference(body.Model, node) is not { Symbol: { } symbol } reference
                            || !IsTrackedInstanceState(symbol))
                        {
                            continue;
                        }

                        // A simple assignment overwrites without reading, so the target alone does not make
                        // the member part of what Process depends on.
                        if (!IsSimpleAssignmentTarget(reference.Access))
                            read.Add(symbol);
                    }
                }
            }

            return read.ToImmutable();
        }

        /// <summary>Whether a <c>MarkChanged</c> call on this node is reachable from <paramref name="method"/>.</summary>
        /// <remarks>
        /// <para>
        /// Only a call on this instance counts. Marking another node says nothing about whether this one's
        /// own recording went stale, and accepting it would excuse the mutation this rule is looking at.
        /// </para>
        /// <para>
        /// Path-insensitive by design: one call anywhere in the member, or in a method of the same type it
        /// calls, clears every assignment in that member. A mutation on a branch that skips the mark is
        /// therefore missed, which is the direction this rule errs in. Naming <c>MarkChanged</c> clears the
        /// member as much as calling it does, so handing the method group to a scheduler or storing it in a
        /// delegate counts: the suppression is by symbol, not by invocation.
        /// </para>
        /// <para>
        /// Anywhere in the member means anywhere the member runs, which is the one place path-insensitivity
        /// stops. A nested function the body cannot reach is not walked, and a call the compiler removes is
        /// not followed - see <see cref="RunsNestedFunction"/> and
        /// <see cref="ConditionalCompilation.IsCallCompiled"/> - because a mark that is not in the program
        /// the author ships leaves the node exactly as stale as no mark at all, and this is the rule's one
        /// unrecoverable failure: silence here is what the author reads as approval.
        /// </para>
        /// </remarks>
        public bool MarksChanged(IMethodSymbol method)
        {
            if (_marksChanged.TryGetValue(method, out bool cached))
                return cached;

            var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            bool marks = MarksChangedCore(method, visited);
            _marksChanged[method] = marks;
            return marks;
        }

        /// <summary>The writes to state <c>Process</c> reads that <paramref name="method"/> makes.</summary>
        /// <remarks>
        /// Only the parts of the member that run, on the same terms <see cref="MarksChanged"/> reads it: an
        /// assignment in a body nothing reaches changes nothing, and reporting one while refusing to see the
        /// mark written beside it would be a diagnostic that the node is stale, aimed at code the program
        /// never executes. Every local function this walk skips is one no name in the member reaches, since
        /// nothing here follows calls to find the rest.
        /// </remarks>
        public IEnumerable<StateAssignment> FindStateAssignments(
            IMethodSymbol method,
            ImmutableHashSet<ISymbol> trackedState)
        {
            foreach (BodyWithModel body in GetBodies(method))
            {
                Dictionary<ISymbol, ISymbol> aliases = CollectRefLocalAliases(body, trackedState);

                foreach (SyntaxNode node in body.Body.DescendantNodesAndSelf(
                    child => RunsNestedFunction(
                        body.Model,
                        body.Body,
                        child,
                        localFunctionsFollowedAsCallees: false)))
                {
                    if (GetStateReference(body.Model, node) is not { Symbol: { } symbol } reference)
                        continue;

                    // A ref local is the state itself under another name, and the name carries no receiver
                    // to ask about: which storage it aliases was decided where it was declared.
                    if (aliases.TryGetValue(symbol, out ISymbol? aliased))
                    {
                        if (ChangesTheValueBehind(reference.Access))
                            yield return new StateAssignment(aliased, reference.Access.GetLocation());

                        continue;
                    }

                    if (!trackedState.Contains(symbol))
                        continue;

                    // An assignment to another instance of the same type is a different object's state, and
                    // marking this node changed would say nothing about it.
                    if (!reference.OnThisInstance)
                        continue;

                    if (ChangesTheValueBehind(reference.Access))
                        yield return new StateAssignment(symbol, reference.Access.GetLocation());
                }
            }
        }

        private static Dictionary<ISymbol, ISymbol> CollectRefLocalAliases(
            BodyWithModel body,
            ImmutableHashSet<ISymbol> trackedState)
        {
            var aliases = new Dictionary<ISymbol, ISymbol>(SymbolEqualityComparer.Default);

            foreach (SyntaxNode node in body.Body.DescendantNodesAndSelf(
                child => RunsNestedFunction(
                    body.Model,
                    body.Body,
                    child,
                    localFunctionsFollowedAsCallees: false)))
            {
                switch (node)
                {
                    case VariableDeclaratorSyntax { Initializer.Value: RefExpressionSyntax aliased } declarator
                        when body.Model.GetDeclaredSymbol(declarator) is ILocalSymbol { IsRef: true } local:
                        if (GetAliasedState(body.Model, aliases, trackedState, aliased) is { } state)
                            aliases[local] = state;

                        break;

                    // alias = ref other rebinds which storage the name reaches, and this reads a body in
                    // source order rather than along its paths, so a rebound alias is one whose referent it
                    // cannot say. Dropping it can cost a report and can never invent one.
                    case AssignmentExpressionSyntax { Right: RefExpressionSyntax } rebind
                        when body.Model.GetSymbolInfo(rebind.Left).Symbol is { } rebound:
                        aliases.Remove(rebound);
                        break;
                }
            }

            return aliases;
        }

        private static ISymbol? GetAliasedState(
            SemanticModel model,
            Dictionary<ISymbol, ISymbol> aliases,
            ImmutableHashSet<ISymbol> trackedState,
            RefExpressionSyntax aliased)
        {
            if (GetStateReference(model, GetAliasedName(aliased.Expression))
                is not { Symbol: { } symbol } reference)
            {
                return null;
            }

            if (aliases.TryGetValue(symbol, out ISymbol? chained))
                return chained;

            return reference.OnThisInstance && trackedState.Contains(symbol) ? symbol : null;
        }

        private static SyntaxNode GetAliasedName(ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case ParenthesizedExpressionSyntax parenthesized:
                        expression = parenthesized.Expression;
                        continue;

                    case ElementAccessExpressionSyntax element:
                        expression = element.Expression;
                        continue;

                    case MemberAccessExpressionSyntax memberAccess:
                        return memberAccess.Name;

                    default:
                        return expression;
                }
            }
        }

        private bool MarksChangedCore(IMethodSymbol method, HashSet<ISymbol> visited)
        {
            if (!visited.Add(method))
                return false;

            foreach (BodyWithModel body in GetBodies(method))
            {
                foreach (SyntaxNode node in body.Body.DescendantNodesAndSelf(
                    child => RunsNestedFunction(
                        body.Model,
                        body.Body,
                        child,
                        localFunctionsFollowedAsCallees: true)))
                {
                    if (node is not SimpleNameSyntax name || IsInsideNameOf(name))
                        continue;

                    // A helper reached through another instance marks that instance, however bare the
                    // MarkChanged call inside its body looks, so the receiver decides both questions below.
                    if (!IsOnThisInstance(name))
                        continue;

                    ISymbol? symbol = body.Model.GetSymbolInfo(name).Symbol;

                    // A call the compiler removes is not a mark; asked here so both branches answer alike.
                    if (symbol is IMethodSymbol called
                        && !ConditionalCompilation.IsCallCompiled(compilation, called, name.SyntaxTree))
                    {
                        continue;
                    }

                    if (IsMarkChanged(symbol))
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

        private static bool RunsNestedFunction(
            SemanticModel model,
            SyntaxNode body,
            SyntaxNode nested,
            bool localFunctionsFollowedAsCallees)
            => nested switch
            {
                LocalFunctionStatementSyntax when localFunctionsFollowedAsCallees => false,

                LocalFunctionStatementSyntax function
                    => model.GetDeclaredSymbol(function) is not { } declared
                       || IsNamedOutside(model, body, declared, function),

                AnonymousFunctionExpressionSyntax lambda => RunsLambda(model, body, lambda),

                _ => true,
            };

        private static bool RunsLambda(
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
                && model.GetSymbolInfo(assignment.Left).Symbol is IDiscardSymbol)
            {
                return false;
            }

            if (stored.Parent is EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax declarator }
                && model.GetDeclaredSymbol(declarator) is ILocalSymbol local)
            {
                return IsNamedOutside(model, body, local, lambda);
            }

            return true;
        }

        private static bool IsNamedOutside(
            SemanticModel model,
            SyntaxNode body,
            ISymbol symbol,
            SyntaxNode declaration)
        {
            foreach (SyntaxNode node in body.DescendantNodesAndSelf())
            {
                // The text test is what keeps this affordable: it costs a string compare per node and leaves
                // a semantic query only for the names that could be this one.
                if (node is not IdentifierNameSyntax name
                    || name.Identifier.ValueText != symbol.Name
                    || declaration.Span.Contains(name.Span))
                {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(name).Symbol, symbol))
                    return true;
            }

            return false;
        }

        private bool IsMarkChanged(ISymbol? symbol)
            => symbol is IMethodSymbol { Name: MarkChangedMethodName, IsStatic: false } method
               && SymbolEqualityComparer.Default.Equals(
                   method.ContainingType?.OriginalDefinition,
                   renderNodeType);

        private bool IsTrackedInstanceState(ISymbol? symbol)
        {
            if (symbol is null || symbol.IsStatic || !IsOwnTypeChainMember(symbol))
                return false;

            return symbol switch
            {
                IFieldSymbol { IsConst: false, AssociatedSymbol: null } => true,

                // The backing field a property body names with the field keyword. Nothing else in source can
                // reach it, so tracking it reports the setter that writes it and never doubles up with the
                // property itself - a property with a body is not an auto-property, and an auto-property has
                // no body to name the field from.
                IFieldSymbol { IsConst: false, AssociatedSymbol: IPropertySymbol } => true,

                // A hand-written property is skipped: its setter body assigns the backing field, and that
                // assignment is what gets reported instead - once, where the value actually changes.
                IPropertySymbol property => IsAutoProperty(property),

                // A field-like event's subscriber list lives in a delegate field a source type's member
                // list leaves out, so the event is the only name this walk can track that field by. An
                // event with accessors is skipped for the reason a hand-written property is.
                IEventSymbol @event => IsFieldLikeEvent(@event),
                _ => false,
            };
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
                    ExpressionSyntax access = GetAccessExpression(name);
                    if (!IsSimpleAssignmentTarget(access) && property.GetMethod is { } getter)
                        yield return getter;
                    if (IsWriteTarget(access) && property.SetMethod is { } setter)
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

    private static bool IsOnThisInstance(SimpleNameSyntax name)
    {
        switch (name.Parent)
        {
            case MemberAccessExpressionSyntax memberAccess when memberAccess.Name == name:
                return memberAccess.Expression is ThisExpressionSyntax or BaseExpressionSyntax;

            // A conditional access spells its receiver once, in the expression that guards the whole chain,
            // so the binding beside the name carries no receiver of its own.
            case MemberBindingExpressionSyntax binding when binding.Name == name:
                return ConditionalAccessSyntax.FindReceiver(binding)
                    is ThisExpressionSyntax or BaseExpressionSyntax;

            default:
                return true;
        }
    }

    private static bool IsSimpleAssignmentTarget(ExpressionSyntax expression)
        => (expression.Parent is AssignmentExpressionSyntax assignment
            && assignment.Left == expression
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
           || (expression.Parent is ArgumentSyntax argument && IsDeconstructionTarget(argument));

    private static bool ChangesTheValueBehind(ExpressionSyntax expression)
        => IsWriteTarget(expression)
           || (expression.Parent is ElementAccessExpressionSyntax element
               && element.Expression == expression
               && ChangesTheValueBehind(element));

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
                       || argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)
                       || IsDeconstructionTarget(argument);

            default:
                return false;
        }
    }

    private static bool IsDeconstructionTarget(ArgumentSyntax argument)
        => argument.Parent is TupleExpressionSyntax tuple && IsWriteTarget(tuple);

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
