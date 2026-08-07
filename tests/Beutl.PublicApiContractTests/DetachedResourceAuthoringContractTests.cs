using System.Numerics;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Meshes;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

/// <summary>
/// This project is not a friend of <c>Beutl.Engine</c>, so everything here compiles only against the public
/// authoring surface an out-of-tree plugin sees, with the same generator the SDK ships as an analyzer.
/// </summary>
[TestFixture]
public sealed class DetachedResourceAuthoringContractTests
{
    [Test]
    public void APluginGeometryAuthoredOnTheResource_AnswersWhileDetached()
    {
        using var detached = new PluginGeometry.Resource { Side = 24 };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(detached.IsAttached, Is.False);
            Assert.That(detached.GetOriginal(), Is.Null);
            Assert.That(detached.Bounds, Is.EqualTo(new Rect(0, 0, 24, 24)));
            Assert.That(detached.FillContains(new Point(12, 12)), Is.True);
        }
    }

    [Test]
    public void APluginSegmentAuthoredOnTheResource_AnswersWhileDetached()
    {
        using var detached = new PathGeometry.Resource
        {
            Figures =
            {
                new PathFigure.Resource
                {
                    StartPoint = new Point(0, 0),
                    Segments = { new PluginSegment.Resource { To = new Point(40, 15) } },
                },
            },
        };

        Assert.That(detached.Bounds, Is.EqualTo(new Rect(0, 0, 40, 15)));
    }

    [Test]
    public void APluginMeshAuthoredOnTheResource_AnswersWhileDetached()
    {
        using var detached = new PluginMesh.Resource { Extent = 3 };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(detached.VertexCount, Is.EqualTo(3));
            Assert.That(detached.IndexCount, Is.EqualTo(3));
            Assert.That(detached.GetBoundingBox().Max.X, Is.EqualTo(3));
        }
    }

    [Test]
    public void AnAttachedResource_StillReportsItsBackingObject()
    {
        var geometry = new PluginGeometry();
        geometry.Side.CurrentValue = 10;
        using Geometry.Resource attached = geometry.ToResource(CompositionContext.Default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(attached.IsAttached, Is.True);
            Assert.That(attached.RequireOriginal(), Is.SameAs(geometry));
            Assert.That(attached.Bounds, Is.EqualTo(new Rect(0, 0, 10, 10)));
        }
    }

    [Test]
    public void RequireOriginal_OnADetachedResource_ThrowsAnExplanatoryInvalidOperationException()
    {
        using var detached = new PluginGeometry.Resource();

        var exception = Assert.Throws<InvalidOperationException>(() => detached.RequireOriginal());
        Assert.That(exception!.Message, Does.Contain(nameof(EngineObject.ToResource)));
    }

    [Test]
    public void TwoDetachedPens_AreNotTreatedAsOneByTheStrokeCache()
    {
        using var geometry = new PluginGeometry.Resource { Side = 100 };
        using var thin = DetachedPen(thickness: 4);
        using var thick = DetachedPen(thickness: 20);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(geometry.GetRenderBounds(thin), Is.EqualTo(new Rect(-2, -2, 104, 104)));
            Assert.That(geometry.GetRenderBounds(thick), Is.EqualTo(new Rect(-10, -10, 120, 120)));
        }
    }

    /// <summary>
    /// The generator emits each value-property backing field as <c>default!</c>, so a hand-built resource does
    /// not inherit the default its <c>IProperty</c> declares. This pins that gap: a plugin author has to set
    /// <c>TrimEnd</c> and <c>Opacity</c> by hand, and a future change that closes the gap has to update this
    /// contract deliberately.
    /// </summary>
    [Test]
    public void ADetachedResource_StartsItsValuePropertiesAtDefaultRatherThanTheDeclaredDefault()
    {
        using var bare = new Pen.Resource { Thickness = 4, Brush = new SolidColorBrush.Resource() };
        using Pen.Resource attached = new Pen
        {
            Brush = { CurrentValue = Brushes.Black },
            Thickness = { CurrentValue = 4 },
        }.ToResource(CompositionContext.Default);
        using var geometry = new PluginGeometry.Resource { Side = 100 };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bare.TrimEnd, Is.Zero);
            Assert.That(attached.TrimEnd, Is.EqualTo(100));
            Assert.That(bare.MiterLimit, Is.Zero);
            Assert.That(attached.MiterLimit, Is.EqualTo(10));
            Assert.That(new SolidColorBrush.Resource { Color = Colors.Red }.Opacity, Is.Zero);
            Assert.That(geometry.GetRenderBounds(bare), Is.EqualTo(default(Rect)),
                "TrimEnd = 0 trims the stroke away, so an unset declared default is not inert");
        }
    }

    private static Pen.Resource DetachedPen(float thickness)
    {
        return new Pen.Resource
        {
            Brush = new SolidColorBrush.Resource { Color = Colors.Black, Opacity = 100 },
            Thickness = thickness,
            TrimEnd = 100,
            MiterLimit = 10,
        };
    }
}

public sealed partial class PluginGeometry : Geometry
{
    public PluginGeometry()
    {
        ScanProperties<PluginGeometry>();
    }

    public IProperty<float> Side { get; } = Property.CreateAnimatable<float>();

    public partial class Resource
    {
        public override void ApplyTo(IGeometryContext context)
        {
            base.ApplyTo(context);
            context.MoveTo(new Point(0, 0));
            context.LineTo(new Point(Side, 0));
            context.LineTo(new Point(Side, Side));
            context.LineTo(new Point(0, Side));
            context.Close();
        }
    }
}

public sealed partial class PluginSegment : PathSegment
{
    public PluginSegment()
    {
        ScanProperties<PluginSegment>();
    }

    public IProperty<Point> To { get; } = Property.CreateAnimatable<Point>();

    public override IProperty<Point> GetEndPoint() => To;

    public partial class Resource
    {
        public override void ApplyTo(IGeometryContext context)
        {
            context.LineTo(To);
        }

        public override Point? GetEndPoint() => To;
    }
}

public sealed partial class PluginMesh : Mesh
{
    public PluginMesh()
    {
        ScanProperties<PluginMesh>();
    }

    public IProperty<float> Extent { get; } = Property.CreateAnimatable<float>();

    public partial class Resource
    {
        public override void ApplyTo(out Vertex3D[] vertices, out uint[] indices)
        {
            vertices =
            [
                new Vertex3D(Vector3.Zero, Vector3.UnitY, Vector2.Zero),
                new Vertex3D(new Vector3(Extent, 0, 0), Vector3.UnitY, Vector2.UnitX),
                new Vertex3D(new Vector3(0, 0, Extent), Vector3.UnitY, Vector2.UnitY),
            ];
            indices = [0, 1, 2];
        }
    }
}
