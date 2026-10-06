using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Beutl.Engine.SourceGenerators.Analyzers;

public sealed partial class RenderNodeChangeMarkingAnalyzer
{
    private sealed class TypeAnalysis(
        Compilation compilation,
        INamedTypeSymbol type,
        INamedTypeSymbol renderNodeType,
        CancellationToken cancellationToken)
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
            pending.Push(entryPoint.OriginalDefinition);
            visited.Add(entryPoint.OriginalDefinition);

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
                                && visited.Add(callee.OriginalDefinition))
                            {
                                pending.Push(callee.OriginalDefinition);
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
                            read.Add(symbol.OriginalDefinition);
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
                foreach (SyntaxNode node in body.Body.DescendantNodesAndSelf(
                    child => RunsNestedFunction(
                        body.Model,
                        body.Body,
                        child,
                        localFunctionsFollowedAsCallees: false)))
                {
                    if (GetStateReference(body.Model, node) is not { Symbol: { } symbol } reference)
                        continue;

                    // A write through a ref local lands on whatever the local is bound to, which is the
                    // node's state when every binding this member gives it names that state.
                    if (symbol is ILocalSymbol { RefKind: RefKind.Ref } alias)
                    {
                        if (ChangesTheState(body.Model, reference.Access)
                            && ResolveRefTarget(body.Model, body.Body, alias, trackedState, cancellationToken)
                            is { } aliased)
                        {
                            yield return new StateAssignment(aliased, reference.Access.GetLocation());
                        }

                        continue;
                    }

                    if (!trackedState.Contains(symbol.OriginalDefinition))
                        continue;

                    // An assignment to another instance of the same type is a different object's state, and
                    // marking this node changed would say nothing about it.
                    if (!reference.OnThisInstance)
                        continue;

                    if (ChangesTheState(body.Model, reference.Access))
                        yield return new StateAssignment(symbol, reference.Access.GetLocation());
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
            => (!localFunctionsFollowedAsCallees || nested is not LocalFunctionStatementSyntax)
               && NestedFunctionSyntax.Runs(model, body, nested, CancellationToken.None);

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
                IParameterSymbol parameter => parameter.DeclaringSyntaxReferences.Any(reference =>
                    reference.GetSyntax() is ParameterSyntax { Parent.Parent: TypeDeclarationSyntax }),

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

        public IEnumerable<IMethodSymbol> ConstructorSubscriptions(INamedTypeSymbol declaring)
        {
            foreach (INamedTypeSymbol current in EnumerateTypeChain(declaring, renderNodeType))
                foreach (IMethodSymbol constructor in current.InstanceConstructors)
                    foreach (BodyWithModel body in GetBodies(constructor))
                        foreach (AnonymousFunctionExpressionSyntax lambda in body.Body.DescendantNodes(child => RunsNestedFunction(body.Model, body.Body, child,
                                     localFunctionsFollowedAsCallees: false)).OfType<AnonymousFunctionExpressionSyntax>())
                        {
                            if (IsSubscriptionCallback(lambda, body.Model) && body.Model.GetOperation(lambda) is IAnonymousFunctionOperation operation)
                                yield return operation.Symbol;
                        }
        }

        private static bool IsSubscriptionCallback(AnonymousFunctionExpressionSyntax lambda, SemanticModel model)
        {
            bool eventHandler = lambda.Parent is AssignmentExpressionSyntax assignment
                && assignment.IsKind(SyntaxKind.AddAssignmentExpression)
                && model.GetSymbolInfo(assignment.Left).Symbol is IEventSymbol;
            bool subscription = lambda.Parent is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax invocation }
                && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "Subscribe" };
            return eventHandler || subscription;
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
                    AnonymousFunctionExpressionSyntax lambda => lambda.Body,
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
                    ExpressionSyntax access = MemberAccessSyntax.GetAccessExpression(name);
                    if (!IsSimpleAssignmentTarget(access) && property.GetMethod is { } getter)
                        yield return getter;
                    if (IsWriteTarget(access) && property.SetMethod is { } setter)
                        yield return setter;
                    break;
            }
        }

        private readonly record struct BodyWithModel(SyntaxNode Body, SemanticModel Model);
    }
}
