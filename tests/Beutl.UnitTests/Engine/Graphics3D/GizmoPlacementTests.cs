using System.Numerics;

using Beutl.Composition;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Primitives;

namespace Beutl.UnitTests.Engine.Graphics3D;

[TestFixture]
public class GizmoPlacementTests
{
    // A perspective projection scales by depth along the view direction, so two targets at the same depth
    // get the same gizmo size however far off-axis one of them is.
    [Test]
    public void WorldScale_DependsOnViewDepth_NotOnDistance()
    {
        var camera = (Camera3D.Resource)new PerspectiveCamera().ToResource(CompositionContext.Default);

        float onAxis = GizmoHitTester.GetWorldScale(camera, new Vector3(0, 0, 500), 16f / 9);
        float offAxis = GizmoHitTester.GetWorldScale(camera, new Vector3(1200, -600, 500), 16f / 9);

        Assert.That(offAxis, Is.EqualTo(onAxis).Within(1e-3f));
    }

    [Test]
    public void WorldPosition_IncludesTheGroupsATargetIsNestedIn()
    {
        var child = new Cube3D();
        child.Position.CurrentValue = new Vector3(10, 20, 30);
        var group = new Group3D();
        group.Position.CurrentValue = new Vector3(100, 0, 0);
        group.Children.Add(child);
        using var groupResource = (Group3D.Resource)group.ToResource(CompositionContext.Default);
        Object3D.Resource childResource = groupResource.GetChildResources().Single();

        Vector3 position = Renderer3D.GetWorldPosition([groupResource], childResource);

        Assert.That(position, Is.EqualTo(new Vector3(110, 20, 30)));
    }

    // A drag measured in world space is applied to the child's local Position through the parent's inverse.
    [Test]
    public void ParentWorldMatrix_IsTheGroupTransform_OrIdentityAtTheRoot()
    {
        var child = new Cube3D();
        var group = new Group3D();
        group.Scale.CurrentValue = new Vector3(2);
        group.Children.Add(child);
        using var groupResource = (Group3D.Resource)group.ToResource(CompositionContext.Default);
        Object3D.Resource childResource = groupResource.GetChildResources().Single();

        Matrix4x4 parent = Renderer3D.GetParentWorldMatrix([groupResource], childResource);
        Matrix4x4 root = Renderer3D.GetParentWorldMatrix([groupResource], groupResource);

        Assert.Multiple(() =>
        {
            Assert.That(Vector3.TransformNormal(new Vector3(10, 0, 0), parent), Is.EqualTo(new Vector3(20, 0, 0)));
            Assert.That(root, Is.EqualTo(Matrix4x4.Identity));
        });
    }
}
