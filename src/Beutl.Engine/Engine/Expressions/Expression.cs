using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Beutl.Engine.Expressions;

public class Expression<T>(string expression) : IExpression<T>
{
    private static readonly ScriptOptions s_scriptOptions = CreateScriptOptions();

    private ScriptRunner<object>? _scriptRunner;
    private string? _parseError;
    private bool _isParsed = false;
    private bool _isEvaluating = false;

    public string ExpressionString { get; } = expression ?? throw new ArgumentNullException(nameof(expression));

    public Type ResultType => typeof(T);

    public T Evaluate(ExpressionContext context)
    {
        // 循環参照チェック
        if (_isEvaluating)
        {
            throw new ExpressionException($"Circular reference detected while evaluating property: {ExpressionString}");
        }

        EnsureParsed();

        if (_scriptRunner == null)
        {
            throw new ExpressionException($"Expression parse error: {_parseError}");
        }

        _isEvaluating = true;
        try
        {
            var globals = new ExpressionGlobals(context);
            var result = _scriptRunner(globals).GetAwaiter().GetResult();
            return ConvertResult(result);
        }
        catch (Exception ex) when (ex is not ExpressionException)
        {
            throw new ExpressionException($"Expression evaluation error: {ex.Message}", ex);
        }
        finally
        {
            _isEvaluating = false;
        }
    }

    public bool Validate([NotNullWhen(false)] out string? error)
    {
        EnsureParsed();
        error = _parseError;
        return _parseError == null;
    }

    private void EnsureParsed()
    {
        if (_isParsed)
            return;

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
                _parseError = string.Join(Environment.NewLine, errors.Select(e => e.GetMessage()));
                _scriptRunner = null;
            }
            else
            {
                _scriptRunner = script.CreateDelegate();
                _parseError = null;
            }
        }
        catch (Exception ex)
        {
            _scriptRunner = null;
            _parseError = $"Compilation error: {ex.Message}";
        }

        _isParsed = true;
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
        if (targetType == typeof(bool))
        {
            if (IsNumericType(sourceType))
            {
                double numValue = Convert.ToDouble(value);
                return (T)(object)(numValue != 0);
            }
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
}

public static class Expression
{
    public static Expression<T> Create<T>(string expressionString)
    {
        return new Expression<T>(expressionString);
    }

    public static bool TryParse<T>(string expressionString, [NotNullWhen(true)] out Expression<T>? expression, [NotNullWhen(false)] out string? error)
    {
        expression = new Expression<T>(expressionString);
        if (expression.Validate(out error))
        {
            return true;
        }

        expression = null;
        return false;
    }
}
