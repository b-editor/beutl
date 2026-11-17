using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Beutl.AgentToolkit.NodeGraphs;

/// <summary>One step of an edit_node_graph call.</summary>
public sealed record NodeGraphOperation
{
    [Description("add | update | remove | connect | disconnect.")]
    public string? Op { get; init; }

    [Description("add: a name for the new node that the later operations of this call can use in place of its Id. Letters, digits, '_' and '-', starting with a letter.")]
    public string? Ref { get; init; }

    [Description("add: the node type as list_node_types names it, such as ShaderFullscreenPassNode or FilterEffectNode<Blur>, or its $type discriminator.")]
    public string? Type { get; init; }

    [Description("update/remove: the node, by ref, Id, or '@Type' for the only node of a type in the graph, such as @ShaderLoopInput in a loop body.")]
    public string? Node { get; init; }

    [Description("The group or loop node, by ref or Id, whose inner graph the operation edits. Omit for the graph that graphId names.")]
    public string? Graph { get; init; }

    [Description("add/update: the node's name, shown in the editor and in messages.")]
    public string? Name { get; init; }

    [Description("add/update: [x, y] in the node editor. A new node without one goes to the right of the others.")]
    public double[]? Position { get; init; }

    [Description("add/update: values by member name, applied in order, such as {\"Code\": \"#version 450 ...\", \"Padding\": 32, \"In.params.amount\": 0.5}. A member that setting another one creates, as code creates its In.* ports, can follow it in the same object. {\"$kf\": [[seconds, value, easing?], ...]} sets keyframes; a plain value replaces them.")]
    public JsonObject? Values { get; init; }

    [Description("connect/disconnect: the output end, '<node>.<port>' such as 'blur.Out.color', where <node> is a ref, an Id or '@Type'. Connecting from a node that grows ports (GroupInput, a loop input) with '<node>' alone adds a port matching the input.")]
    public string? From { get; init; }

    [Description("connect/disconnect: the input end, '<node>.<port>' such as 'composite.In.glow'. Connecting to a node that grows ports (GroupOutput, a loop output) with '<node>' alone adds a port matching the output.")]
    public string? To { get; init; }

    [Description("disconnect: '<node>.<port>' whose every connection is removed, in place of from and to.")]
    public string? Port { get; init; }

    [Description("connect: replace the connection the input already has instead of failing.")]
    public bool Replace { get; init; }

    // Members the operation does not have, which a misspelled field would otherwise drop without a word.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownFields { get; init; }
}
