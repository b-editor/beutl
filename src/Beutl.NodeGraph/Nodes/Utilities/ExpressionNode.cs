using Beutl.NodeGraph.Composition;
using Beutl.Scripting;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Beutl.NodeGraph.Nodes.Utilities;

/// <summary>
/// Evaluates a synchronous C# script. Scripts containing <c>await</c> are reported in the error monitor.
/// </summary>
public partial class ExpressionNode : GraphNode
{
    private static readonly ScriptOptions s_scriptOptions = CreateScriptOptions();
    private readonly ExpressionNodeState _state = new();

    public ExpressionNode()
    {
        InputPort = AddListInput<object?>("Inputs", NodePortDisplays.Inputs);
        Expression = AddProperty<string>("Expression", NodePortDisplays.Expression);
        Output = AddOutput<object?>("Output", NodePortDisplays.Output);
        ErrorMonitor = AddTextMonitor("Error", NodePortDisplays.Error);
    }

    public ListInputPort<object?> InputPort { get; }

    public NodeMember<string> Expression { get; }

    public OutputPort<object?> Output { get; }

    public NodeMonitor<string?> ErrorMonitor { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            var node = RequireOriginal();
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
                List<object?> inputs = context.CollectListInputValues<object?>(node.InputPort);
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
                var errors = SynchronousScriptCompiler.GetErrors(script);

                if (errors.Count > 0)
                {
                    CompileError = string.Join(Environment.NewLine, errors);
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
