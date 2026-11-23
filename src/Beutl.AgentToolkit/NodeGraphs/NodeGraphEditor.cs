using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Reconciliation;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes.Group;

using ConnectionRejection = Beutl.Editor.Services.NodeGraphMutationService.ConnectionRejection;

namespace Beutl.AgentToolkit.NodeGraphs;

// Applies the operations of one edit_node_graph call to a graph, in order, refusing the first one that cannot apply.
// The same operations run on a copy of the scene first and then on the scene itself, so they must not depend on
// anything but the graph and the Ids given for new nodes.
internal sealed partial class NodeGraphEditor(GraphModel root, Rational? frameRate)
{
    private const double NodeSpacing = 320;

    private static readonly Dictionary<string, string[]> s_fields = new(StringComparer.Ordinal)
    {
        ["add"] = ["ref", "type", "graph", "name", "position", "values"],
        ["update"] = ["node", "graph", "name", "position", "values"],
        ["remove"] = ["node", "graph"],
        ["connect"] = ["from", "to", "graph", "replace"],
        ["disconnect"] = ["from", "to", "port", "graph"],
    };

    private readonly Dictionary<string, GraphNode> _refs = new(StringComparer.Ordinal);
    private readonly List<GraphNode> _touched = [];
    private readonly List<GraphNode> _created = [];

    // The nodes the operations added or changed, in the order they were first touched.
    public IReadOnlyList<GraphNode> Touched => _touched;

    // The nodes the operations added and did not remove again.
    public IReadOnlyList<GraphNode> Created => _created;

    public string? RefOf(GraphNode node) => _refs.FirstOrDefault(pair => ReferenceEquals(pair.Value, node)).Key;

    public void Apply(IReadOnlyList<NodeGraphOperation> operations, IReadOnlyList<Guid> newNodeIds)
    {
        for (int index = 0; index < operations.Count; index++)
        {
            NodeGraphOperation operation = operations[index];
            try
            {
                ApplyOne(operation, newNodeIds[index]);
            }
            catch (ReconcileException ex)
            {
                ToolError error = ex.Error;
                throw new ReconcileException(error with
                {
                    Message = $"operations[{index}] ({operation.Op}): {error.Message}",
                    Target = error.Target ?? $"operations[{index}]"
                });
            }
            catch (Exception ex)
            {
                throw new ReconcileException(new ToolError(
                    ErrorCode.ValidationRejected,
                    $"operations[{index}] ({operation.Op}) failed: {ex.Message}",
                    $"operations[{index}]",
                    "Check the operation against read_node_graph and the guide, then retry."));
            }
        }
    }

    // A new node's Id is minted before the copy runs, so the copy and the scene get the same one.
    public static Guid[] MintNodeIds(IReadOnlyList<NodeGraphOperation> operations)
        => operations.Select(operation => IsOp(operation, "add") ? Guid.NewGuid() : Guid.Empty).ToArray();

    private static bool IsOp(NodeGraphOperation operation, string name)
        => string.Equals(operation.Op?.Trim(), name, StringComparison.OrdinalIgnoreCase);

    private void ApplyOne(NodeGraphOperation operation, Guid newNodeId)
    {
        string op = operation.Op?.Trim().ToLowerInvariant() ?? "";
        if (!s_fields.TryGetValue(op, out string[]? allowed))
        {
            throw Rejected($"'{operation.Op}' is not an operation.", "Use add, update, remove, connect or disconnect.");
        }

        if (operation.UnknownFields is { Count: > 0 } unknown)
        {
            throw Rejected(
                $"'{unknown.Keys.First()}' is not a field of an operation.",
                $"A {op} operation takes {string.Join(", ", allowed)}.");
        }

        if (UsedFields(operation).FirstOrDefault(field => !allowed.Contains(field)) is { } unused)
        {
            throw Rejected($"A {op} operation takes no '{unused}'.", $"A {op} operation takes {string.Join(", ", allowed)}.");
        }

        GraphModel graph = operation.Graph is { } groupToken ? ResolveInnerGraph(groupToken) : root;
        switch (op)
        {
            case "add":
                Add(operation, graph, newNodeId);
                break;
            case "update":
                Update(operation, graph);
                break;
            case "remove":
                Remove(operation, graph);
                break;
            case "connect":
                Connect(operation, graph);
                break;
            case "disconnect":
                Disconnect(operation, graph);
                break;
        }
    }

