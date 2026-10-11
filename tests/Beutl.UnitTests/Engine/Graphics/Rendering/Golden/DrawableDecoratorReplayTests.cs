using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class DrawableDecoratorReplayTests
{
    [TestCase(0.5f, false)]
    [TestCase(1f, false)]
    [TestCase(2f, false)]
    [TestCase(0.5f, true)]
    [TestCase(1f, true)]
    [TestCase(2f, true)]
    public void AnimatedDecorator_WithDifferentChildBounds_MatchesFreshFrames(float outputScale, bool scaleTransform)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            Scene scene = CreateScene(scaleTransform);
            using var sequential = CreateRenderer(scene, outputScale);
            sequential.Render(sequential.Compositor.EvaluateGraphics(TimeSpan.Zero));
            using Bitmap first = sequential.Snapshot();

            foreach (int seconds in new[] { 1, 2 })
            {
                var time = TimeSpan.FromSeconds(seconds);
                sequential.Render(sequential.Compositor.EvaluateGraphics(time));
                using Bitmap actual = sequential.Snapshot();

                using var fresh = CreateRenderer(scene, outputScale);
                fresh.Render(fresh.Compositor.EvaluateGraphics(time));
                using Bitmap expected = fresh.Snapshot();

                Assert.That(expected.GetPixelSpan().SequenceEqual(first.GetPixelSpan()), Is.False,
                    "The animated transform must visibly change the frame.");
                GoldenImageHarness.AssertByteIdentical(expected, actual);
            }
        });
    }

    private static SceneRenderer CreateRenderer(Scene scene, float outputScale)
    {
        return new SceneRenderer(scene, RenderIntent.Delivery, outputScale, disableResourceShare: true)
        {
            CacheOptions = RenderCacheOptions.Disabled,
        };
    }

    private static Scene CreateScene(bool scaleTransform)
    {
        var decorator = new DrawableDecorator();
        decorator.Children.Add(CreateRectangle(40, 24, 30, 25, Colors.OrangeRed));
        decorator.Children.Add(CreateRectangle(62, 40, 155, 78, Colors.CornflowerBlue));

        IProperty<float> parameter;
        if (scaleTransform)
        {
            var transform = new ScaleTransform();
            decorator.Transform.CurrentValue = transform;
            parameter = transform.Scale;
        }
        else
        {
            var transform = new RotationTransform();
            decorator.Transform.CurrentValue = transform;
            parameter = transform.Rotation;
        }

        parameter.Animation = new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = scaleTransform ? 100 : 0 },
                new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = scaleTransform ? 150 : 30 },
                new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(2), Value = scaleTransform ? 75 : -20 },
            },
        };

        var scene = new Scene(256, 144, "decorator") { Uri = new Uri("file:///decorator/scene") };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(4),
            ZIndex = 0,
            Uri = new Uri("file:///decorator/element"),
        };
        element.AddObject(decorator);
        scene.Children.Add(element);
        return scene;
    }

    private static RectShape CreateRectangle(float width, float height, float x, float y, Color color)
    {
        return new RectShape
        {
            Width = { CurrentValue = width },
            Height = { CurrentValue = height },
            Fill = { CurrentValue = new SolidColorBrush(color) },
            AlignmentX = { CurrentValue = AlignmentX.Left },
            AlignmentY = { CurrentValue = AlignmentY.Top },
            Transform = { CurrentValue = new TranslateTransform(x, y) },
        };
    }
}
