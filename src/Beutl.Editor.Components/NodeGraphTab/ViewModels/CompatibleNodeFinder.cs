using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Group;

namespace Beutl.Editor.Components.NodeGraphTab.ViewModels;

internal static class CompatibleNodeFinder
{
    internal sealed record Candidate(GraphNode Node, IReadOnlyList<INodePort?> Ports);

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

            try
            {
                if (Activator.CreateInstance(registry.Type) is not GraphNode node) return;
                INodePort?[] ports = node.EnumerateMembers().OfType<INodePort>()
                    .Where(port => CanConnect(source, port)).Cast<INodePort?>().ToArray();
                if (ports.Length == 0 && node is IDynamicPortNode dynamic
                    && (source is IOutputPort && dynamic.PossibleLocation.HasFlag(NodePortLocation.Left)
                        || source is IInputPort && dynamic.PossibleLocation.HasFlag(NodePortLocation.Right)))
                    ports = [null];
                if (ports.Length > 0) result.Add(registry, new Candidate(node, ports));
            }
            catch (Exception ex) when (ex is TargetInvocationException or MissingMethodException
                                       or MemberAccessException or TypeLoadException)
            {
                // An unloaded or broken extension must not prevent the rest of the picker from opening.
            }
        }
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
