using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
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
            else
            {
                return new ParseResult(script.CreateDelegate(), null);
            }
        }
        catch (Exception ex)
        {
            return new ParseResult(null, $"Compilation error: {ex.Message}");
        }
    }

    private static ScriptOptions CreateScriptOptions()
    {
        return ScriptOptions.Default
            .AddReferences(
                typeof(object).Assembly,
                typeof(Math).Assembly,
                typeof(Console).Assembly,
                typeof(Enumerable).Assembly,
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
