using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;

namespace Beutl.Engine.Expressions;

public sealed class StringExpression<T> : IExpression<T>
{
    private static readonly ScriptOptions s_scriptOptions = CreateScriptOptions();

    private readonly Lazy<ParseResult> _parseResult;

    public StringExpression(string expression)
    {
        ExpressionString = expression ?? throw new ArgumentNullException(nameof(expression));
        _parseResult = new Lazy<ParseResult>(Parse, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string ExpressionString { get; }

    public T Evaluate(ExpressionContext context)
    {
        var result = _parseResult.Value;

        if (result.ScriptRunner == null)
        {
            throw new ExpressionException($"Expression parse error: {result.ParseError}");
        }

        try
        {
            var globals = new ExpressionGlobals(context);
            var evalResult = result.ScriptRunner(globals).GetAwaiter().GetResult();
            return ConvertResult(evalResult);
        }
        catch (Exception ex) when (ex is not ExpressionException)
        {
            throw new ExpressionException($"Expression evaluation error: {ex.Message}", ex);
        }
    }

    public bool Validate([NotNullWhen(false)] out string? error)
    {
        var result = _parseResult.Value;
        error = result.ParseError;
        return result.ParseError == null;
    }

    private ParseResult Parse()
    {
        try
        {
            var script = CSharpScript.Create<object>(
                ExpressionString,
                s_scriptOptions,
                typeof(ExpressionGlobals));

            var diagnostics = script.Compile();
            var errors = diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();

            if (errors.Count > 0)
            {
                return new ParseResult(null, string.Join(Environment.NewLine, errors.Select(e => e.GetMessage())));
            }
            if (ValidateResultTypes((CSharpCompilation)script.GetCompilation()) is { } typeError)
            {
                return new ParseResult(null, typeError);
            }

            return new ParseResult(script.CreateDelegate(), null);
        }
        catch (Exception ex)
        {
            return new ParseResult(null, $"Compilation error: {ex.Message}");
        }
    }

    private static string? ValidateResultTypes(CSharpCompilation compilation)
    {
        ITypeSymbol? targetType = GetTypeSymbol(compilation, typeof(T));
        if (targetType == null) return null;

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            var root = (CompilationUnitSyntax)tree.GetRoot();
            SemanticModel model = compilation.GetSemanticModel(tree);
            foreach (GlobalStatementSyntax global in root.Members.OfType<GlobalStatementSyntax>())
            {
                // Returns inside lambdas or local functions do not return from the script.
                var results = global.Statement.DescendantNodesAndSelf(node =>
                        node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
                    .OfType<ReturnStatementSyntax>()
                    .Select(statement => statement.Expression)
                    .OfType<ExpressionSyntax>();
                if (global == root.Members.LastOrDefault()
                    && global.Statement is ExpressionStatementSyntax { SemicolonToken.IsMissing: true } final)
                {
                    results = results.Append(final.Expression);
                }

                foreach (ExpressionSyntax result in results)
                {
                    // ConvertResult accepts null as default(T), regardless of its declared type.
                    if (model.GetConstantValue(result) is { HasValue: true, Value: null }) continue;

                    ITypeSymbol? sourceType = model.GetTypeInfo(result).Type;
                    if (sourceType != null && !CanConvertResultType(compilation, sourceType, targetType))
                    {
                        return $"The expression returns {sourceType.ToDisplayString()}, but this property needs {targetType.ToDisplayString()}.";
                    }
                }
            }
        }

        return null;
    }

    private static bool CanConvertResultType(CSharpCompilation compilation, ITypeSymbol source, ITypeSymbol target)
    {
        // Object/dynamic results need the runtime check. A null result (including void scripts)
        // continues to mean default(T), as it does in ConvertResult.
        if (source.SpecialType is SpecialType.System_Object or SpecialType.System_Void
            || source.TypeKind == TypeKind.Dynamic)
            return true;

        if (source is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            source = nullable.TypeArguments[0];

        ITypeSymbol unwrappedTarget = target is INamedTypeSymbol
        { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableTarget
            ? nullableTarget.TypeArguments[0]
            : target;
        if (SymbolEqualityComparer.Default.Equals(source, unwrappedTarget)) return true;

        Conversion conversion = compilation.ClassifyConversion(source, target);
        if (conversion.IsIdentity || conversion.IsReference || conversion.IsBoxing || conversion.IsUnboxing)
            return true;

        // Match ConvertResult, including narrowing numeric conversions and numeric-to-bool.
        return IsNumericType(source.SpecialType)
               && (IsNumericType(target.SpecialType) || target.SpecialType == SpecialType.System_Boolean);
    }

    private static ITypeSymbol? GetTypeSymbol(Compilation compilation, Type type)
    {
        if (type.IsArray)
        {
            ITypeSymbol? element = GetTypeSymbol(compilation, type.GetElementType()!);
            return element == null ? null : compilation.CreateArrayTypeSymbol(element, type.GetArrayRank());
        }

        Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        INamedTypeSymbol? symbol = compilation.GetTypeByMetadataName(definition.FullName!);
        if (symbol == null || !type.IsGenericType) return symbol;

        ITypeSymbol?[] arguments = type.GetGenericArguments().Select(argument => GetTypeSymbol(compilation, argument)).ToArray();
        // If a plugin type cannot be resolved, retain the runtime conversion check.
        return arguments.Length == symbol.Arity && arguments.All(argument => argument != null)
            ? symbol.Construct(arguments!)
            : null;
    }

    private static bool IsNumericType(SpecialType type)
    {
        return type is SpecialType.System_Byte or SpecialType.System_SByte
            or SpecialType.System_Int16 or SpecialType.System_UInt16
            or SpecialType.System_Int32 or SpecialType.System_UInt32
            or SpecialType.System_Int64 or SpecialType.System_UInt64
            or SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
    }

    private static ScriptOptions CreateScriptOptions()
    {
        return ScriptOptions.Default
            .AddReferences(
                typeof(object).Assembly,
                typeof(Math).Assembly,
                typeof(Console).Assembly,
                typeof(Enumerable).Assembly,
                typeof(T).Assembly,
                typeof(BeutlApplication).Assembly,
                typeof(ExpressionGlobals).Assembly)
            .AddImports(
                "System",
                "System.Linq",
                "Beutl.Media",
                "Beutl.Graphics",
                "Beutl.Engine");
    }

    private static T ConvertResult(object? value)
    {
        if (value == null)
        {
            return default!;
        }

        // Direct assignment if types match
        if (value is T typedValue)
        {
            return typedValue;
        }

        var targetType = typeof(T);
        var sourceType = value.GetType();

        // Numeric conversions
        if (IsNumericType(targetType) && IsNumericType(sourceType))
        {
            return (T)Convert.ChangeType(value, targetType);
        }

        // Special handling for bool
        if (targetType == typeof(bool) && IsNumericType(sourceType))
        {
            double numValue = Convert.ToDouble(value);
            return (T)(object)(numValue != 0);
        }

        throw new ExpressionException($"Cannot convert expression result from {sourceType.Name} to {targetType.Name}");
    }

    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal);
    }

    public override string ToString() => ExpressionString;

    private sealed record ParseResult(ScriptRunner<object>? ScriptRunner, string? ParseError);
}
