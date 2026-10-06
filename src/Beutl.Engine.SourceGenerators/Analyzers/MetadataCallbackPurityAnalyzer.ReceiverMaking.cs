using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Beutl.Engine.SourceGenerators.Analyzers;

public sealed partial class MetadataCallbackPurityAnalyzer
{
    private static void FollowPropertyAccess(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        IPropertySymbol property,
        SyntaxNode reference,
        ExpressionSyntax access,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (FollowReceiverCreation(context, model, body, reference, depth, walked, report)
            is not { } made)
        {
            return;
        }

        if (RunsGetter(access) && property.GetMethod is { } getter)
            FollowCall(context, RunsAsMade(made, getter), reference, "property", depth, walked, report);

        if (RunsSetter(access) && property.SetMethod is { } setter)
            FollowCall(context, RunsAsMade(made, setter), reference, "property", depth, walked, report);
    }

    /// <summary>The type the receiver was made as, or null where its making cannot be read.</summary>
    private static INamedTypeSymbol? FollowReceiverCreation(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        SyntaxNode body,
        SyntaxNode reference,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
        => FollowHeldCreation(
            context,
            GetReceiverCreation(context, model, reference),
            body,
            reference,
            depth,
            walked,
            report);

    /// <summary>The type <paramref name="held"/> was made as, following the constructor that made it.</summary>
    private static INamedTypeSymbol? FollowHeldCreation(
        SyntaxNodeAnalysisContext context,
        (ExpressionSyntax Creation, SemanticModel Model, INamedTypeSymbol Made)? held,
        SyntaxNode body,
        SyntaxNode reference,
        int depth,
        Dictionary<ISymbol, int> walked,
        Action<SyntaxNode, string, ISymbol, string> report)
    {
        if (held is not { } made)
            return null;

        if (RunsWith(body, made.Creation)
            && made.Model.GetSymbolInfo(made.Creation, context.CancellationToken).Symbol
                is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor)
        {
            FollowConstructor(context, constructor, reference, depth, walked, report);
        }

        return made.Made;
    }

    /// <summary>The member an instance made as <paramref name="made"/> runs.</summary>
    /// <remarks>
    /// A receiver whose making is readable carries an instance of exactly one type for its whole life, so
    /// a virtual member reached through a base declaration, and an interface member reached through the
    /// interface, both run a body the declaration the call bound to does not name - and that body, not the
    /// declaration, is what the callback answers with. A member nothing overrides, and one on a receiver
    /// whose making was not read, are already what runs and are handed back unchanged.
    /// </remarks>
    private static IMethodSymbol RunsAsMade(INamedTypeSymbol? made, IMethodSymbol member)
    {
        if (made is null)
            return member;

        if (member.ContainingType is { TypeKind: TypeKind.Interface })
            return RunsOn(made, member) ?? member;

        if (!member.IsVirtual && !member.IsAbstract && !member.IsOverride)
            return member;

        IMethodSymbol declaration = member.OriginalDefinition;

        for (INamedTypeSymbol? current = made; current is not null; current = current.BaseType)
        {
            foreach (ISymbol candidate in current.GetMembers(member.Name))
            {
                if (candidate is IMethodSymbol overriding && Overrides(overriding, declaration))
                    return overriding;
            }
        }

        return member;
    }

    private static bool Overrides(IMethodSymbol candidate, IMethodSymbol declaration)
    {
        for (IMethodSymbol? current = candidate; current is not null; current = current.OverriddenMethod)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, declaration))
                return true;
        }

        return false;
    }

    private static bool RunsWith(SyntaxNode body, ExpressionSyntax expression)
        => expression.SyntaxTree == body.SyntaxTree && body.Span.Contains(expression.Span);

    private static (ExpressionSyntax Creation, SemanticModel Model, INamedTypeSymbol Made)?
        GetReceiverCreation(
            SyntaxNodeAnalysisContext context,
            SemanticModel model,
            SyntaxNode reference)
        => GetReceiver(reference) is { } receiver ? GetCreationHeldBy(context, model, receiver) : null;

    /// <summary>The creation the instance <paramref name="receiver"/> holds was made by.</summary>
    private static (ExpressionSyntax Creation, SemanticModel Model, INamedTypeSymbol Made)?
        GetCreationHeldBy(
            SyntaxNodeAnalysisContext context,
            SemanticModel model,
            ExpressionSyntax receiver)
    {
        ExpressionSyntax expression = StripParentheses(receiver);
        (ExpressionSyntax Creation, SemanticModel Model)? made =
            expression is BaseObjectCreationExpressionSyntax written
                ? (written, model)
                : GetHeldCreation(context, model, expression);

        if (made is not { } creation
            || creation.Model.GetTypeInfo(creation.Creation, context.CancellationToken).Type
                is not INamedTypeSymbol created)
        {
            return null;
        }

        return IsHeldBy(created, model.GetTypeInfo(receiver, context.CancellationToken).Type)
            ? (creation.Creation, creation.Model, created)
            : null;
    }

    /// <summary>Whether an instance made as <paramref name="created"/> is what the receiver holds.</summary>
    /// <remarks>
    /// A making that reaches a receiver of a base type or of an interface it implements is the instance
    /// that receiver holds, and the derived body is what a call on it runs. One that reaches a receiver of
    /// an unrelated type got there through a user-defined conversion, which hands back something else
    /// entirely and says nothing about what the receiver ends up holding.
    /// </remarks>
    private static bool IsHeldBy(INamedTypeSymbol created, ITypeSymbol? receiver)
    {
        if (receiver is null)
            return false;

        for (INamedTypeSymbol? current = created; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, receiver))
                return true;
        }

        foreach (INamedTypeSymbol implemented in created.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implemented, receiver))
                return true;
        }

        return false;
    }

    private static (ExpressionSyntax Creation, SemanticModel Model)? GetHeldCreation(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ExpressionSyntax expression)
    {
        if (expression is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
            return null;

        ISymbol? symbol = model.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        ExpressionSyntax? initializer = symbol switch
        {
            IFieldSymbol field when HoldsOneCreation(context, model, expression, field)
                => GetFieldInitializer(context, field),

            ILocalSymbol local => GetUnreassignedLocalInitializer(context, local),

            IPropertySymbol { SetMethod: null } property
                when ReachesMemberOnItsOwn(context, model, expression, property)
                => GetAliasedFieldInitializer(context, property),

            // A parameter, a settable field, a property that computes, a method group: each is a receiver
            // whose making this rule was not shown, and the walk stops at every one of them.
            _ => null,
        };

        if (initializer is null)
            return null;

        SemanticModel initializerModel = GetSemanticModel(context, initializer.SyntaxTree);
        ExpressionSyntax value = StripParentheses(initializer);
        return value is BaseObjectCreationExpressionSyntax ? (value, initializerModel) : null;
    }

    private static bool HoldsOneCreation(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ExpressionSyntax reference,
        IFieldSymbol field)
        => field.IsReadOnly
           && ReachesMemberOnItsOwn(context, model, reference, field)
           && !IsWrittenInAConstructor(context, field);

    private static ExpressionSyntax? GetAliasedFieldInitializer(
        SyntaxNodeAnalysisContext context,
        IPropertySymbol property)
    {
        foreach (SyntaxReference declaration in property.DeclaringSyntaxReferences)
        {
            if (declaration.GetSyntax(context.CancellationToken) is not PropertyDeclarationSyntax syntax
                || GetInvariantCandidate(syntax) is not { } candidate)
            {
                continue;
            }

            ExpressionSyntax named = StripParentheses(candidate);
            if (named is not (IdentifierNameSyntax or MemberAccessExpressionSyntax))
                continue;

            SemanticModel getterModel = GetSemanticModel(context, named.SyntaxTree);
            if (getterModel.GetSymbolInfo(named, context.CancellationToken).Symbol is IFieldSymbol field
                && HoldsOneCreation(context, getterModel, named, field))
            {
                return GetFieldInitializer(context, field);
            }
        }

        return null;
    }

    private static bool ReachesMemberOnItsOwn(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ExpressionSyntax reference,
        ISymbol member)
    {
        if (reference is not MemberAccessExpressionSyntax access)
            return true;

        ExpressionSyntax qualifier = StripParentheses(access.Expression);

        return member.IsStatic
            ? model.GetSymbolInfo(qualifier, context.CancellationToken).Symbol is ITypeSymbol
            : qualifier is ThisExpressionSyntax;
    }

    private static bool IsWrittenInAConstructor(SyntaxNodeAnalysisContext context, ISymbol field)
    {
        INamedTypeSymbol type = field.OriginalDefinition.ContainingType!;

        foreach (IMethodSymbol constructor in type.InstanceConstructors.Concat(type.StaticConstructors))
        {
            foreach (SyntaxReference declaration in constructor.OriginalDefinition.DeclaringSyntaxReferences)
            {
                if (declaration.GetSyntax(context.CancellationToken) is ConstructorDeclarationSyntax syntax
                    && IsWrittenWithin(context, syntax, field))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ExpressionSyntax? GetUnreassignedLocalInitializer(
        SyntaxNodeAnalysisContext context,
        ILocalSymbol local)
    {
        foreach (SyntaxReference declaration in local.DeclaringSyntaxReferences)
        {
            if (declaration.GetSyntax(context.CancellationToken)
                is not VariableDeclaratorSyntax { Initializer.Value: { } value } declarator)
            {
                continue;
            }

            SyntaxNode scope = declarator.FirstAncestorOrSelf<MemberDeclarationSyntax>()
                ?? declarator.SyntaxTree.GetRoot(context.CancellationToken);

            if (!IsWrittenWithin(context, scope, local))
                return value;
        }

        return null;
    }

    private static bool IsWrittenWithin(SyntaxNodeAnalysisContext context, SyntaxNode scope, ISymbol symbol)
    {
        SemanticModel model = GetSemanticModel(context, scope.SyntaxTree);
        ISymbol declared = symbol.OriginalDefinition;

        foreach (SyntaxNode node in scope.DescendantNodes())
        {
            foreach (ExpressionSyntax written in GetWriteTargets(node))
            {
                ISymbol? target = model.GetSymbolInfo(written, context.CancellationToken).Symbol;
                if (SymbolEqualityComparer.Default.Equals(target?.OriginalDefinition, declared))
                    return true;
            }
        }

        return false;
    }

    private static IEnumerable<ExpressionSyntax> GetWriteTargets(SyntaxNode node)
    {
        switch (node)
        {
            // A deconstruction writes the elements of the tuple on its left and not the tuple itself.
            case AssignmentExpressionSyntax { Left: TupleExpressionSyntax tuple }:
                foreach (ArgumentSyntax element in tuple.Arguments)
                    yield return element.Expression;

                break;

            case AssignmentExpressionSyntax assignment:
                yield return assignment.Left;

                break;

            case ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None):
                yield return argument.Expression;

                break;

            case PrefixUnaryExpressionSyntax prefix
                when prefix.IsKind(SyntaxKind.PreIncrementExpression)
                    || prefix.IsKind(SyntaxKind.PreDecrementExpression):
                yield return prefix.Operand;

                break;

            case PostfixUnaryExpressionSyntax postfix
                when postfix.IsKind(SyntaxKind.PostIncrementExpression)
                    || postfix.IsKind(SyntaxKind.PostDecrementExpression):
                yield return postfix.Operand;

                break;
        }
    }

    private static ExpressionSyntax? GetReceiver(SyntaxNode reference) => reference switch
    {
        SimpleNameSyntax name when name.Parent is MemberAccessExpressionSyntax access && access.Name == name
            => access.Expression,
        SimpleNameSyntax name when name.Parent is MemberBindingExpressionSyntax binding && binding.Name == name
            => ConditionalAccessSyntax.FindReceiver(binding),
        // An object initializer writes its member with no receiver beside it, because the receiver is the
        // object the initializer belongs to. A member of a nested initializer has no such spelling: the
        // object it writes into is whatever the outer member handed back, which is not a making.
        SimpleNameSyntax name when name.Parent is AssignmentExpressionSyntax
        {
            Parent: InitializerExpressionSyntax { Parent: BaseObjectCreationExpressionSyntax made },
        } member && member.Left == name
            => made,
        // A property pattern names its member with no receiver beside it either, because the receiver is
        // whatever the pattern is matched against, which the is or the switch spells beside it.
        SimpleNameSyntax name when name.Parent is BaseExpressionColonSyntax
        {
            Parent: SubpatternSyntax subpattern,
        }
            => GetMatchedExpression(subpattern),
        ImplicitElementAccessSyntax element when element.Parent is AssignmentExpressionSyntax
        {
            Parent: InitializerExpressionSyntax { Parent: BaseObjectCreationExpressionSyntax made },
        } assignment && assignment.Left == element
            => made,
        ElementAccessExpressionSyntax element => element.Expression,
        _ => null,
    };

    /// <summary>The expression the pattern <paramref name="pattern"/> belongs to is matched against.</summary>
    /// <remarks>
    /// An is, a switch expression and a switch statement each spell what they match beside the pattern,
    /// and the combinators between hand that same value down unchanged. A subpattern of a nested property
    /// pattern reads off what the outer member returned rather than off a making, so it is not answered.
    /// </remarks>
    private static ExpressionSyntax? GetMatchedExpression(SyntaxNode pattern)
    {
        SyntaxNode? matched = pattern.Parent;

        while (matched is PropertyPatternClauseSyntax or RecursivePatternSyntax
               or ParenthesizedPatternSyntax or BinaryPatternSyntax or UnaryPatternSyntax)
        {
            matched = matched.Parent;
        }

        return matched switch
        {
            IsPatternExpressionSyntax match => match.Expression,
            SwitchExpressionArmSyntax { Parent: SwitchExpressionSyntax chosen } => chosen.GoverningExpression,
            CasePatternSwitchLabelSyntax { Parent.Parent: SwitchStatementSyntax statement }
                => statement.Expression,
            _ => null,
        };
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;

        return expression;
    }

    private static bool RunsGetter(ExpressionSyntax access)
        => access.Parent is not AssignmentExpressionSyntax assignment
            || assignment.Left != access
            || !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
            || WritesThroughTheMember(assignment);

    private static bool RunsSetter(ExpressionSyntax access) => access.Parent switch
    {
        AssignmentExpressionSyntax assignment
            => assignment.Left == access && !WritesThroughTheMember(assignment),
        PrefixUnaryExpressionSyntax prefix => prefix.IsKind(SyntaxKind.PreIncrementExpression)
            || prefix.IsKind(SyntaxKind.PreDecrementExpression),
        PostfixUnaryExpressionSyntax postfix => postfix.IsKind(SyntaxKind.PostIncrementExpression)
            || postfix.IsKind(SyntaxKind.PostDecrementExpression),
        _ => false,
    };

    /// <summary>
    /// Whether an assignment writes into what its left side hands back rather than replacing it.
    /// </summary>
    /// <remarks>
    /// A nested initializer - <c>new A { B = { C = 1 } }</c> - is written as an assignment but sets
    /// nothing: it reads <c>B</c> and writes into the object that read returns. It is the one assignment
    /// whose left side runs the getter, and the only place an initializer body appears on the right.
    /// </remarks>
    private static bool WritesThroughTheMember(AssignmentExpressionSyntax assignment)
        => assignment.Right is InitializerExpressionSyntax;
}
