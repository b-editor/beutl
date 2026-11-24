using System.Numerics;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class PuppetDeformerConvergenceTests
{
    private static readonly Size s_frame = new(1920, 1080);
    private static readonly PixelSize s_mask = new(1920, 1080);

    // Two pins moved rigidly ask for the rigid motion of the whole picture, which is the ARAP optimum.
    [TestCase(30f)]
    [TestCase(90f)]
    public void TwoPinsTurnedAboutOneRotateThePictureRigidly(float degrees)
    {
        Point[] anchors = [new(0.1f, 0.5f), new(0.9f, 0.5f)];
        PuppetMesh mesh = PuppetMesh.Create([[new(0, 0), new(1920, 0), new(1920, 1080), new(0, 1080)]],
            s_frame, s_mask, anchors, out _)!;
        Vector2 pivot = PuppetMesh.ToLocal(s_frame, anchors[0]);
        var (sin, cos) = MathF.SinCos(degrees * MathF.PI / 180);
        Vector2 Rotate(Vector2 point)
        {
            Vector2 offset = point - pivot;
            return pivot + new Vector2(cos * offset.X - sin * offset.Y, sin * offset.X + cos * offset.Y);
        }

        Point[] destinations = [anchors[0], PuppetMesh.Normalize(s_frame, Rotate(PuppetMesh.ToLocal(s_frame, anchors[1])))];
        Vector2[] deformed = new PuppetDeformer(mesh).Deform(s_frame, destinations);

        double squares = 0;
        for (int vertex = 0; vertex < deformed.Length; vertex++)
            squares += Vector2.DistanceSquared(deformed[vertex], Rotate(mesh.Rest[vertex]));
        Assert.Multiple(() =>
        {
            Assert.That(Math.Sqrt(squares / deformed.Length), Is.LessThan(1), "RMS distance from the rigid rotation, in pixels");
            Assert.That(FoldedTriangles(mesh, deformed), Is.Zero);
            Assert.That(deformed[mesh.PinVertices[0]], Is.EqualTo(PuppetMesh.ToLocal(s_frame, destinations[0])));
            Assert.That(deformed[mesh.PinVertices[1]], Is.EqualTo(PuppetMesh.ToLocal(s_frame, destinations[1])));
        });
    }

    // A bar held at both ends with its middle pulled sideways, or with both ends turned about its middle.
    [TestCase(400f, 0f)]
    [TestCase(0f, 30f)]
    public void ThreePinsBendABarWithoutFolding(float pull, float degrees)
    {
        Point[] anchors = [new(200f / 1920, 0.5f), new(0.5f, 0.5f), new(1720f / 1920, 0.5f)];
        PuppetMesh mesh = PuppetMesh.Create([[new(160, 420), new(1760, 420), new(1760, 660), new(160, 660)]],
            s_frame, s_mask, anchors, out _)!;
        Assert.That(mesh.PinVertices, Has.All.GreaterThanOrEqualTo(0));
        Vector2 middle = PuppetMesh.ToLocal(s_frame, anchors[1]);
        Point Turn(Point anchor, float angle)
        {
            var (sin, cos) = MathF.SinCos(angle * MathF.PI / 180);
            Vector2 offset = PuppetMesh.ToLocal(s_frame, anchor) - middle;
            return PuppetMesh.Normalize(s_frame, middle + new Vector2(cos * offset.X - sin * offset.Y, sin * offset.X + cos * offset.Y));
        }

        Point[] destinations = [Turn(anchors[0], -degrees), new(anchors[1].X, anchors[1].Y + pull / 1080), Turn(anchors[2], degrees)];
        var deformer = new PuppetDeformer(mesh);
        Vector2[] deformed = deformer.Deform(s_frame, destinations);

        Assert.Multiple(() =>
        {
            Assert.That(deformed.All(vertex => float.IsFinite(vertex.X) && float.IsFinite(vertex.Y)), Is.True);
            Assert.That(FoldedTriangles(mesh, deformed), Is.Zero);
            for (int pin = 0; pin < anchors.Length; pin++)
                Assert.That(deformed[mesh.PinVertices[pin]], Is.EqualTo(PuppetMesh.ToLocal(s_frame, destinations[pin])));
        });

        // Each solve starts from the pins alone, so a pose solved in between changes nothing.
        deformer.Deform(s_frame, [anchors[0], new(0.5f, 0.2f), anchors[2]]);
        Assert.That(deformer.Deform(s_frame, destinations), Is.EqualTo(deformed));
    }

    private static int FoldedTriangles(PuppetMesh mesh, Vector2[] deformed)
    {
        int folded = 0;
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Vector2 a = deformed[mesh.Indices[i]];
            Vector2 b = deformed[mesh.Indices[i + 1]];
            Vector2 c = deformed[mesh.Indices[i + 2]];
            // The mesh winds every triangle the same way at rest.
            if ((double)(b.X - a.X) * (c.Y - a.Y) - (double)(b.Y - a.Y) * (c.X - a.X) <= 0)
                folded++;
        }
        return folded;
    }
}
