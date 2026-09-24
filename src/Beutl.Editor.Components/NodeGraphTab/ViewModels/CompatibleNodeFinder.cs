using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

internal static class CompatibleNodeFinder
{
    internal sealed record Candidate(GraphNodeRegistry.RegistryItem Registry, IReadOnlyList<PortChoice?> Ports);

    internal sealed record PortChoice(
        int Index,
        string Name,
        Type? AssociatedType,
        bool IsInput,
        bool IsOutput,
        bool CanConnectInput,
        DisplayAttribute? Display,
        string? RootName,
        DisplayAttribute? RootDisplay)
    {
        public string DisplayName
        {
            get
            {
                string name = Display?.GetName() ?? Name;
                return RootName is null ? name : $"{RootDisplay?.GetName() ?? RootName} / {name}";
            }
        }
    }

    private sealed record NodeDescriptor(IReadOnlyList<PortChoice> Ports, NodePortLocation? DynamicLocation)
    {
        public static NodeDescriptor Empty { get; } = new([], null);
    }

    private static readonly ConditionalWeakTable<Type, NodeDescriptor> s_descriptors = new();
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(CompatibleNodeFinder));

    private static readonly HashSet<Type> s_numericTypes =
    [
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(nint), typeof(nuint), typeof(Int128), typeof(UInt128),
        typeof(Half), typeof(float), typeof(double), typeof(decimal), typeof(char)
    ];

    public static IReadOnlyDictionary<GraphNodeRegistry.RegistryItem, Candidate> Find(
        GraphModel graph, INodePort source, IEnumerable<GraphNodeRegistry.BaseRegistryItem> registered)
    {
        var result = new Dictionary<GraphNodeRegistry.RegistryItem, Candidate>(ReferenceEqualityComparer.Instance);
        if (source.AssociatedType == null) return result;
        if (source is IInputPort input && (input is not IListInputPort && !input.Connection.IsNull
                                           || input.FindHierarchicalParent<GraphNode>() is { } owner
                                           && !owner.CanConnectInput(input)))
            return result;

        foreach (GraphNodeRegistry.BaseRegistryItem item in registered)
            Visit(item);
        return result;

        void Visit(GraphNodeRegistry.BaseRegistryItem item)
        {
            if (item is GraphNodeRegistry.GroupableRegistryItem group)
            {
                foreach (GraphNodeRegistry.BaseRegistryItem child in group.Items) Visit(child);
                return;
            }

            if (item is not GraphNodeRegistry.RegistryItem registry) return;
            if (graph is not GraphGroup
                && (typeof(GroupInput).IsAssignableFrom(registry.Type)
                    || typeof(GroupOutput).IsAssignableFrom(registry.Type)))
                return;
            if (graph is GraphGroup existingGroup
                && (typeof(GroupInput).IsAssignableFrom(registry.Type) && existingGroup.Nodes.Any(n => n is GroupInput)
                    || typeof(GroupOutput).IsAssignableFrom(registry.Type) && existingGroup.Nodes.Any(n => n is GroupOutput)))
                return;

            // OutputNode declares object but the graph renderers consume RenderNode values only.
            if (typeof(OutputNode).IsAssignableFrom(registry.Type)
                && (graph is GraphGroup || source is not IOutputPort output
                    || output.AssociatedType is not { } type
                    || (type != typeof(object) && !typeof(RenderNode).IsAssignableFrom(type))))
                return;

            NodeDescriptor descriptor = s_descriptors.GetValue(registry.Type, CreateDescriptor);
            PortChoice?[] ports = descriptor.Ports.Where(port => CanConnect(source, port))
                .Cast<PortChoice?>().ToArray();
            if (ports.Length == 0 && descriptor.DynamicLocation is { } location
                && (source is IOutputPort && location.HasFlag(NodePortLocation.Left)
                    || source is IInputPort && location.HasFlag(NodePortLocation.Right)))
                ports = [null];
            if (ports.Length > 0) result.Add(registry, new Candidate(registry, ports));
        }
    }

    private static NodeDescriptor CreateDescriptor(Type type)
    {
        GraphNode? node = null;
        try
        {
            node = Activator.CreateInstance(type) as GraphNode;
            if (node == null) return NodeDescriptor.Empty;
            PortChoice[] ports = node.EnumerateMembers().OfType<INodePort>()
                .Select((port, index) =>
                {
                    NodeMember? root = (port as INestedInputPort)?.RootMember.Value;
                    bool canConnectInput = port is IInputPort input
                        && (input is IListInputPort || input.Connection.IsNull)
                        && node.CanConnectInput(input);
                    return new PortChoice(index, port.Name, port.AssociatedType,
                        port is IInputPort, port is IOutputPort, canConnectInput,
                        port.Display, root?.Name, root?.Display);
                }).ToArray();
            return new NodeDescriptor(ports, (node as IDynamicPortNode)?.PossibleLocation);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            s_logger.LogWarning(ex, "Skipping unavailable node type {NodeType} in the port drop picker", type);
            return NodeDescriptor.Empty;
        }
        finally
        {
            try
            {
                (node as IDisposable)?.Dispose();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                s_logger.LogWarning(ex, "Failed to dispose node metadata probe {NodeType}", type);
            }
        }
    }

    internal static bool TryCreateSelection(Candidate candidate, PortChoice? choice, INodePort source,
        out GraphNode? node, out INodePort? port)
    {
        node = null;
        port = null;
        bool selected = false;
        try
        {
            node = Activator.CreateInstance(candidate.Registry.Type) as GraphNode;
            if (node == null) return false;
            if (choice == null) return selected = node is IDynamicPortNode;

            port = node.EnumerateMembers().OfType<INodePort>().ElementAtOrDefault(choice.Index);
            return selected = port != null && port.Name == choice.Name
                && port.AssociatedType == choice.AssociatedType && CanConnect(source, port);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            s_logger.LogWarning(ex, "Failed to create selected node type {NodeType}", candidate.Registry.Type);
            return false;
        }
        finally
        {
            if (!selected)
            {
                try
                {
                    (node as IDisposable)?.Dispose();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    s_logger.LogWarning(ex, "Failed to dispose rejected node type {NodeType}", candidate.Registry.Type);
                }
                node = null;
                port = null;
            }
        }
    }

    private static bool CanConnect(INodePort source, PortChoice candidate)
    {
        if (source.AssociatedType is not { } sourceType
            || candidate.AssociatedType is not { } candidateType) return false;
        if (source is IOutputPort && candidate.IsInput && candidate.CanConnectInput)
            return CanPropagate(sourceType, candidateType);
        if (source is IInputPort && candidate.IsOutput)
            return CanPropagate(candidateType, sourceType);
        return false;
    }

    internal static bool CanConnect(INodePort source, INodePort candidate)
    {
        IInputPort? input;
        IOutputPort? output;
        if (source is IOutputPort sourceOutput && candidate is IInputPort candidateInput)
        {
            input = candidateInput;
            output = sourceOutput;
        }
        else if (source is IInputPort sourceInput && candidate is IOutputPort candidateOutput)
        {
            input = sourceInput;
            output = candidateOutput;
        }
        else
        {
            return false;
        }

        if (input is not IListInputPort && !input.Connection.IsNull) return false;
        if (input.FindHierarchicalParent<GraphNode>() is { } owner && !owner.CanConnectInput(input))
            return false;
        if (output.AssociatedType is not { } outputType || input.AssociatedType is not { } inputType)
            return false;
        return CanPropagate(outputType, inputType);
    }

    // Keep the known conversions in step with ItemValueHelper's receivers. Value-dependent
    // conversions such as string parsing are omitted; explicit object outputs remain dynamic.
    internal static bool CanPropagate(Type outputType, Type inputType)
    {
        // Switch and expression nodes deliberately expose object outputs whose value type
        // is determined at runtime. They can supply any input when that value matches.
        if (outputType == typeof(object)) return true;
        if (inputType.IsAssignableFrom(outputType)
            || Nullable.GetUnderlyingType(inputType) == outputType)
            return true;
        if ((inputType == typeof(Drawable) && typeof(RenderNode).IsAssignableFrom(outputType))
            || (inputType == typeof(Transform) && outputType == typeof(Matrix)))
            return true;

        if (s_numericTypes.Contains(outputType))
            return (s_numericTypes.Contains(inputType) && inputType != typeof(char))
                   || inputType == typeof(TimeSpan)
                   || inputType == typeof(Thickness)
                   || inputType == typeof(Vector)
                   || inputType == typeof(Point)
                   || inputType == typeof(Size)
                   || inputType == typeof(Rect)
                   || inputType == typeof(PixelPoint)
                   || inputType == typeof(PixelSize)
                   || inputType == typeof(PixelRect);

        return inputType == typeof(Vector) && (outputType == typeof(Size) || outputType == typeof(Point))
               || inputType == typeof(Point) && (outputType == typeof(Size) || outputType == typeof(Vector))
               || inputType == typeof(Size) && (outputType == typeof(Point) || outputType == typeof(Vector))
               || inputType == typeof(Rect) && outputType == typeof(Size)
               || inputType == typeof(PixelPoint) && outputType == typeof(PixelSize)
               || inputType == typeof(PixelSize) && outputType == typeof(PixelPoint)
               || inputType == typeof(PixelRect) && outputType == typeof(PixelSize);
    }
}
