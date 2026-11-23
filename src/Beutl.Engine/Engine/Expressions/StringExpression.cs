using System.Diagnostics.CodeAnalysis;
using System.Text;
using Beutl.Scripting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Scripting;

namespace Beutl.Engine.Expressions;

/// <summary>
/// Evaluates a C# expression synchronously. Scripts containing <c>await</c> are rejected during validation.
/// </summary>
public sealed class StringExpression<T> : IReplacingExpression<T>
{
    private readonly CompiledStringExpression<T, T> _script;

    public StringExpression(string expression)
    {
        _script = new CompiledStringExpression<T, T>(expression);
    }

    // A copy over another expression shares the compiled script.
    private StringExpression(StringExpression<T> source, IExpression<T>? replaced)
    {
        _script = source._script;
        Replaced = replaced;
    }

    public string ExpressionString => _script.ExpressionString;

    public IExpression<T>? Replaced { get; }

    IExpression<T> IReplacingExpression<T>.WithReplaced(IExpression<T>? replaced) => new StringExpression<T>(this, replaced);

    public T Evaluate(ExpressionContext context) => _script.Evaluate(context, Replaced);

    internal T Evaluate(ExpressionContext context, IExpression<T>? below) => _script.Evaluate(context, below);

    public bool Validate([NotNullWhen(false)] out string? error) => _script.Validate(out error);

    public override string ToString() => ExpressionString;
}

internal static class StringExpressionParser
{
    internal static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest, kind: SourceCodeKind.Script);

    // The script with a member taken from what it returns, which can come after statements.
    internal static string WithMemberOfResult(string expressionString, string member)
    {
        var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(expressionString, ParseOptions).GetRoot();
        var script = new StringBuilder(expressionString);
        foreach (ExpressionSyntax result in GetResults(root).OrderByDescending(result => result.SpanStart))
        {
            script.Insert(result.Span.End, $").{member}");
            script.Insert(result.SpanStart, '(');
        }

        return script.ToString();
    }

    internal static bool ReferencesValue(string expressionString)
    {
        var root = (CompilationUnitSyntax)CSharpSyntaxTree.ParseText(expressionString, ParseOptions).GetRoot();
        foreach (IdentifierNameSyntax identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (identifier.Identifier.ValueText is not ("Value" or "ValueAtTime"))
                continue;

            if (identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifier)
                continue;

            if (identifier.Parent is MemberBindingExpressionSyntax memberBinding && memberBinding.Name == identifier)
                continue;

            if (identifier.Parent is NameColonSyntax nameColon && nameColon.Name == identifier)
                continue;

            if (identifier.Parent is AssignmentExpressionSyntax { Parent: InitializerExpressionSyntax } assignment
                && assignment.Left == identifier)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    internal static IEnumerable<ExpressionSyntax> GetResults(CompilationUnitSyntax root)
    {
        foreach (GlobalStatementSyntax global in root.Members.OfType<GlobalStatementSyntax>())
        {
            // Returns inside lambdas or local functions do not return from the script.
            var results = global.Statement.DescendantNodesAndSelf(node =>
                    node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
                .OfType<ReturnStatementSyntax>()
                .Select(statement => statement.Expression)
                .OfType<ExpressionSyntax>();
            foreach (ExpressionSyntax result in results)
                yield return result;

            if (global == root.Members.LastOrDefault()
                && global.Statement is ExpressionStatementSyntax { SemicolonToken.IsMissing: true } final)
            {
                yield return final.Expression;
            }
        }
    }
}

internal sealed class CompiledStringExpression<TResult, TValue>
{
    private static readonly ScriptOptions s_scriptOptions = CreateScriptOptions();

    private readonly Lazy<ParseResult> _parseResult;

    public CompiledStringExpression(string expression)
    {
        ExpressionString = expression ?? throw new ArgumentNullException(nameof(expression));
        _parseResult = new Lazy<ParseResult>(Parse, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string ExpressionString { get; }

    public TResult Evaluate(ExpressionContext context, IExpression<TValue>? below)
    {
        var result = _parseResult.Value;

        if (result.ScriptRunner == null)
        {
            throw new ExpressionException($"Expression parse error: {result.ParseError}");
        }

        try
        {
            var globals = new ExpressionGlobals<TValue>(context, below);
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
                typeof(ExpressionGlobals<TValue>));

            var errors = SynchronousScriptCompiler.GetErrors(script);

            if (errors.Count > 0)
            {
                return new ParseResult(null, string.Join(Environment.NewLine, errors));
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
        ITypeSymbol? targetType = GetTypeSymbol(compilation, typeof(TResult));
        if (targetType == null) return null;

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            foreach (ExpressionSyntax result in StringExpressionParser.GetResults((CompilationUnitSyntax)tree.GetRoot()))
            {
                // ConvertResult accepts null as default(TResult), regardless of its declared type.
                if (model.GetConstantValue(result) is { HasValue: true, Value: null }) continue;

                ITypeSymbol? sourceType = model.GetTypeInfo(result).Type;
                if (sourceType != null && !CanConvertResultType(compilation, sourceType, targetType))
                {
                    return $"The expression returns {sourceType.ToDisplayString()}, but this property needs {targetType.ToDisplayString()}.";
                }
            }
        }

        return null;
    }

    private static bool CanConvertResultType(CSharpCompilation compilation, ITypeSymbol source, ITypeSymbol target)
    {
        // Object/dynamic results need the runtime check. A null result (including void scripts)
        // continues to mean default(TResult), as it does in ConvertResult.
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
        if (IsSavedTimeResult(source, unwrappedTarget)) return true;

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

    // Expressions saved before #2885 return TimeSpan for the time properties, which hold MediaTime now.
    private static bool IsSavedTimeResult(ITypeSymbol source, ITypeSymbol target)
        => source.ToDisplayString() == "System.TimeSpan" && target.ToDisplayString() == "Beutl.MediaTime";

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
        ScriptOptions options = ScriptOptions.Default
            .AddReferences(
                typeof(object).Assembly,
                typeof(Math).Assembly,
                typeof(Console).Assembly,
                typeof(Enumerable).Assembly,
                typeof(TResult).Assembly,
                typeof(BeutlApplication).Assembly,
                typeof(ExpressionGlobals).Assembly)
            .AddImports(
                "System",
                "System.Linq",
                "Beutl.Media",
                "Beutl.Graphics",
                "Beutl.Engine");

        if (typeof(TValue).Assembly != typeof(TResult).Assembly)
        {
            options = options.AddReferences(typeof(TValue).Assembly);
        }

        return options;
    }

    private static TResult ConvertResult(object? value)
    {
        if (value == null)
        {
            return default!;
        }

        if (value is TResult typedValue)
        {
            return typedValue;
        }

        if (value is TimeSpan span && typeof(TResult) == typeof(MediaTime))
        {
            return (TResult)(object)MediaTime.FromTimeSpan(span);
        }

        var targetType = typeof(TResult);
        var sourceType = value.GetType();

        if (IsNumericType(targetType) && IsNumericType(sourceType))
        {
            return (TResult)Convert.ChangeType(value, targetType);
        }

        if (targetType == typeof(bool) && IsNumericType(sourceType))
        {
            double numValue = Convert.ToDouble(value);
            return (TResult)(object)(numValue != 0);
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

    private sealed record ParseResult(ScriptRunner<object>? ScriptRunner, string? ParseError);
}
