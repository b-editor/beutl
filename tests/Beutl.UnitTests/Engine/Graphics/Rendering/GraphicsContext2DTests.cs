using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Microsoft.Extensions.Logging;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class GraphicsContext2DTests
{
    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
    }

    [Test]
    public void ShouldTriggerOnUntrackedEvent()
    {
        var drawable = new RectShape();
        drawable.AlignmentX.CurrentValue = AlignmentX.Center;
        drawable.AlignmentY.CurrentValue = AlignmentY.Center;
        drawable.TransformOrigin.CurrentValue = RelativePoint.Center;
        drawable.Width.CurrentValue = 100;
        drawable.Height.CurrentValue = 100;
        drawable.Fill.CurrentValue = Brushes.White;
        drawable.FilterEffect.CurrentValue = new FilterEffectGroup { Children = { new SplitEffect(), new InnerShadow() } };
        drawable.Transform.CurrentValue = new TransformGroup { Children = { new RotationTransform(), new ScaleTransform() } };
        var resource = drawable.ToResource(RenderContext.Default);

        var node = new DrawableRenderNode(resource);
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        drawable.Render(context, resource);

        ((FilterEffectGroup)drawable.FilterEffect.CurrentValue).Children.RemoveAt(0);
        var updateOnly = false;
        resource.Update(drawable, RenderContext.Default, ref updateOnly);
        context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        bool triggered = false;
        RenderNode? untrackedNode = null;
        context.OnUntracked = n =>
        {
            triggered = true;
            untrackedNode = n;
        };
        drawable.Render(context, resource);

        Assert.That(triggered, Is.True);
        Assert.That(untrackedNode, Is.Not.Null);
        Assert.That(untrackedNode, Is.TypeOf<FilterEffectRenderNode>());
    }

    [Test]
    public void Clear_ShouldCreateClearRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));

        context.Clear();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<ClearRenderNode>());
    }

    [Test]
    public void ClearWithColor_ShouldCreateClearRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));

        context.Clear(Colors.White);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<ClearRenderNode>());
        Assert.That(((ClearRenderNode)node.Children[0]).Color, Is.EqualTo(Colors.White));
    }

    [Test]
    public void DrawImageSource_ShouldCreateImageSourceRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        var image = new Mock<IImageSource>();
        image.Setup(i => i.FrameSize).Returns(new PixelSize(100, 100));
        image.Setup(i => i.Clone()).Returns(() => image.Object);

        context.DrawImageSource(image.Object, Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<ImageSourceRenderNode>());
    }

    [Test]
    public void DrawVideoSource_ShouldCreateVideoSourceRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        var video = new Mock<IVideoSource>();
        video.Setup(v => v.FrameSize).Returns(new PixelSize(100, 100));
        video.Setup(v => v.FrameRate).Returns(new Rational(30));
        video.Setup(v => v.Clone()).Returns(() => video.Object);

        context.DrawVideoSource(video.Object, TimeSpan.Zero, Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<VideoSourceRenderNode>());
    }

    [Test]
    public void DrawEllipse_ShouldCreateEllipseRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));

        context.DrawEllipse(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<EllipseRenderNode>());
    }

    [Test]
    public void DrawGeometry_ShouldCreateGeometryRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        var geometry = new EllipseGeometry();
        geometry.Width.CurrentValue = 100;
        geometry.Height.CurrentValue = 100;
        var resource = geometry.ToResource(RenderContext.Default);

        context.DrawGeometry(resource, Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<GeometryRenderNode>());
    }

    [Test]
    public void DrawRectangle_ShouldCreateRectangleRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));

        context.DrawRectangle(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<RectangleRenderNode>());
    }

    [Test]
    public void DrawDrawable_ShouldCreateDrawableRenderNode()
    {
        var drawable = new RectShape();
        var resource = drawable.ToResource(RenderContext.Default);
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));

        context.DrawDrawable(resource);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<DrawableRenderNode>());
    }

    [Test]
    public void DrawNode_ShouldAddPassedNodeDirectly()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        var child = new ContainerRenderNode();

        context.DrawNode(child);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.EqualTo(child));
    }

    [Test]
    public void DrawBackdrop_ShouldCreateDrawBackdropRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        var backdrop = new Mock<IBackdrop>();

        context.DrawBackdrop(backdrop.Object);

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<DrawBackdropRenderNode>());
    }

    [Test]
    public void Snapshot_ShouldCreateSnapshotBackdropRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));

        _ = context.Snapshot();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<SnapshotBackdropRenderNode>());
    }

    [Test]
    public void Push_ShouldCreatePushRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));

        context.Push().Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<PushRenderNode>());
    }

    [Test]
    public void PushLayer_ShouldCreateLayerRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));

        context.PushLayer().Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<LayerRenderNode>());
    }

    [Test]
    public void PushBlendMode_ShouldCreateBlendModeRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));

        context.PushBlendMode(BlendMode.Clear).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<BlendModeRenderNode>());
    }

    [Test]
    public void PushClip_ShouldCreateRectClipRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));

        context.PushClip(new Rect(0, 0, 100, 100)).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<RectClipRenderNode>());
    }

    [Test]
    public void PushClipGeometry_ShouldCreateGeometryClipRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        var geometry = new EllipseGeometry();
        geometry.Width.CurrentValue = 100;
        geometry.Height.CurrentValue = 100;
        var resource = geometry.ToResource(RenderContext.Default);

        context.PushClip(resource).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<GeometryClipRenderNode>());
    }

    [Test]
    public void PushOpacity_ShouldCreateOpacityRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));

        context.PushOpacity(0.5f).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<OpacityRenderNode>());
    }

    [Test]
    public void PushFilterEffect_ShouldCreateFilterEffectRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        var effect = new Blur();
        var resource = effect.ToResource(RenderContext.Default);

        context.PushFilterEffect(resource).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<FilterEffectRenderNode>());
    }

    [Test]
    public void PushOpacityMask_ShouldCreateOpacityMaskRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        var mask = Brushes.Resource.White;
    
        context.PushOpacityMask(mask, new Rect(0, 0, 100, 100)).Dispose();
    
        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<OpacityMaskRenderNode>());
    }

    [Test]
    public void PushTransform_ShouldCreateTransformRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        var transform = new RotationTransform();
        var resource = transform.ToResource(RenderContext.Default);

        context.PushTransform(resource).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<TransformRenderNode>());
    }

    [Test]
    public void PushTransformGroup_ShouldCreateTransformRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        var transform = new TransformGroup { Children = { new RotationTransform(), new ScaleTransform() } };
        var resource = transform.ToResource(RenderContext.Default);

        context.PushTransform(resource).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<TransformRenderNode>());
    }

    [Test]
    public void PushMatrixTransform_ShouldCreateTransformRenderNode()
    {
        var node = new ContainerRenderNode();
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        var matrix = Matrix.CreateRotation(45);

        context.PushTransform(matrix).Dispose();

        Assert.That(node.Children, Is.Not.Empty);
        Assert.That(node.Children[0], Is.InstanceOf<TransformRenderNode>());
    }
}
