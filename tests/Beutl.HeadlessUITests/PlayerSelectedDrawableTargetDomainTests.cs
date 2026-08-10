using Avalonia.Headless.NUnit;
using Beutl.Composition;
using Beutl.Editor.Models;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class PlayerSelectedDrawableTargetDomainTests
{
    [AvaloniaTest]
    public async Task SelectedDrawableMethodsPassTheFrameDomainIntoResourceEvaluation()
    {
        await TestReset.ResetShellAsync();
        string name = $"selected-drawable-domain-{Guid.NewGuid():N}";
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        Project project = (await TestShell.Project.CreateProject(64, 48, 30, 44100, name, location))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;

        var measureProbe = new TargetDomainProbeDrawable();
        var drawProbe = new TargetDomainProbeDrawable();

        TargetDomainProbeException measureFailure = await CaptureProbeFailureAsync(
            async () => await editor.Player.MeasureSelectedDrawable(measureProbe));
        TargetDomainProbeException drawFailure = await CaptureProbeFailureAsync(
            async () => await editor.Player.DrawSelectedDrawable(drawProbe));

        Assert.Multiple(() =>
        {
            Assert.That(measureFailure, Is.SameAs(measureProbe.Sentinel));
            Assert.That(drawFailure, Is.SameAs(drawProbe.Sentinel));
            Assert.That(measureProbe.ObservedTargetDomain, Is.EqualTo(new Rect(0, 0, 64, 48)));
            Assert.That(drawProbe.ObservedTargetDomain, Is.EqualTo(new Rect(0, 0, 64, 48)));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FullTargetUtilityWithoutACompositionDomainReproducesTheOldFailure(bool preview)
    {
        using var fullScope = new LayerRenderNode(default);
        fullScope.AddChild(new TestOpaqueSourceRenderNode(new Rect(3, 5, 24, 18)));
        var drawable = new NodeGraphDrawable();
        GraphModel model = drawable.Model.CurrentValue!;
        var source = new FixedRenderNodeGraphNode(fullScope);
        var output = new OutputNode();
        model.Nodes.Add(source);
        model.Nodes.Add(output);
        model.Connect(output.InputPort, source.Output);
        if (preview)
        {
            var previewNode = new PreviewNode();
            model.Nodes.Add(previewNode);
            model.Connect(previewNode.Input, source.Output);
            GetPreviewMonitor(previewNode).IsEnabled = true;
        }
        else
        {
            var measureNode = new MeasureNode();
            model.Nodes.Add(measureNode);
            model.Connect(measureNode.Input, source.Output);
        }

        Assert.That(
            () => drawable.ToResource(new CompositionContext(TimeSpan.FromSeconds(3))),
            Throws.TypeOf<RenderTargetDomainRequiredException>());
    }

    [Test]
    public void ResourceEvaluationUsesTheFrameDomainForFullTargetMeasureAndPreview()
    {
        var frame = TimeSpan.FromSeconds(3);
        var frameSize = new PixelSize(64, 48);
        var contentBounds = new Rect(3, 5, 24, 18);
        using var fullScope = new LayerRenderNode(default);
        fullScope.AddChild(new TestOpaqueSourceRenderNode(contentBounds));

        NodeGraphDrawable drawable = CreateUtilityDrawable(
            fullScope,
            out MeasureCaptureGraphNode capture,
            out PreviewNode preview);
        NodeMonitor<Ref<Bitmap>?> monitor = GetPreviewMonitor(preview);
        monitor.IsEnabled = true;

        CompositionContext context = PlayerViewModel.CreateSelectedDrawableCompositionContext(frame, frameSize);
        using Drawable.Resource resource = drawable.ToResource(context);

        Assert.Multiple(() =>
        {
            Assert.That(context.Time, Is.EqualTo(frame));
            Assert.That(context.TargetDomain, Is.EqualTo(new Rect(0, 0, frameSize.Width, frameSize.Height)));
            Assert.That(capture.Value, Is.EqualTo(contentBounds));
            Assert.That(monitor.Value, Is.Not.Null);
            Assert.That(monitor.Value!.Value.Width, Is.EqualTo(frameSize.Width));
            Assert.That(monitor.Value.Value.Height, Is.EqualTo(frameSize.Height));
        });

        monitor.Value?.Dispose();
    }

    private static NodeMonitor<Ref<Bitmap>?> GetPreviewMonitor(PreviewNode node)
        => node.Items.OfType<NodeMonitor<Ref<Bitmap>?>>().Single();

    private static async Task<TargetDomainProbeException> CaptureProbeFailureAsync(Func<Task> action)
    {
        try
        {
            await action().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Fail("Expected the target-domain probe to stop rendering.");
            return null!;
        }
        catch (TargetDomainProbeException ex)
        {
            return ex;
        }
    }

    private static NodeGraphDrawable CreateUtilityDrawable(
        RenderNode root,
        out MeasureCaptureGraphNode capture,
        out PreviewNode preview)
    {
        var drawable = new NodeGraphDrawable();
        GraphModel model = drawable.Model.CurrentValue!;
        var source = new FixedRenderNodeGraphNode(root);
        var measure = new MeasureNode();
        capture = new MeasureCaptureGraphNode();
        preview = new PreviewNode();
        var output = new OutputNode();
        model.Nodes.Add(source);
        model.Nodes.Add(measure);
        model.Nodes.Add(capture);
        model.Nodes.Add(preview);
        model.Nodes.Add(output);
        model.Connect(measure.Input, source.Output);
        model.Connect(capture.X, measure.X);
        model.Connect(capture.Y, measure.Y);
        model.Connect(capture.Width, measure.Width);
        model.Connect(capture.Height, measure.Height);
        model.Connect(preview.Input, source.Output);
        model.Connect(output.InputPort, source.Output);
        return drawable;
    }
}

internal sealed partial class FixedRenderNodeGraphNode : GraphNode
{
    public FixedRenderNodeGraphNode(RenderNode value)
    {
        Value = value;
        Output = AddOutput<RenderNode?>("Output");
    }

    public RenderNode Value { get; }

    public OutputPort<RenderNode?> Output { get; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            Output = GetOriginal().Value;
        }
    }
}

internal sealed partial class MeasureCaptureGraphNode : GraphNode
{
    public MeasureCaptureGraphNode()
    {
        X = AddInput<float>("X");
        Y = AddInput<float>("Y");
        Width = AddInput<float>("Width");
        Height = AddInput<float>("Height");
    }

    public InputPort<float> X { get; }

    public InputPort<float> Y { get; }

    public InputPort<float> Width { get; }

    public InputPort<float> Height { get; }

    public Rect Value { get; private set; }

    public partial class Resource
    {
        public override void Update(GraphCompositionContext context)
        {
            MeasureCaptureGraphNode node = GetOriginal();
            node.Value = new Rect(X, Y, Width, Height);
        }
    }
}

internal sealed class TestOpaqueSourceRenderNode(Rect bounds) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        context.Publish(context.OpaqueSource(OpaqueRenderDescription.CreateRequestLocal(
            session =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(static canvas => canvas.Clear(Colors.CornflowerBlue));
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            RenderScaleContract.MaterializeAtWorkingScale)));
    }
}

[SuppressResourceClassGeneration]
internal sealed class TargetDomainProbeDrawable : Drawable
{
    public TargetDomainProbeException Sentinel { get; } = new();

    public Rect? ObservedTargetDomain { get; private set; }

    public override Resource ToResource(CompositionContext context)
    {
        ObservedTargetDomain = context.TargetDomain;
        throw Sentinel;
    }

    protected override Size MeasureCore(Size availableSize, Resource resource) => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Resource resource)
    {
    }
}

internal sealed class TargetDomainProbeException : Exception;
