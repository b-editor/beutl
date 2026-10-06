using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Beutl.Engine.SourceGenerators.Analyzers;

public sealed partial class MetadataCallbackPurityAnalyzer
{
    private static (ExpressionSyntax Callback, SemanticModel Model, string? Unresolved) ResolveCallback(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax expression)
    {
        SemanticModel model = context.SemanticModel;
        HashSet<ISymbol> visited = new(SymbolEqualityComparer.Default);

        while (expression is IdentifierNameSyntax or MemberAccessExpressionSyntax)
        {
            ISymbol? symbol = model.GetSymbolInfo(expression, context.CancellationToken).Symbol;
            ExpressionSyntax? source;

            switch (symbol)
            {
                case IFieldSymbol { IsReadOnly: true } field:
                    if (IsWrittenInAConstructor(context, field))
                        return (expression, model, "the callback field is reassigned in a constructor, so its initializer does not describe the callback used here");
                    if (!visited.Add(field))
                        return (expression, model, CyclicCallback);

                    source = GetFieldInitializer(context, field);
                    if (source is null)
                        return (expression, model, DescribeUnreadableField(field));
                    break;

                case IPropertySymbol property:
                    if (IsWrittenInAConstructor(context, property))
                        return (expression, model, "the callback property is assigned in a constructor, so its initializer does not describe the callback used here");
                    if (property.SetMethod is not null)
                    {
                        return (expression, model, "the callback comes from a property with a setter, so "
                            + "the delegate the call sees is whatever was last assigned to it");
                    }

                    if (!visited.Add(property))
                        return (expression, model, CyclicCallback);

                    source = GetGetterResult(context, property);
                    if (source is null)
                        return (expression, model, DescribeUnreadableProperty(property));
                    break;

                default:
                    return (expression, model, null);
            }

            model = GetSemanticModel(context, source.SyntaxTree);
            expression = Unwrap(source);
        }

        return (expression, model, null);
    }

    private const string CyclicCallback =
        "the callback comes from a member that resolves back to itself, so what delegate it ends up "
        + "holding cannot be determined";

    private static ExpressionSyntax? GetFieldInitializer(
        SyntaxNodeAnalysisContext context,
        IFieldSymbol field)
    {
        foreach (SyntaxReference declaration in field.DeclaringSyntaxReferences)
        {
            if (declaration.GetSyntax(context.CancellationToken)
                is VariableDeclaratorSyntax { Initializer.Value: { } value })
            {
                return value;
            }
        }

        return null;
    }

    private static ExpressionSyntax? GetGetterResult(
        SyntaxNodeAnalysisContext context,
        IPropertySymbol property)
    {
        foreach (SyntaxReference declaration in property.DeclaringSyntaxReferences)
        {
            if (declaration.GetSyntax(context.CancellationToken) is PropertyDeclarationSyntax syntax
                && GetInvariantCandidate(syntax) is { } value)
            {
                return value;
            }
        }

        return null;
    }

    private static string DescribeUnreadableField(IFieldSymbol field)
        => field.DeclaringSyntaxReferences.IsEmpty
            ? "the callback comes from a readonly field compiled into another assembly, so what its "
              + "initialiser gave the delegate cannot be seen, and readonly only fixes the reference"
            : "the callback comes from a readonly field with no initialiser, so the delegate is built in a "
              + "constructor, where it can close over that constructor's arguments";

    private static string DescribeUnreadableProperty(IPropertySymbol property)
        => property.DeclaringSyntaxReferences.IsEmpty
            ? "the callback comes from a get-only property compiled into another assembly, so what its "
              + "getter returns cannot be seen, and having no setter says only that this declaration does "
              + "not write it"
            : "the callback comes from a get-only property whose getter is not a single returned expression "
              + "this rule can read, so which delegate it hands back cannot be determined";

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

