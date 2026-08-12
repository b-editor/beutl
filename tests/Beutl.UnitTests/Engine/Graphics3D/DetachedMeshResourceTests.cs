using Beutl.Composition;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Meshes;
using Beutl.Graphics3D.Models;

namespace Beutl.UnitTests.Engine.Graphics3D;

/// <summary>
/// <c>Mesh</c> had the same shape as <c>Geometry</c>: <c>EnsureCached</c> dispatched through the backing
/// engine object, so a publicly constructed resource threw. Generation now dispatches on the resource.
/// </summary>
[TestFixture]
public sealed class DetachedMeshResourceTests
{
    [Test]
    public void ADetachedCube_GeneratesTheSameMeshAsItsAttachedCounterpart()
    {
        using var detached = new CubeMesh.Resource { Width = 2, Height = 3, Depth = 4 };
        using Mesh.Resource attached = new CubeMesh
        {
            Width = { CurrentValue = 2 },
            Height = { CurrentValue = 3 },
            Depth = { CurrentValue = 4 },
        }.ToResource(CompositionContext.Default);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(detached.GetVertices().ToArray(), Is.EqualTo(attached.GetVertices().ToArray()).AsCollection);
            Assert.That(detached.GetIndices().ToArray(), Is.EqualTo(attached.GetIndices().ToArray()).AsCollection);
            Assert.That(detached.GetBoundingBox().Min, Is.EqualTo(attached.GetBoundingBox().Min));
            Assert.That(detached.GetBoundingBox().Max, Is.EqualTo(attached.GetBoundingBox().Max));
        }
    }

    [Test]
    public void EveryDetachedBuiltInMesh_ReportsItsCounts()
    {
        using var cube = new CubeMesh.Resource { Width = 1, Height = 1, Depth = 1 };
        using var plane = new PlaneMesh.Resource { Width = 2, Height = 2, WidthSegments = 2, HeightSegments = 3 };
        using var sphere = new SphereMesh.Resource { Radius = 1, Segments = 8, Rings = 4 };
        using var model = new ModelMesh.Resource
        {
            Vertices = [.. new CubeMesh.Resource { Width = 1, Height = 1, Depth = 1 }.GetVertices()],
            Indices = [.. new CubeMesh.Resource { Width = 1, Height = 1, Depth = 1 }.GetIndices()],
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cube.VertexCount, Is.EqualTo(24));
            Assert.That(plane.VertexCount, Is.EqualTo(12));
            Assert.That(sphere.VertexCount, Is.EqualTo(45));
            Assert.That(model.VertexCount, Is.EqualTo(24));
            Assert.That(model.IndexCount, Is.EqualTo(cube.IndexCount));
        }
    }

    /// <summary>
    /// <c>MeshBufferUploadHelper</c> and <c>TransparentPass</c> clear <c>BuffersDirty</c> once they have
    /// uploaded the current vertices, so a regenerated mesh that leaves it clear keeps the GPU on the old
    /// buffers.
    /// </summary>
    [Test]
    public void RegeneratingAMesh_MarksItsGpuBuffersDirtyAgain()
    {
        using var mesh = new CubeMesh.Resource { Width = 1, Height = 1, Depth = 1 };
        _ = mesh.GetVertices();
        mesh.BuffersDirty = false;

        mesh.Width = 4;
        mesh.Version++;
        ReadOnlySpan<Vertex3D> regenerated = mesh.GetVertices();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mesh.GetBoundingBox().Max.X, Is.EqualTo(2));
            Assert.That(regenerated.Length, Is.EqualTo(24));
            Assert.That(mesh.BuffersDirty, Is.True);
        }
    }

    [Test]
    public void AMeshServedFromItsCache_LeavesTheGpuBufferFlagAlone()
    {
        using var mesh = new CubeMesh.Resource { Width = 1, Height = 1, Depth = 1 };
        _ = mesh.GetVertices();
        mesh.BuffersDirty = false;

        _ = mesh.GetVertices();

        Assert.That(mesh.BuffersDirty, Is.False);
    }
}