    private static IEnumerable<string> UsedFields(NodeGraphOperation operation)
    {
        if (operation.Ref is not null) yield return "ref";
        if (operation.Type is not null) yield return "type";
        if (operation.Node is not null) yield return "node";
        if (operation.Graph is not null) yield return "graph";
        if (operation.Name is not null) yield return "name";
        if (operation.Position is not null) yield return "position";
        if (operation.Values is not null) yield return "values";
        if (operation.From is not null) yield return "from";
        if (operation.To is not null) yield return "to";
        if (operation.Port is not null) yield return "port";
        if (operation.Replace) yield return "replace";
    }

    private void Add(NodeGraphOperation operation, GraphModel graph, Guid id)
    {
        if (string.IsNullOrWhiteSpace(operation.Type))
        {
            throw Rejected("An add operation needs a type.", "Call list_node_types for the type names.");
        }

        if (NodeTypeCatalog.Resolve(operation.Type, out IReadOnlyList<NodeTypeEntry> candidates) is not { } entry)
        {
            throw Rejected(
                candidates.Count > 1 ? $"'{operation.Type}' names several node types." : $"'{operation.Type}' is not a node type.",
                candidates.Count > 0
                    ? $"Use one of: {string.Join(", ", candidates.Select(candidate => candidates.Count > 1 ? candidate.Discriminator : candidate.Name))}."
                    : "Call list_node_types for the type names.");
        }

        if (operation.Ref is { } name)
        {
            if (!RefPattern().IsMatch(name) || Guid.TryParse(name, out _))
            {
                throw Rejected($"'{name}' cannot be a ref.", "Use letters, digits, '_' and '-', starting with a letter.");
            }

            if (_refs.ContainsKey(name))
            {
                throw Rejected($"The ref '{name}' is already taken in this call.", "Give each new node its own ref.");
            }
        }

        var node = (GraphNode)Activator.CreateInstance(entry.Type)!;
        node.Id.Value = id;
        if (operation.Name is not null)
        {
            node.Name.Value = operation.Name;
        }

        node.Position.Value = ReadPosition(operation) ?? NextPosition(graph);
        if (!NodeGraphMutationService.CanAddNode(graph, node))
        {
            throw Rejected(
                $"The graph already has a {entry.Name}.",
                "A group holds one GroupInput and one GroupOutput; connect to the existing one without a port name to add a port.");
        }

        graph.Nodes.Add(node);
        if (operation.Ref is { } added)
        {
            _refs[added] = node;
        }

        _created.Add(node);
        Touch(node);
        if (operation.Values is { } values)
        {
            ApplyValues(node, values);
        }
    }

    private void Update(NodeGraphOperation operation, GraphModel graph)
    {
        GraphNode node = ResolveNode(RequireField(operation.Node, "node"), graph);
        if (operation.Name is not null)
        {
            node.Name.Value = operation.Name;
        }

        if (ReadPosition(operation) is { } position)
        {
            node.Position.Value = position;
        }

        Touch(node);
        if (operation.Values is { } values)
        {
            ApplyValues(node, values);
        }
    }

    private void Remove(NodeGraphOperation operation, GraphModel graph)
    {
        GraphNode node = ResolveNode(RequireField(operation.Node, "node"), graph);
        NodeGraphMutationService.RemoveNodeCore(graph, node);
        foreach (string name in _refs.Where(pair => ReferenceEquals(pair.Value, node)).Select(pair => pair.Key).ToArray())
        {
            _refs.Remove(name);
        }

        _touched.Remove(node);
        _created.Remove(node);
    }

