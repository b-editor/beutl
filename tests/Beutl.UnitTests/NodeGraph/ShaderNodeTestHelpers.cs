using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.NodeGraph;

internal static class ShaderNodeTestHelpers
{
    public static T Add<T>(this GraphModel graph, T node) where T : GraphNode
    {
        graph.Nodes.Add(node);
        return node;
    }

    /// <summary>The input a pass generated for what the code declares under <paramref name="name"/>.</summary>
    public static IInputPort In(this GraphNode node, string name)
        => (IInputPort)node.Items.Single(item => item is IInputPort && item.Name.Value == "In." + name);

    public static IOutputPort Out(this GraphNode node, string name)
        => (IOutputPort)node.Items.Single(item => item is IOutputPort && item.Name.Value == "Out." + name);

    public static void Set(this GraphNode node, string name, object? value) => node.In(name).Property!.SetValue(value);

    public static IInputPort NamedInput(this GraphNode node, string name)
        => (IInputPort)node.Items.Single(item => item is IInputPort && item.Name.Value == name);

    /// <summary>Records what the only graph output of <paramref name="graph"/> would hand to the renderer.</summary>
    public static ShaderGraphDescription Record(GraphModel graph)
    {
        using var snapshot = new GraphSnapshot();
        snapshot.Build(graph, CompositionContext.Default);
        snapshot.Evaluate(CompositionContext.Default);
        return Record(graph, snapshot);
    }

    public static ShaderGraphDescription Record(GraphModel graph, GraphSnapshot snapshot)
    {
        ShaderGraphDescription? description = Picture(graph, snapshot)?.Description;
        Assert.That(description, Is.Not.Null, "Recording failed.");
        return description!;
    }

    /// <summary>What the only graph output of <paramref name="graph"/> received.</summary>
    public static ShaderGraphRenderNode? Picture(GraphModel graph, GraphSnapshot snapshot)
    {
        OutputNode output = graph.Nodes.OfType<OutputNode>().Single();
        return snapshot.GetItemValue(snapshot.FindSlotIndex(output), output.Items.IndexOf(output.InputPort))!.GetBoxed()
            as ShaderGraphRenderNode;
    }

    public static ushort[] Render(ShaderGraphDescription graph, float scale)
        => VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bounds = new Rect(0, 0, 32, 24);
            using RenderTarget backing = RenderTarget.Create((int)(32 * scale), (int)(24 * scale))!;
            backing.Value.Canvas.Clear(SKColors.Empty);
            backing.Value.Canvas.Scale(scale);
            using (var paint = new SKPaint { Color = SKColors.White })
                backing.Value.Canvas.DrawRect(4, 4, 24, 16, paint);
            backing.Value.Canvas.Flush();
            using var targets = new EffectTargets
            {
                new EffectTarget(backing, bounds, EffectiveScale.At(scale), PixelRect.FromRect(bounds, scale))
            };
            using var context = new FilterEffectContext(bounds);
            context.ShaderGraph(graph);
            using var builder = new SKImageFilterBuilder();
            using var executor = new FilterEffectExecutor(targets, builder, RenderIntent.Preview,
                RenderRequestPurpose.Auxiliary, drawableBrushMaterializer: null,
                outputScale: scale, workingScale: scale, maxWorkingScale: scale);
            executor.Apply(context);
            executor.Flush(false);
            using Bitmap bitmap = targets.Single().RenderTarget!.Snapshot();
            return bitmap.GetPixelSpan<ushort>().ToArray();
        });
}
