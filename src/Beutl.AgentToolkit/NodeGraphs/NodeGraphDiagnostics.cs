using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.NodeGraphs;

public sealed record NodeGraphDiagnostic(
    string Severity,
    string Message,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Node = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? NodeLabel = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Connection = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<ShaderCompilerMessage>? Lines = null)
{
    // Whether the edit brought it, as against one the graph had before. Null outside an edit.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsNew { get; init; }

    // A connection by the Ids of its ends, which stay while the labels of new nodes differ between runs.
    [JsonIgnore]
    internal string? ConnectionKey { get; init; }
}

// A line of the shader compiler's message: stage is the file the compiler names the stage after (vert, frag, comp,
// or glsl for geometry and tessellation), line counts within that stage's code.
public sealed record ShaderCompilerMessage(string Stage, int Line, string Message);

internal static partial class NodeGraphDiagnostics
{
    public const string Error = "error";
    public const string Warning = "warning";

    // Evaluates the whole graph as a render would record it, without a GPU, and reads what each node and connection
    // reports. Evaluating writes to the graph's monitors and connection statuses, so run it on a copy, on the thread
    // that owns the document, where the statuses are written at once. whileEvaluated reads the graph in that state.
    public static List<NodeGraphDiagnostic> Evaluate(
        NodeGraphTarget target, Func<GraphNode, string> label, Action? whileEvaluated = null)
    {
        var diagnostics = new List<NodeGraphDiagnostic>();
        // At the middle of the element, as a render there would record it: at its first instant a replay has run no
        // steps, so nothing inside its body would be recorded or checked.
        MediaTime time = target.Element is { } element
            ? element.Start.Value + MediaTime.FromSeconds(element.Length.Value.ToSeconds() / 2)
            : MediaTime.Zero;
        using (var snapshot = new GraphSnapshot())
        {
            try
            {
                snapshot.Build(target.Root, new CompositionContext(time));
                snapshot.Evaluate(new CompositionContext(time));
            }
            catch (Exception ex)
            {
                diagnostics.Add(new NodeGraphDiagnostic(Error, $"The graph could not be evaluated: {ex.Message}"));
            }

            // Read before the snapshot goes: disposing its pictures takes back what they failed on.
            Collect(target.Root, label, diagnostics);
            whileEvaluated?.Invoke();
        }

        AddStructureWarnings(target, label, diagnostics);
        return diagnostics;
    }

    // Marks what the edit brought, and says whether it brought an error.
    public static bool MarkNew(IReadOnlyList<NodeGraphDiagnostic> before, List<NodeGraphDiagnostic> after)
    {
        var known = new HashSet<(string, string, string?, string?)>(before.Select(Key));
        bool newError = false;
        for (int i = 0; i < after.Count; i++)
        {
            bool isNew = !known.Contains(Key(after[i]));
            after[i] = after[i] with { IsNew = isNew };
            newError |= isNew && after[i].Severity == Error;
        }

        return newError;
    }

    // What a rendered copy of the scene reports on its nodes, for the warnings of a render. Run before the renderer
    // that drew it is disposed, which takes back what its pictures failed on.
    public static IReadOnlyList<string> CollectRenderFailures(Scene scene)
    {
        var failures = new List<string>();
        try
        {
            foreach ((EngineObject owner, GraphModel root) in NodeGraphLocator.EnumerateRoots(scene))
            {
                foreach (GraphModel graph in root.EnumerateGraphs())
                {
                    foreach (GraphNode node in graph.Nodes)
                    {
                        if (NodeGraphMembers.ReadError(node) is { } error)
                        {
                            failures.Add($"Node graph {owner.Id.Value} ({owner.GetType().Name}): node {node.Id.Value} ({Label(node)}) failed: {error}");
                        }
                    }
                }
            }
        }
        catch (Exception)
        {
            // The warnings are an aid; a render that drew must not fail on reading them.
        }

        return failures;
    }

    public static string Label(GraphNode node)
        => string.IsNullOrEmpty(node.Name.Value) ? NodeGraphMembers.FormatType(node.GetType()) : node.Name.Value;

    private static (string, string, string?, string?) Key(NodeGraphDiagnostic diagnostic)
        => (diagnostic.Severity, diagnostic.Message, diagnostic.Node, diagnostic.ConnectionKey);