    private void Connect(NodeGraphOperation operation, GraphModel graph)
    {
        (GraphNode fromNode, string? fromPort) = ParseEnd(RequireField(operation.From, "from"), graph);
        (GraphNode toNode, string? toPort) = ParseEnd(RequireField(operation.To, "to"), graph);
        if (fromPort is null && toPort is null)
        {
            throw Rejected("Name the port on at least one end.", "Write each end as '<node>.<port>'.");
        }

        if (fromPort is null || toPort is null)
        {
            AddPort(
                graph,
                fromPort is null ? fromNode : toNode,
                fromPort is null ? FindPort(toNode, toPort!, output: false) : FindPort(fromNode, fromPort, output: true),
                fromPort is null);
            return;
        }

        INodePort from = FindPort(fromNode, fromPort, output: true);
        INodePort to = FindPort(toNode, toPort, output: false);
        if (!NodeGraphMutationService.SortPortDirection(from, to, out IInputPort? input, out IOutputPort? output))
        {
            throw Rejected(
                $"'{operation.From}' and '{operation.To}' are both {(from is IInputPort ? "inputs" : "outputs")}.",
                "Connect an output (from) to an input (to).");
        }

        ConnectionRejection rejection = NodeGraphMutationService.ValidateConnection(graph, input, output);
        if (rejection == ConnectionRejection.InputOccupied)
        {
            Connection existing = graph.AllConnections.First(connection => connection.Id.Value == input.Connection.Value.Id);
            if (!operation.Replace)
            {
                throw Rejected(
                    $"'{operation.To}' is already connected from '{Describe(existing.Output.Value.Value as INodeMember)}'.",
                    "Pass replace: true to replace that connection, or disconnect it first.");
            }

            graph.Disconnect(existing);
            rejection = NodeGraphMutationService.ValidateConnection(graph, input, output);
        }

        if (rejection == ConnectionRejection.InputLocked)
        {
            throw Rejected(
                $"'{Describe(input)}' cannot be connected now.",
                "Its property may be animated or driven by an expression, or another connected input already drives the same property.");
        }

        if (rejection == ConnectionRejection.Cycle)
        {
            throw Rejected(
                $"Connecting '{Describe(output)}' to '{Describe(input)}' would close a cycle.",
                "A graph runs its nodes in the order its connections give. To feed a result back, use a ShaderRepeatNode or ShaderReplayNode.");
        }

        graph.Connect(input, output);
    }

    // A node that grows ports adds one matching the port at the other end, as the editor does for a wire dropped on it.
    private void AddPort(GraphModel graph, GraphNode node, INodePort mate, bool grownOnOutputSide)
    {
        NodePortLocation side = grownOnOutputSide ? NodePortLocation.Right : NodePortLocation.Left;
        switch (NodeGraphMutationService.ValidateNewPort(graph, node, mate, side))
        {
            case ConnectionRejection.NotDynamicNode:
                throw Rejected(
                    $"'{Describe(node)}' does not add ports; name the port.",
                    "Only nodes such as GroupInput, GroupOutput and the inputs and outputs of a loop body add a port for a connection.");
            case ConnectionRejection.WrongMateDirection:
                throw Rejected(
                    $"'{Describe(mate)}' is an {(mate is IInputPort ? "input" : "output")} on the {(grownOnOutputSide ? "to" : "from")} end.",
                    "Connect an output (from) to an input (to).");
            case ConnectionRejection.WrongSide:
                throw Rejected(
                    $"'{Describe(node)}' adds {(grownOnOutputSide ? "inputs" : "outputs")} only.",
                    grownOnOutputSide ? "Connect to it instead of from it." : "Connect from it instead of to it.");
            case ConnectionRejection.NoPortType:
                throw Rejected($"'{Describe(mate)}' has no type for a new port to take.", "Name the port to connect to.");
            case ConnectionRejection.InputOccupied or ConnectionRejection.InputLocked:
                throw Rejected($"'{Describe(mate)}' is already connected or cannot be connected now.", "Disconnect it first.");
            case ConnectionRejection.Cycle:
                throw Rejected($"A port on '{Describe(node)}' for '{Describe(mate)}' would close a cycle.", "Connect nodes in one direction only.");
        }

        if (!((IDynamicPortNode)node).AddNodePort(mate, out _))
        {
            throw Rejected($"'{Describe(node)}' could not add a port for '{Describe(mate)}'.", "Name an existing port instead.");
        }

        Touch(node);
    }

