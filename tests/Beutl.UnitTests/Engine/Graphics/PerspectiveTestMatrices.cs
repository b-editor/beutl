using Beutl.Graphics;

namespace Beutl.UnitTests.Engine.Graphics;

internal static class PerspectiveTestMatrices
{
    // The shape of transforms.rot3d.depth-0050 at editor scale: a layer centred in a 256x144 frame whose
    // Y rotation carries a perspective divisor, so part of it sits behind the camera plane.
    public static Matrix ComposeCenteredRotation(float width, float height, float rotationY, float depth)
    {
        float radians = MathF.PI * rotationY / 180f;
        var rotation = new Matrix(
            MathF.Cos(radians), 0, MathF.Sin(radians) / depth,
            0, 1, 0,
            0, 0, 1);
        return Matrix.CreateTranslation(-width / 2, -height / 2)
               * rotation
               * Matrix.CreateTranslation(128, 72);
    }
}
