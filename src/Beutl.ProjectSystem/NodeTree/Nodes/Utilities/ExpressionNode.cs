using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Beutl.NodeTree.Nodes.Utilities;

public class ExpressionNode : Node
{
    private static readonly ScriptOptions s_scriptOptions = CreateScriptOptions();

    private readonly ListInputSocket<object?> _inputSocket;
    private readonly NodeItem<string> _expressionProperty;
    private readonly OutputSocket<object?> _outputSocket;
    private readonly NodeMonitor<string?> _errorMonitor;

    public ExpressionNode()
    {
        _inputSocket = AddListInput<object?>("Inputs");
        _expressionProperty = AddProperty<string>("Expression");
        _outputSocket = AddOutput<object?>("Output");
        _errorMonitor = AddTextMonitor("Error");
    }

    public override void Evaluate(NodeEvaluationContext context)
    {
        base.Evaluate(context);

        var state = context.GetOrSetStateWithFactory(() => new ExpressionNodeState());
        string? expression = _expressionProperty.Value;

        if (string.IsNullOrWhiteSpace(expression))
        {
            _outputSocket.Value = null;
            _errorMonitor.Value = null;
            return;
        }

        if (state.Expression != expression)
        {
            state.Compile(expression!, s_scriptOptions);
        }

        if (state.CompileError != null)
        {
            _outputSocket.Value = null;
            _errorMonitor.Value = state.CompileError;
            return;
        }

        try
        {
            List<object?> inputs = _inputSocket.CollectValues();
            double time = context.Renderer.Time.TotalSeconds;
            var globals = new ExpressionNodeGlobals(inputs, time);
            object? result = state.Runner!(globals).GetAwaiter().GetResult();
            _outputSocket.Value = result;
            _errorMonitor.Value = null;
        }
        catch (Exception ex)
        {
            _outputSocket.Value = null;
            _errorMonitor.Value = $"Runtime error: {ex.Message}";
        }
    }

    private static ScriptOptions CreateScriptOptions()
    {
        return ScriptOptions.Default
            .AddReferences(
                typeof(object).Assembly,
                typeof(Math).Assembly,
                typeof(Enumerable).Assembly,
                typeof(ExpressionNodeGlobals).Assembly)
            .AddImports(
                "System",
                "System.Linq",
                "System.Collections.Generic");
    }

    private sealed class ExpressionNodeState
    {
        public string? Expression { get; private set; }

        public ScriptRunner<object>? Runner { get; private set; }

        public string? CompileError { get; private set; }

        public void Compile(string expression, ScriptOptions options)
        {
            Expression = expression;
            Runner = null;
            CompileError = null;

            try
            {
                var script = CSharpScript.Create<object>(
                    expression,
                    options,
                    typeof(ExpressionNodeGlobals));
                var diagnostics = script.Compile();
                var errors = diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .ToList();

                if (errors.Count > 0)
                {
                    CompileError = string.Join(Environment.NewLine, errors.Select(e => e.GetMessage()));
                }
                else
                {
                    Runner = script.CreateDelegate();
                }
            }
            catch (Exception ex)
            {
                CompileError = $"Compilation error: {ex.Message}";
            }
        }
    }
}
