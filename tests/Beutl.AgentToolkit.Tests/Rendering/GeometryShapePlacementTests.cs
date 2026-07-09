using Beutl.AgentToolkit.Rendering;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Rendering;

public sealed class GeometryShapePlacementTests
{
    // Deliberately kept placement model: drawn center = alignment-resolved center +
    // geometry path bounds origin. Skill/schema authoring rules encode this formula;
    // if this test fails, the rendering model changed and those rules must be revisited.
    [TestCase(0f, 0f)]
    [TestCase(-30f, -20f)]
    [TestCase(30f, 20f)]
    public async Task Drawn_center_is_alignment_center_plus_path_bounds_origin(float originX, float originY)
    {
        string dir = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var scene = new Scene(320, 180, "Scene") { Uri = new Uri(Path.Combine(dir, "Scene.scene")) };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(2),
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };

        var figure = new PathFigure();
        figure.StartPoint.CurrentValue = new Point(originX, originY);
        figure.Segments.Add(new LineSegment(new Point(originX + 100, originY + 55)));
        figure.Segments.Add(new LineSegment(new Point(originX, originY + 110)));
        figure.IsClosed.CurrentValue = true;
        var shape = new GeometryShape
        {
            Name = "placement probe",
            Data = { CurrentValue = new PathGeometry { Figures = { figure } } },
            Fill = { CurrentValue = new SolidColorBrush(Colors.White) }
        };
        element.AddObject(shape);
        scene.Children.Add(element);

        var renderer = new StillRenderer();
        RenderStillResponse response = await renderer.RenderAsync(
            scene,
            TimeSpan.FromSeconds(1),
            Path.Combine(dir, "placement.png"),
            renderScale: 1,
            CancellationToken.None);

        var visibility = response.VisibilityAnalysis!;
        double centerX = (visibility.Left + visibility.Right + 1) / 2.0;
        double centerY = (visibility.Top + visibility.Bottom + 1) / 2.0;

        Assert.Multiple(() =>
        {
            Assert.That(centerX, Is.EqualTo(160 + originX).Within(1.5));
            Assert.That(centerY, Is.EqualTo(90 + originY).Within(1.5));
        });
    }
}