    private void Disconnect(NodeGraphOperation operation, GraphModel graph)
    {
        if (operation.Port is { } portText)
        {
            if (operation.From is not null || operation.To is not null)
            {
                throw Rejected("Give either port, or from and to.", "port removes every connection of one port.");
            }

            (GraphNode node, string? name) = ParseEnd(portText, graph);
            if (name is null)
            {
                throw Rejected($"'{portText}' names no port.", "Write it as '<node>.<port>'.");
            }

            // A group shows an inner input and output under one name; which wires to cut is then the caller's to say.
            int sameName = node.EnumerateMembers().Count(member => member is INodePort && NodeGraphMembers.NameOf(node, member) == name);
            if (sameName > 1)
            {
                throw Rejected(
                    $"'{portText}' names {sameName} ports of '{Describe(node)}'.",
                    "Give from and to instead: from picks the output of that name, and to the input.");
            }

            INodePort port = FindPort(node, name, output: null);
            Connection[] connections = ConnectionsOf(graph, port);
            if (connections.Length == 0)
            {
                throw Rejected($"'{portText}' has no connections.", "read_node_graph lists the connections.");
            }

            foreach (Connection connection in connections)
            {
                graph.Disconnect(connection);
            }

            return;
        }

        (GraphNode fromNode, string? fromPort) = ParseEnd(RequireField(operation.From, "from"), graph);
        (GraphNode toNode, string? toPort) = ParseEnd(RequireField(operation.To, "to"), graph);
        if (fromPort is null || toPort is null)
        {
            throw Rejected("Name the port on both ends.", "Write each end as '<node>.<port>', or use port to remove every connection of one port.");
        }

        INodePort from = FindPort(fromNode, fromPort, output: true);
        INodePort to = FindPort(toNode, toPort, output: false);
        Connection? found = graph.AllConnections.FirstOrDefault(connection =>
            (connection.Output.Value.Id == from.Id.Value && connection.Input.Value.Id == to.Id.Value)
            || (connection.Output.Value.Id == to.Id.Value && connection.Input.Value.Id == from.Id.Value));
        if (found is null)
        {
            throw Rejected($"'{operation.From}' is not connected to '{operation.To}'.", "read_node_graph lists the connections.");
        }

        graph.Disconnect(found);
    }

