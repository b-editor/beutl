using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.UnitTests.Engine.Graphics.Transformation;

public class Rotation3DTransformCenterTests
{
    [TestCase(100f, -100f, 0f)]
    [TestCase(30f, 20f, -50f)]
    [TestCase(40f, 60f, 0f)]
    public void Rotation_keeps_its_center_in_place(float centerX, float centerY, float centerZ)
    {
        Matrix matrix = Rotation3DTransform.CreateMatrix(0, 0, 90, centerX, centerY, centerZ, 0);

        Point center = new Point(centerX, centerY) * matrix;

        Assert.Multiple(() =>
        {
            Assert.That(center.X, Is.EqualTo(centerX).Within(1e-3));
            Assert.That(center.Y, Is.EqualTo(centerY).Within(1e-3));
        });
    }

    [Test]
    public void Zero_and_nan_centers_rotate_about_the_origin()
    {
        Matrix origin = Rotation3DTransform.CreateMatrix(0, 0, 90, 0, 0, 0, 0);
        Matrix nan = Rotation3DTransform.CreateMatrix(0, 0, 90, float.NaN, 100, 0, 0);
        var point = new Point(10, 0);

        Assert.Multiple(() =>
        {
            Assert.That((point * origin).Y, Is.EqualTo(10).Within(1e-3));
            Assert.That(point * nan, Is.EqualTo(point * origin));
        });
    }
}