    private static string? DescribeImpurity(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ExpressionSyntax expression)
    {
        switch (expression)
        {
            case AnonymousFunctionExpressionSyntax lambda:
                // A static lambda cannot reach a local, a parameter, or this; the compiler says so.
                return lambda.Modifiers.Any(SyntaxKind.StaticKeyword)
                    ? null
                    : DescribeCaptureImpurity(context, model, lambda);

            case IdentifierNameSyntax or MemberAccessExpressionSyntax:
                {
                    ISymbol? symbol = model.GetSymbolInfo(expression, context.CancellationToken).Symbol;
                    return symbol switch
                    {
                        IMethodSymbol method => DescribeReceiverImpurity(context, model, method, expression),

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

    private const string RenderNodeTypeName = "Beutl.Graphics.Rendering.RenderNode";

    private static string? DescribeCaptureImpurity(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        AnonymousFunctionExpressionSyntax lambda)
    {
        IOperation? operation = model.GetOperation(lambda, context.CancellationToken);

        // The conversion to the delegate type shares the lambda's syntax, so what comes back can be the
        // conversion rather than the function underneath it.
        while (operation is IDelegateCreationOperation or IConversionOperation or IParenthesizedOperation)
        {
            operation = operation switch
            {
                IDelegateCreationOperation creation => creation.Target,
                IConversionOperation conversion => conversion.Operand,
                _ => ((IParenthesizedOperation)operation).Operand,
            };
        }

        if (operation is not IAnonymousFunctionOperation function)
        {
            // Silence has to mean the rule looked, and here it could not.
            return "the lambda is not declared static and this rule could not read what it closed over, "
                + "so it is reported rather than assumed to close over nothing";
        }

        ITypeSymbol? enclosingInstance = null;
        foreach (IOperation node in function.Descendants())
        {
            switch (node)
            {
                case IInvocationOperation { TargetMethod: { MethodKind: MethodKind.LocalFunction, IsStatic: false } target }
                    when IsDeclaredOutside(target, lambda):
                    return $"the lambda calls the non-static local function '{target.Name}', which can capture mutable state";

                case ILocalReferenceOperation local
                    when !local.Local.HasConstantValue && IsDeclaredOutside(local.Local, lambda):
                    return $"the lambda closes over the local '{local.Local.Name}', which can be assigned "
                        + "after this call, so one plan compiles for the first answer and is replayed for "
                        + "the second";

                case IParameterReferenceOperation parameter
                    when IsDeclaredOutside(parameter.Parameter, lambda)
                         && !IsNodePrimaryConstructorParameter(context, parameter.Parameter):
                    return $"the lambda closes over the parameter '{parameter.Parameter.Name}', which the "
                        + "caller decides per call, so one plan compiles for the first answer and is "
                        + "replayed for the second";

                case IInstanceReferenceOperation
                {
                    ReferenceKind: InstanceReferenceKind.ContainingTypeInstance, Type: { } instance
                }:
                    enclosingInstance = instance;
                    break;
            }
        }

        if (enclosingInstance is null || IsRenderNode(enclosingInstance))
            return null;

        return $"the lambda reads the enclosing '{enclosingInstance.Name}', which is not a RenderNode: "
            + "change marking and the recorded-answer cross-check are a node's, so nothing holds what this "
            + "reads to one answer";
    }

    private static bool IsNodePrimaryConstructorParameter(SyntaxNodeAnalysisContext context, IParameterSymbol parameter)
        => parameter.ContainingType is { } type && IsRenderNode(type)
           && parameter.DeclaringSyntaxReferences.Any(reference =>
               reference.GetSyntax(context.CancellationToken) is ParameterSyntax { Parent.Parent: TypeDeclarationSyntax });

    private static bool IsDeclaredOutside(ISymbol symbol, AnonymousFunctionExpressionSyntax lambda)
    {
        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.SyntaxTree == lambda.SyntaxTree && lambda.Span.Contains(reference.Span))
                return false;
        }

        return true;
    }

    private static bool IsRenderNode(ITypeSymbol type)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.ContainingNamespace?.ToDisplayString() + "." + current.Name == RenderNodeTypeName)
                return true;
        }

        return false;
    }

    private static string? DescribeReceiverImpurity(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        IMethodSymbol method,
        ExpressionSyntax expression)
    {
        if (method.IsStatic)
            return null;

        if (expression is not MemberAccessExpressionSyntax memberAccess)
        {
            // A bare name is an instance method on `this` or a local function, and only the first has a
            // receiver at all. A local function not declared static reaches the scope it is written in the
            // way a lambda does, and nothing here reads which locals it took, so it keeps the answer the
            // closure walk gives an unreadable lambda.
            return method.MethodKind == MethodKind.LocalFunction
                ? "the callback is a local function that is not declared static, so it can read a local or "
                  + "a parameter of the method it is written in and this rule does not read which"
                : DescribeEnclosingReceiverImpurity(context, model, expression);
        }

        // Parentheses and nothing else: a cast is what changes the member the call binds to, so stripping
        // one would read a receiver the call was never bound against.
        if (StripParentheses(memberAccess.Expression) is ThisExpressionSyntax or BaseExpressionSyntax)
            return DescribeEnclosingReceiverImpurity(context, model, expression);

        ITypeSymbol? receiver = model
            .GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        return receiver is { IsValueType: true }
            ? "the callback is an instance method on a value type, so the delegate carries a boxed copy of "
              + "whatever the receiver held at this call"
            : "the callback is an instance method on a reference type, and the delegate keeps that "
              + "object as its receiver";
    }

    private static string? DescribeEnclosingReceiverImpurity(
        SyntaxNodeAnalysisContext context,
        SemanticModel model,
        ExpressionSyntax expression)
    {
        ITypeSymbol? enclosingInstance = model
            .GetEnclosingSymbol(expression.SpanStart, context.CancellationToken)?.ContainingType;

        if (enclosingInstance is null)
        {
            // Silence has to mean the rule looked, and here it could not.
            return "the callback is an instance method and this rule could not read what type it is "
                + "written inside, so it is reported rather than assumed to be a node's";
        }

        // A RenderNode is a class, so this decides nothing the node test would have decided otherwise; it
        // is here to name what actually happens to a struct's `this`.
        if (enclosingInstance.IsValueType)
        {
            return "the callback is an instance method on a value type, so the delegate carries a boxed "
                + "copy of whatever the receiver held at this call";
        }

        if (IsRenderNode(enclosingInstance))
            return null;

        return $"the callback is an instance method of the enclosing '{enclosingInstance.Name}', which is "
            + "not a RenderNode: change marking and the recorded-answer cross-check are a node's, so "
            + "nothing holds what its receiver reads to one answer";
    }
}