    private void ApplyValues(GraphNode node, JsonObject values)
    {
        // Setting one member can add, replace or rebind others: code declares ports, a mode chooses them, and a new
        // brush rebinds the inputs of its properties. So the order of the values must not matter. A name not found
        // yet, or a value its member does not take yet, waits for the rest, and a value whose member a later one
        // replaced or rebound, such as a port that new code declares with another type, goes again to the member in
        // its place, until a round changes nothing.
        var written = new Dictionary<string, (INodeMember Member, IPropertyAdapter? Adapter, JsonNode? Value)>(StringComparer.Ordinal);
        var refused = new Dictionary<string, ReconcileException>(StringComparer.Ordinal);
        List<KeyValuePair<string, JsonNode?>> pending = [.. values];
        for (int round = 0; pending.Count > 0; round++)
        {
            var waiting = new List<KeyValuePair<string, JsonNode?>>();
            bool progressed = false;
            foreach (KeyValuePair<string, JsonNode?> pair in pending)
            {
                if (NodeGraphMembers.Find(node, pair.Key, NodeGraphMembers.IsSettable) is not { } member)
                {
                    waiting.Add(pair);
                    continue;
                }

                try
                {
                    NodeGraphMembers.WriteValue(member, pair.Key, pair.Value?.DeepClone(), frameRate);
                    written[pair.Key] = (member, member.Property, pair.Value);
                    refused.Remove(pair.Key);
                    progressed = true;
                }
                catch (ReconcileException ex)
                {
                    refused[pair.Key] = ex;
                    waiting.Add(pair);
                }
            }

            HashSet<INodeMember> members = [.. node.EnumerateMembers()];
            foreach ((string name, (INodeMember member, IPropertyAdapter? adapter, JsonNode? value)) in written.ToArray())
            {
                if (!members.Contains(member) || !ReferenceEquals(member.Property, adapter))
                {
                    written.Remove(name);
                    waiting.Add(new(name, value));
                }
            }

            if (waiting.Count > 0 && (!progressed || round > values.Count))
            {
                if (waiting.Select(pair => refused.GetValueOrDefault(pair.Key)).FirstOrDefault(error => error is not null) is { } failure)
                {
                    throw failure;
                }

                string[] settable = node.EnumerateMembers()
                    .Where(NodeGraphMembers.IsSettable)
                    .Select(member => NodeGraphMembers.NameOf(node, member))
                    .ToArray();
                string? error = NodeGraphMembers.ReadError(node);
                throw Rejected(
                    $"'{Describe(node)}' has no member '{waiting[0].Key}'.",
                    (settable.Length > 0 ? $"Its settable members are {string.Join(", ", settable)}." : "It has no settable members.")
                    + (error is null ? "" : $" It reports: {error}"));
            }

            pending = waiting;
        }
    }

    private GraphModel ResolveInnerGraph(string token)
    {
        GraphNode node = ResolveNodeAnywhere(token);
        if (node is not GroupNode group)
        {
            throw Rejected($"'{token}' is a {NodeGraphMembers.FormatType(node.GetType())}, which has no inner graph.", "Name a group or loop node.");
        }

        return group.Group.Value;
    }

