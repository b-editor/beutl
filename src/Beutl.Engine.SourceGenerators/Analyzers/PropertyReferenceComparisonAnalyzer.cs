using System.Collections.Immutable;
using Beutl.Engine.SourceGenerators.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Beutl.Engine.SourceGenerators.Analyzers;

// Reports ==, != and Equals between two property objects, which compare the objects rather than their values (#3482).
// Beutl.Analyzers compiles this file too, for the projects that do not take the generator.
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PropertyReferenceComparisonAnalyzer : DiagnosticAnalyzer
{
    private const string PropertyMetadataName = "Beutl.IProperty";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.PropertyReferenceComparison);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            if (start.Compilation.GetTypeByMetadataName(PropertyMetadataName) is not { } property)
            {
                return;
            }

            start.RegisterOperationAction(operation => AnalyzeBinary(operation, property), OperationKind.Binary);
            start.RegisterOperationAction(operation => AnalyzeInvocation(operation, property), OperationKind.Invocation);
        });
    }

    // The built-in reference equality, which an operator of the operand types would replace.
    private static void AnalyzeBinary(OperationAnalysisContext context, INamedTypeSymbol property)
    {
        var binary = (IBinaryOperation)context.Operation;
        if (binary.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)
            || binary.OperatorMethod is not null
            || !IsPropertyObject(binary.LeftOperand, property)
            || !IsPropertyObject(binary.RightOperand, property))
        {
            return;
        }

        string text = binary.OperatorKind == BinaryOperatorKind.Equals ? "==" : "!=";
        context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.PropertyReferenceComparison, binary.Syntax.GetLocation(), text));
    }

    // Equals(object) called on a property object, or the static object.Equals(a, b).
    private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol property)
    {
        var invocation = (IInvocationOperation)context.Operation;
        IMethodSymbol method = invocation.TargetMethod;
        if (method.Name != nameof(Equals) || method.ReturnType.SpecialType != SpecialType.System_Boolean)
        {
            return;
        }

        bool compares = method.IsStatic
            ? method.ContainingType.SpecialType == SpecialType.System_Object
              && invocation.Arguments.Length == 2
              && IsPropertyObject(invocation.Arguments[0].Value, property)
              && IsPropertyObject(invocation.Arguments[1].Value, property)
            : method.Parameters.Length == 1
              && method.Parameters[0].Type.SpecialType == SpecialType.System_Object
              && invocation.Instance is { } instance
              && IsPropertyObject(instance, property)
              && IsPropertyObject(invocation.Arguments[0].Value, property);
        if (compares)
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.PropertyReferenceComparison, invocation.Syntax.GetLocation(), "Equals"));
        }
    }

    // Whether the operand or a conversion inside it, like (IProperty<int>)o, is a property object rather than null.
    private static bool IsPropertyObject(IOperation operand, INamedTypeSymbol property)
    {
        bool isProperty = false;
        for (IOperation? current = operand; current is not null; current = (current as IConversionOperation)?.Operand)
        {
            if (current.ConstantValue is { HasValue: true, Value: null })
            {
                return false;
            }

            isProperty |= current.Type is { } type
                          && (SymbolEqualityComparer.Default.Equals(type, property)
                              || type.AllInterfaces.Contains(property, SymbolEqualityComparer.Default));
        }

        return isProperty;
    }
}
