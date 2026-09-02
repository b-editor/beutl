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

        foreach (ISymbol member in type.GetMembers())
        {
            if (member is not IMethodSymbol method)
                continue;

            // Constructors precede recording, and teardown follows the last recording.
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
            var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            return MarksChangedCore(method, visited);
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
