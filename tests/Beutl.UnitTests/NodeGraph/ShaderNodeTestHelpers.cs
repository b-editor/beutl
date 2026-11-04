using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
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

    /// <summary>Records what the only apply node of <paramref name="graph"/> would hand to the renderer.</summary>
    public static ShaderGraphDescription Record(GraphModel graph)
    {
        using var snapshot = new GraphSnapshot();
        snapshot.Build(graph, CompositionContext.Default);
        snapshot.Evaluate(CompositionContext.Default);
        return Record(graph, snapshot);
    }

    public static ShaderGraphDescription Record(GraphModel graph, GraphSnapshot snapshot)
    {
        ShaderApplyNode apply = graph.Nodes.OfType<ShaderApplyNode>().Single();
        Assert.That(apply.Error.Value, Is.Null);
        int slot = snapshot.FindSlotIndex(apply);
        var image = (ShaderGraphValue<ShaderImage>)snapshot.GetItemValue(slot, apply.Items.IndexOf(apply.Image))!.GetBoxed()!;
        var color = (ColorEncoding)snapshot.GetItemValue(slot, apply.Items.IndexOf(apply.Color))!.GetBoxed()!;
        return ShaderGraphRecordingContext.Build(image, color);
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
