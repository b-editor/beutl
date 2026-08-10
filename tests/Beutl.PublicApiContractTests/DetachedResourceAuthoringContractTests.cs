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
            Assert.That(attached.GetOriginal(), Is.SameAs(geometry));
            Assert.That(attached.Bounds, Is.EqualTo(new Rect(0, 0, 10, 10)));
        }
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

    [Test]
    public void ADetachedResource_OwnsTheNestedResourceItHoldsAndItsReplacement()
    {
        var resource = new PluginObjectDefaultOwner.Resource();
        var initial = new PluginObjectDefault.Resource();
        resource.Child = initial;
        var replacement = new PluginObjectDefault.Resource();

        resource.Child = replacement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(initial.IsDisposed, Is.True,
                "replacing an owned object property must release the resource it displaced");
            Assert.That(replacement.IsDisposed, Is.False);
        }

        resource.Dispose();

        Assert.That(replacement.IsDisposed, Is.True,
            "a resource property setter transfers ownership of the replacement to its owner");
    }

    private static Pen.Resource DetachedPen(float thickness)
    {
        return new Pen.Resource
        {
            Brush = new SolidColorBrush.Resource { Color = Colors.Black },
            Thickness = thickness,
            MiterLimit = 10,
            TrimEnd = 100,
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

public sealed partial class PluginObjectDefault : EngineObject
{
}

public sealed partial class PluginObjectDefaultOwner : EngineObject
{
    public IProperty<PluginObjectDefault?> Child { get; } = Property.Create<PluginObjectDefault?>();
}
