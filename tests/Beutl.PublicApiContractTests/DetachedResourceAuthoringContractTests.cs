using System.Numerics;
using System.Reflection;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Meshes;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

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
    public void ADetachedResource_ReportsNoBackingObject()
    {
        using var detached = new PluginGeometry.Resource { Side = 24 };

        EngineObject? original = detached.GetOriginal();

        Assert.That(original, Is.Null);
    }

    [Test]
    public void TheBackingObjectAccessor_IsDeclaredNullable()
    {
        var context = new NullabilityInfoContext();
        MethodInfo baseAccessor = typeof(EngineObject.Resource)
            .GetMethod(nameof(EngineObject.Resource.GetOriginal), Type.EmptyTypes)!;
        MethodInfo generatedAccessor = typeof(PluginGeometry.Resource)
            .GetMethod(nameof(PluginGeometry.Resource.GetOriginal), Type.EmptyTypes)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                context.Create(baseAccessor.ReturnParameter).ReadState,
                Is.EqualTo(NullabilityState.Nullable));
            Assert.That(
                context.Create(generatedAccessor.ReturnParameter).ReadState,
                Is.EqualTo(NullabilityState.Nullable),
                "The generated typed accessor must carry the same admission as the one it hides.");
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
    public void AResourcePropertySetter_ReplacesWithoutDisposingThePreviousValue()
    {
        using var resource = new PluginObjectDefaultOwner.Resource();
        using var initial = new PluginObjectDefault.Resource();
        using var replacement = new PluginObjectDefault.Resource();
        resource.Child = initial;

        resource.Child = replacement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(initial.IsDisposed, Is.False,
                "a resource property setter is a plain assignment and must not dispose the displaced value");
            Assert.That(replacement.IsDisposed, Is.False);
        }

        resource.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(initial.IsDisposed, Is.False);
            Assert.That(replacement.IsDisposed, Is.True,
                "disposing the owner must still release the resource currently stored in the property");
        }
    }

    [Test]
    public void MutatingADetachedGeometry_RebuildsItsCachedPathOnceTheAuthorBumpsTheVersion()
    {
        using var detached = new PluginGeometry.Resource { Side = 10 };
        Rect beforeMutation = detached.Bounds;

        detached.Side = 40;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(detached.Side, Is.EqualTo(40), "the setter still stores what it was given");
            Assert.That(detached.Bounds, Is.EqualTo(beforeMutation),
                "it moved no version, so the path built before the assignment still stands");
        }

        detached.Version++;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(detached.Bounds, Is.EqualTo(new Rect(0, 0, 40, 40)));
            Assert.That(detached.FillContains(new Point(30, 30)), Is.True);
        }
    }

    [Test]
    public void MutatingADetachedGeometry_RebuildsItsCachedStrokePathOnceTheAuthorBumpsTheVersion()
    {
        using var detached = new PluginGeometry.Resource { Side = 10 };
        using var pen = DetachedPen(thickness: 4);
        Rect beforeMutation = detached.GetRenderBounds(pen);

        detached.Side = 100;

        Assert.That(detached.GetRenderBounds(pen), Is.EqualTo(beforeMutation),
            "the stroke path keys on the same version the setter left where it was");

        detached.Version++;

        Assert.That(detached.GetRenderBounds(pen), Is.EqualTo(new Rect(-2, -2, 104, 104)));
    }

    [Test]
    public void MutatingADetachedMesh_RegeneratesItsGeometryOnceTheAuthorBumpsTheVersion()
    {
        using var detached = new PluginMesh.Resource { Extent = 3 };
        _ = detached.GetVertices();

        detached.Extent = 9;

        Assert.That(detached.GetBoundingBox().Max.X, Is.EqualTo(3),
            "the mesh cache keys on the version too, so it still serves the vertices it generated");

        detached.Version++;

        Assert.That(detached.GetBoundingBox().Max.X, Is.EqualTo(9));
    }

    [Test]
    public void AddingAFigureToADetachedPathGeometry_RebuildsItsCachedPathOnceTheAuthorBumpsTheVersion()
    {
        using var detached = new PathGeometry.Resource
        {
            Figures =
            {
                new PathFigure.Resource
                {
                    StartPoint = new Point(0, 0),
                    Segments = { new PluginSegment.Resource { To = new Point(10, 10) } },
                },
            },
        };
        Rect beforeAdding = detached.Bounds;

        detached.Figures.Add(new PathFigure.Resource
        {
            StartPoint = new Point(0, 0),
            Segments = { new PluginSegment.Resource { To = new Point(40, 15) } },
        });

        Assert.That(detached.Bounds, Is.EqualTo(beforeAdding),
            "adding to the list runs no setter, so nothing about the resource has changed yet");

        detached.Version++;

        Assert.That(detached.Bounds, Is.EqualTo(new Rect(0, 0, 40, 15)),
            "the author's bump is what invalidates the path, and the rebuild reads the added figure");
    }

    [Test]
    public void MutatingAFigureOfADetachedPathGeometry_RebuildsItsCachedPathOnceTheAuthorBumpsTheVersion()
    {
        using var figure = new PathFigure.Resource
        {
            StartPoint = new Point(0, 0),
            Segments = { new PluginSegment.Resource { To = new Point(10, 10) } },
        };
        using var detached = new PathGeometry.Resource { Figures = { figure } };
        Rect beforeMutation = detached.Bounds;

        figure.StartPoint = new Point(40, 15);

        Assert.That(detached.Bounds, Is.EqualTo(beforeMutation),
            "assigning a property on the child moved no version, so the parent still answers as before");

        detached.Version++;

        Assert.That(detached.Bounds, Is.EqualTo(new Rect(10, 10, 30, 5)));
    }

    [Test]
    public void MutatingASegmentOfADetachedPathGeometry_RebuildsItsCachedPathOnceTheAuthorBumpsTheVersion()
    {
        using var segment = new PluginSegment.Resource { To = new Point(10, 10) };
        using var detached = new PathGeometry.Resource
        {
            Figures =
            {
                new PathFigure.Resource { StartPoint = new Point(0, 0), Segments = { segment } },
            },
        };
        Rect beforeMutation = detached.Bounds;

        segment.To = new Point(40, 15);

        Assert.That(detached.Bounds, Is.EqualTo(beforeMutation),
            "nothing between the segment and the geometry moved the geometry's own version");

        detached.Version++;

        Assert.That(detached.Bounds, Is.EqualTo(new Rect(0, 0, 40, 15)));
    }

    [Test]
    public void TogglingADetachedResource_LeavesACaptureCurrentUntilTheAuthorBumpsTheVersion()
    {
        using var detached = new Blur.Resource();
        (Blur.Resource Resource, int Version)? whileDisabled = detached.Capture();

        detached.IsEnabled = true;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(detached.IsEnabled, Is.True, "the setter still stores what it was given");
            Assert.That(detached.Compare(whileDisabled), Is.True,
                "enabling it moved no version, so what was recorded while it was disabled still matches");
        }

        detached.IsEnabled = false;

        Assert.That(detached.Compare(whileDisabled), Is.True,
            "disabling it again moves no version either - neither direction invalidates on its own");

        detached.Version++;

        Assert.That(detached.Compare(whileDisabled), Is.False,
            "the author's bump is what tells a recording the enabled state it was taken under is stale");
    }

    [Test]
    public void MutatingAGeneratedPropertyOfADetachedResource_LeavesACaptureCurrentUntilTheAuthorBumps()
    {
        using var detached = new Blur.Resource { Sigma = new Size(4, 4) };
        (Blur.Resource Resource, int Version)? beforeMutation = detached.Capture();

        detached.Sigma = new Size(12, 12);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(detached.Sigma, Is.EqualTo(new Size(12, 12)));
            Assert.That(detached.Compare(beforeMutation), Is.True,
                "a generated setter stores the value and moves no version");
        }

        detached.Version++;

        Assert.That(detached.Compare(beforeMutation), Is.False);
    }

    [Test]
    public void BuildingAResourceFromAnEnabledObject_LeavesItsVersionUnmoved()
    {
        var blur = new Blur();

        using var attached = blur.ToResource(CompositionContext.Default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(attached.IsEnabled, Is.True);
            Assert.That(attached.Version, Is.Zero);
        }
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
