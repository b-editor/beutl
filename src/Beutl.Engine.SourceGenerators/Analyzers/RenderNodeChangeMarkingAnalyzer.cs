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
/// <c>RenderNode.MarkChanged</c> is public, so an out-of-tree node decides for itself when to call it. Today
/// a node that forgets is still re-recorded every frame and only loses the pixel cache, which is why the
/// omission has been survivable. Once a recorded graph may be reused for a node that reports no changes,
/// forgetting means the node is not re-recorded at all and renders stale, and no compile error says so.
/// </para>
/// <para>
/// What a rule can decide here is bounded, and the bound is the point rather than an apology for it. This
/// reports two shapes, both about state the node's <c>Process</c> reads: an assignment written in the node's
/// own type to an instance field or auto-property, or to an element of one, and a member the node declares -
/// an auto-property, a field-like event, or a field - that anyone outside it can write. Those are the shapes
/// authors write, and a rule that guessed past them on a public extension point would be suppressed
/// wholesale and then protect nothing. The runtime cross-check in <c>Beutl.Engine</c> is what covers the
/// rest; silence here is not a proof.
/// </para>
/// <para>
/// The second shape is asked at the declaration because there is nowhere else to ask it. A synthesized
/// setter, a field-like event's accessors and a field have no body to read, and the write is made by whoever
/// holds the node, in code this rule is not looking at - so both halves of the first shape are missing while
/// the node still goes stale.
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
    private const string MarkChangedMethodName = "MarkChanged";
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

        IMethodSymbol? process = FindProcessMethod(type, renderNodeType);
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
                    assignment.State.Name,
                    CallMarkChanged));
            }
        }

        ReportExternallyWritableState(context, type, readState);
    }

    /// <summary>
    /// Reports a member of this node that code outside it can write, and whose value <c>Process</c> reads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A synthesized setter, a field-like event's accessors and a field have no body anywhere, so the walk
    /// over member bodies above yields nothing for them, and nobody inside the node writes them either: the
    /// write is made by whoever holds the node, in code this rule is not looking at. Both halves of the shape
    /// that reports an assignment are therefore absent, and the node is stale from the moment the write
    /// lands.
    /// </para>
    /// <para>
    /// So this half is asked at the declaration rather than at a call site, and it is the declaration the
    /// author can fix: give the member a body that marks, or narrow it until only the node's own code -
    /// which the assignment shape does read - can reach it.
    /// </para>
    /// <para>
    /// Only a member the node's own type declares, so that a node inheriting one is not reported a second
    /// time at the same location; the type that declares it is the type analyzed for it.
    /// </para>
    /// </remarks>
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

    /// <summary>How code outside the node writes a member of it, and what the author can do about it.</summary>
    private readonly record struct ExternalWrite(string Writer, string Fix, Location Location);

    /// <returns>
    /// The write code outside the node can make, or <see langword="null"/> when there is none.
    /// </returns>
    /// <remarks>
    /// <para>
    /// An init accessor runs only while the object is being made, which is before there is a recording to
    /// invalidate - the same reason a constructor assignment is not reported, and the same reason a readonly
    /// field is not one of these writes. A private member is reachable only from the node's own code, and
    /// every write made there is already read by the assignment shape, which also accepts the ones that
    /// mark; reporting the declaration too would reject a node that marks on every path into it.
    /// </para>
    /// <para>
    /// A property setter with a body of its own, and an event whose accessors have bodies, are likewise the
    /// assignment shape, reported where the value actually changes.
    /// </para>
    /// </remarks>
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

        // += and -= are the assignments of the delegate field a field-like event stands for, and they bind
        // from wherever the event is visible.
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

    /// <summary>Whether every accessor of <paramref name="property"/> is one the compiler synthesizes.</summary>
    /// <remarks>
    /// The single test both halves of this rule are judged by: an auto-property is state in its own right,
    /// because no body anywhere names the field behind it, while a property with a body is read through that
    /// body instead - the assignment inside its setter is what gets reported, once, where the value changes.
    /// </remarks>
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

    /// <summary>Whether the accessors of <paramref name="event"/> are ones the compiler synthesizes.</summary>
    /// <remarks>
    /// The event counterpart of <see cref="IsAutoProperty"/>, asked of the accessor rather than of the
    /// declaration because a field-like event declares no accessor syntax to read.
    /// </remarks>
    private static bool IsFieldLikeEvent(IEventSymbol @event)
        => @event.AddMethod is { IsImplicitlyDeclared: true };

    private static bool IsDisposalOverride(IMethodSymbol method, INamedTypeSymbol renderNodeType)
        => method.Name == DisposeCallbackName && OverridesRenderNodeMember(method, renderNodeType);

    /// <summary>Whether <paramref name="method"/> fills a virtual slot declared by <c>RenderNode</c> itself.</summary>
    /// <remarks>
    /// Sharing a name with a <c>RenderNode</c> member is not the same as being the member the engine calls. A
    /// node may declare an unrelated overload, or hide the member with <c>new</c>; neither is the body a
    /// virtual call through <c>RenderNode</c> reaches.
    /// </remarks>
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

    /// <summary>Finds the <c>Process</c> body a node of this type would actually run, if it is in source.</summary>
    /// <remarks>
    /// <para>
    /// A node that inherits <c>Process</c> is analyzed against the base's body, so only state that body names
    /// enters the read set. Whatever the base reaches through a virtual hook does not, which is one of the
    /// places this rule stops.
    /// </para>
    /// <para>
    /// The body is chosen by the slot it overrides rather than by name and arity, because an unrelated
    /// <c>Process</c> overload declared on the node would otherwise be read as the render path and leave the
    /// read set empty - silencing the rule over everything the inherited override really reads.
    /// </para>
    /// </remarks>
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

    /// <summary>A reference to a member, as the state walkers need to read it.</summary>
    private readonly record struct StateReference(ISymbol? Symbol, ExpressionSyntax Access, bool OnThisInstance);

    /// <summary>Reads <paramref name="node"/> as a reference to a member, if that is what it is.</summary>
    /// <remarks>
    /// The <c>field</c> keyword is a reference to instance state that never spells a name, so a walk that
    /// only looked at names could not see a property's backing field at all - neither the read that puts it
    /// in the read set nor the write that has to be marked.
    /// </remarks>
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
                Dictionary<ISymbol, ISymbol> aliases = CollectRefLocalAliases(body, trackedState);

                foreach (SyntaxNode node in body.Body.DescendantNodesAndSelf())
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

        /// <returns>The ref locals in the body that name state <c>Process</c> reads, and what each names.</returns>
        /// <remarks>
        /// <para>
        /// <c>ref var alias = ref _bounds;</c> puts the field's own reference under a <c>ref</c>, which
        /// writes nothing, and the write one statement later names only the local - so both halves of an
        /// ordinary mutator went past the rule while the node still rendered stale content.
        /// </para>
        /// <para>
        /// Taking the reference is not itself the change, which is why this is tracked rather than read as
        /// a write: a member that only reads through the alias leaves the recording as it was, and
        /// reporting it would make this a rule about <c>ref</c> instead of about mutation.
        /// </para>
        /// <para>
        /// Out through brackets, on the terms <see cref="ChangesTheValueBehind"/> already sets: a reference
        /// to an element of a tracked collection is a reference into that collection. A ref local aliasing
        /// anything else - a local, an element of one, another object's member - names storage no recording
        /// of this node ever read, and is not tracked.
        /// </para>
        /// </remarks>
        private static Dictionary<ISymbol, ISymbol> CollectRefLocalAliases(
            BodyWithModel body,
            ImmutableHashSet<ISymbol> trackedState)
        {
            var aliases = new Dictionary<ISymbol, ISymbol>(SymbolEqualityComparer.Default);

            foreach (SyntaxNode node in body.Body.DescendantNodesAndSelf())
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

        /// <returns>
        /// The tracked state a <c>ref</c> expression reaches, through another alias where it names one, or
        /// <see langword="null"/> when it reaches storage no recording of this node reads.
        /// </returns>
        /// <remarks>
        /// An alias of an alias names whatever the first one named, and a local carries no receiver of its
        /// own, so the question <see cref="StateReference.OnThisInstance"/> answers was settled where that
        /// first alias was declared. A declaration always precedes the aliases of it, so reading the body in
        /// source order is enough to have the answer by the time it is asked for.
        /// </remarks>
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

        /// <returns>The node naming the storage a <c>ref</c> expression points at.</returns>
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
                foreach (SyntaxNode node in body.Body.DescendantNodesAndSelf())
                {
                    if (node is not SimpleNameSyntax name || IsInsideNameOf(name))
                        continue;

                    // A helper reached through another instance marks that instance, however bare the
                    // MarkChanged call inside its body looks, so the receiver decides both questions below.
                    if (!IsOnThisInstance(name))
                        continue;

                    ISymbol? symbol = body.Model.GetSymbolInfo(name).Symbol;
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

        /// <summary>The methods a reference to <paramref name="name"/> actually runs.</summary>
        /// <remarks>
        /// A property reference runs one accessor, not both: a read runs the getter and a plain assignment
        /// runs the setter, while a compound assignment or an increment runs each in turn. Yielding both for
        /// every reference let a bare read of a property whose setter marks stand in for the mark.
        /// </remarks>
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

    /// <remarks>
    /// A bare identifier inside an instance member is this node; a qualified one has to say so, whether it
    /// spells the receiver beside the name or once at the head of a conditional-access chain. Anything else
    /// names another object, whose staleness this node's mark does not decide.
    /// </remarks>
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

    /// <remarks>
    /// A deconstruction target counts, on the same terms the single name does: it is written without being
    /// read, and only the syntax between it and the <c>=</c> differs. Reading it as anything else would put
    /// a value <c>Process</c> only overwrites into the state it depends on, and run a property's getter for
    /// a reference that never reads it.
    /// </remarks>
    private static bool IsSimpleAssignmentTarget(ExpressionSyntax expression)
        => (expression.Parent is AssignmentExpressionSyntax assignment
            && assignment.Left == expression
            && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
           || (expression.Parent is ArgumentSyntax argument && IsDeconstructionTarget(argument));

    /// <summary>Whether the reference is where a change to the state this member holds is written.</summary>
    /// <remarks>
    /// <para>
    /// An element write - <c>_points[0] = value</c> - is the assignment shape and not the
    /// collection-mutation bound. Nothing but the tracked name reaches that element, no name outside this
    /// member is involved, and the read side already counts the field as state <c>Process</c> depends on,
    /// because a read of <c>_points[i]</c> reads <c>_points</c>. Reading the write out differently is what
    /// let the node go stale while the rule stayed silent.
    /// </para>
    /// <para>
    /// Asked separately from <see cref="IsWriteTarget"/>, which answers a different question - which
    /// accessor a reference runs. An element write on a property runs its getter, so widening that one
    /// would have walked the setter body and let a mark written there excuse a mutation it never sees.
    /// </para>
    /// <para>
    /// Through brackets only, however many are nested. A member written past the name -
    /// <c>_child.Bounds = value</c> - is another object's state, which this node's mark does not decide,
    /// and is where the rule already stops.
    /// </para>
    /// </remarks>
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

    /// <summary>Whether the argument is an element the left side of a deconstruction writes.</summary>
    /// <remarks>
    /// <para>
    /// A deconstruction is the assignment shape written once for several targets: each element stands
    /// exactly where <c>_bounds = bounds</c> puts its name, changes the same value on the same statement,
    /// and is spelled by the same node's own body. Only the tuple standing between the name and the
    /// <c>=</c> differs, so reading the two out differently is what let an ordinary mutator past the rule.
    /// </para>
    /// <para>
    /// Out through tuples only, however many are nested, and only on the left: the identical tuple on the
    /// right reads its elements, and an argument of an ordinary call is parented by an argument list rather
    /// than a tuple, so neither reaches an assignment this way. What each element writes is still decided by
    /// the receiver it spells, so a deconstruction into another object stops where every other write does.
    /// </para>
    /// </remarks>
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