    private GraphNode ResolveNode(string token, GraphModel graph)
    {
        // '@Type' names the one node of that type in the graph, such as a loop body's ShaderLoopInput, which a new
        // loop makes itself, so no earlier operation could have given it a ref.
        if (token.StartsWith('@'))
        {
            string typeName = token[1..].Trim();
            GraphNode[] matches = graph.Nodes
                .Where(candidate => string.Equals(NodeGraphMembers.FormatType(candidate.GetType()), typeName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return matches.Length == 1
                ? matches[0]
                : throw Rejected(
                    matches.Length == 0
                        ? $"This graph has no {typeName}."
                        : $"This graph has {matches.Length} nodes of type {typeName}.",
                    "'@Type' names the only node of a type in the graph; use a ref or an Id otherwise.");
        }

        GraphNode node = ResolveNodeAnywhere(token);
        if (!ReferenceEquals(node.FindHierarchicalParent<GraphModel>(), graph))
        {
            throw Rejected(
                $"'{token}' is in another graph than this operation edits.",
                "Both ends of a connection are in one graph. Use the graph field for nodes inside a group or loop, and its GroupInput, GroupOutput or loop ports to cross into it.");
        }

        return node;
    }

    private GraphNode ResolveNodeAnywhere(string token)
    {
        string trimmed = token.Trim();
        if (_refs.TryGetValue(trimmed, out GraphNode? named))
        {
            return named;
        }

        if (Guid.TryParse(trimmed, out Guid id))
        {
            foreach (GraphModel graph in root.EnumerateGraphs())
            {
                foreach (GraphNode node in graph.Nodes)
                {
                    if (node.Id.Value == id)
                    {
                        return node;
                    }
                }
            }
        }

        throw Rejected(
            $"'{trimmed}' is no node of this graph.",
            _refs.Count > 0
                ? $"Use a node Id from read_node_graph, or a ref given earlier in this call: {string.Join(", ", _refs.Keys)}."
                : "Use a node Id from read_node_graph, or a ref given by an earlier add in this call.");
    }

    // '<node>.<port>', split at the first dot: refs and Ids have none, port names may.
    private (GraphNode Node, string? Port) ParseEnd(string text, GraphModel graph)
    {
        int dot = text.IndexOf('.', StringComparison.Ordinal);
        string node = dot < 0 ? text : text[..dot];
        string? port = dot < 0 ? null : text[(dot + 1)..];
        if (port is { Length: 0 })
        {
            throw Rejected($"'{text}' ends in a dot.", "Write it as '<node>.<port>', or '<node>' alone to add a port.");
        }

        return (ResolveNode(node, graph), port);
    }

    // output says which end it is: true for from, false for to, null for either.
    private INodePort FindPort(GraphNode node, string name, bool? output)
    {
        INodeMember? member = NodeGraphMembers.Find(
            node, name, output is { } isOutput ? candidate => candidate is IOutputPort == isOutput && candidate is INodePort : candidate => candidate is INodePort);
        if (member is INodePort port)
        {
            return port;
        }

        string[] ports = node.EnumerateMembers()
            .OfType<INodePort>()
            .Select(candidate => $"{NodeGraphMembers.NameOf(node, candidate)} ({NodeGraphMembers.KindOf(candidate)})")
            .ToArray();
        string? error = NodeGraphMembers.ReadError(node);
        throw Rejected(
            member is null
                ? $"'{Describe(node)}' has no port '{name}'."
                : $"'{name}' on '{Describe(node)}' is a {NodeGraphMembers.KindOf(member)}, not a port.",
            (ports.Length > 0 ? $"Its ports are {string.Join(", ", ports)}." : "It has no ports.")
            + (error is null ? "" : $" It reports: {error}"));
    }

    private static Connection[] ConnectionsOf(GraphModel graph, INodePort port)
    {
        HashSet<Guid> ids = port switch
        {
            IOutputPort output => [.. output.Connections.Select(reference => reference.Id)],
            IListPort list => [.. list.Connections.Select(reference => reference.Id)],
            IInputPort { Connection.Value: { IsNull: false } connection } => [connection.Id],
            _ => [],
        };
        return graph.AllConnections.Where(connection => ids.Contains(connection.Id.Value)).ToArray();
    }

    private static string RequireField(string? value, string name)
        => string.IsNullOrWhiteSpace(value) ? throw Rejected($"The operation needs {name}.", "See the description of the operation's fields.") : value;

    private static (double X, double Y)? ReadPosition(NodeGraphOperation operation)
    {
        if (operation.Position is not { } position)
        {
            return null;
        }

        if (position.Length != 2 || !double.IsFinite(position[0]) || !double.IsFinite(position[1]))
        {
            throw Rejected("position takes [x, y].", "Give two finite numbers.");
        }

        return (position[0], position[1]);
    }

    private static (double X, double Y) NextPosition(GraphModel graph)
        => graph.Nodes.Count == 0 ? (0, 0) : (graph.Nodes.Max(node => node.Position.Value.X) + NodeSpacing, 0);

    private void Touch(GraphNode node)
    {
        if (!_touched.Contains(node))
        {
            _touched.Add(node);
        }
    }

    private string Describe(GraphNode node)
        => RefOf(node) ?? (string.IsNullOrEmpty(node.Name.Value) ? $"{NodeGraphMembers.FormatType(node.GetType())} {node.Id.Value}" : node.Name.Value);

    private string Describe(INodeMember? member)
    {
        if (member?.FindHierarchicalParent<GraphNode>() is not { } node)
        {
            return member?.Name.Value ?? "?";
        }

        return $"{Describe(node)}.{NodeGraphMembers.NameOf(node, member)}";
    }

    private static ReconcileException Rejected(string message, string? hint)
        => new(new ToolError(ErrorCode.ValidationRejected, message, null, hint));

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$")]
    private static partial Regex RefPattern();
}
