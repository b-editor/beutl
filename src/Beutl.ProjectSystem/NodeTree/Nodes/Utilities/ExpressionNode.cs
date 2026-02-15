using Beutl.NodeTree.Rendering;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Beutl.NodeTree.Nodes.Utilities;

public partial class ExpressionNode : Node
{
    private static readonly ScriptOptions s_scriptOptions = CreateScriptOptions();
    private readonly ExpressionNodeState _state = new();

    public ExpressionNode()
    {
        InputSocket = AddListInput<object?>("Inputs");
        Expression = AddProperty<string>("Expression");
        Output = AddOutput<object?>("Output");
        ErrorMonitor = AddTextMonitor("Error");
    }

    public ListInputSocket<object?> InputSocket { get; }

    public NodeItem<string> Expression { get; }

    public OutputSocket<object?> Output { get; }

    public NodeMonitor<string?> ErrorMonitor { get; }

    public partial class Resource
    {
        public override void Update(NodeRenderContext context)
        {
            var node = GetOriginal();
            var state = node._state;
            string? expression = Expression;

            if (string.IsNullOrWhiteSpace(expression))
            {
                Output = null;
                node.ErrorMonitor.Value = null;
                return;
            }

            if (state.Expression != expression)
            {
                state.Compile(expression!, s_scriptOptions);
            }

            if (state.CompileError != null)
            {
                Output = null;
                node.ErrorMonitor.Value = state.CompileError;
                return;
            }

            try
            {
                List<object?> inputs = context.CollectListInputValues<object?>(node.InputSocket);
                double time = context.Time.TotalSeconds;
                var globals = new ExpressionNodeGlobals(inputs, time);
                object? result = state.Runner!(globals).GetAwaiter().GetResult();
                Output = result;
                node.ErrorMonitor.Value = null;
            }
            catch (Exception ex)
            {
                Output = null;
                node.ErrorMonitor.Value = $"Runtime error: {ex.Message}";
            }
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