    private static void Collect(GraphModel root, Func<GraphNode, string> label, List<NodeGraphDiagnostic> diagnostics)
    {
        foreach (GraphModel graph in root.EnumerateGraphs())
        {
            foreach (GraphNode node in graph.Nodes)
            {
                if (NodeGraphMembers.ReadError(node) is { } error)
                {
                    IReadOnlyList<ShaderCompilerMessage> lines = ParseCompilerMessages(error);
                    diagnostics.Add(new NodeGraphDiagnostic(
                        Error, error, node.Id.Value.ToString(), label(node), null, lines.Count > 0 ? lines : null));
                }
            }

            foreach (Connection connection in graph.AllConnections)
            {
                if (connection.Status.Value == ConnectionStatus.Error)
                {
                    diagnostics.Add(DescribeFailedConnection(connection, label));
                }
            }
        }
    }

    private static NodeGraphDiagnostic DescribeFailedConnection(Connection connection, Func<GraphNode, string> label)
    {
        var output = connection.Output.Value.Value as INodeMember;
        var input = connection.Input.Value.Value as INodeMember;
        GraphNode? producer = output?.FindHierarchicalParent<GraphNode>();
        GraphNode? consumer = input?.FindHierarchicalParent<GraphNode>();
        string from = End(producer, output, label);
        string to = End(consumer, input, label);
        string message = producer is not null && NodeGraphMembers.ReadError(producer) is not null
            ? "The connection fails because the node it comes from failed; see that node's error."
            : output is null || input is null
                ? "The connection has an end that no longer exists."
                : $"{NodeGraphMembers.FormatType(NodeGraphMembers.ValueTypeOf(output))} from the output does not convert to {NodeGraphMembers.FormatType(NodeGraphMembers.ValueTypeOf(input))} of the input, or the input cannot take a connection now.";
        return new NodeGraphDiagnostic(Error, message, consumer?.Id.Value.ToString(), consumer is null ? null : label(consumer), $"{from} -> {to}")
        {
            ConnectionKey = $"{connection.Output.Value.Id} -> {connection.Input.Value.Id}"
        };

        static string End(GraphNode? node, INodeMember? member, Func<GraphNode, string> label)
            => node is null || member is null ? "?" : $"{label(node)}.{NodeGraphMembers.NameOf(node, member)}";
    }

    private static void AddStructureWarnings(NodeGraphTarget target, Func<GraphNode, string> label, List<NodeGraphDiagnostic> diagnostics)
    {
        GraphModel root = target.Root;
        OutputNode[] outputs = [.. root.Nodes.OfType<OutputNode>()];
        if (target.Owner is NodeGraphFilterEffect)
        {
            if (!root.Nodes.OfType<FilterEffectInputNode>().Any())
            {
                diagnostics.Add(new NodeGraphDiagnostic(
                    Warning,
                    "The effect has no FilterEffectInputNode, so it passes its layer through unchanged and never runs its graph."));
            }

            if (outputs.Length == 0)
            {
                diagnostics.Add(new NodeGraphDiagnostic(
                    Warning,
                    "The effect has no OutputNode, so it passes its layer through unchanged."));
            }
        }
        else if (outputs.Length == 0)
        {
            diagnostics.Add(new NodeGraphDiagnostic(Warning, "The drawable has no OutputNode, so it draws nothing."));
        }

        foreach (OutputNode output in outputs)
        {
            if (output.InputPort.Connection.Value.IsNull)
            {
                diagnostics.Add(new NodeGraphDiagnostic(
                    Warning,
                    "Nothing is connected to this OutputNode's Output.",
                    output.Id.Value.ToString(),
                    label(output)));
            }
        }
    }

    // shaderc writes "shader.frag:12: error: 'x' : undeclared identifier".
    internal static IReadOnlyList<ShaderCompilerMessage> ParseCompilerMessages(string text)
    {
        var lines = new List<ShaderCompilerMessage>();
        foreach (Match match in CompilerLinePattern().Matches(text))
        {
            if (int.TryParse(match.Groups["line"].ValueSpan, out int line))
            {
                lines.Add(new ShaderCompilerMessage(match.Groups["stage"].Value, line, match.Groups["text"].Value.Trim()));
            }
        }

        return lines;
    }

    [GeneratedRegex(@"shader\.(?<stage>vert|frag|comp|glsl|tesc|tese|geom):(?<line>\d+):\s*(?:error|warning)\s*:\s*(?<text>[^\r\n]+)")]
    private static partial Regex CompilerLinePattern();
}
