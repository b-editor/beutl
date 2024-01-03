using Beutl.Graphics.Transformation;
using Beutl.Utilities;

using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Beutl.Graphics.UnitTests;

public class TransformParser
{
    [Test]
    [TestCase("translate(10px)", 10f, 0f)]
    [TestCase("translate(10px, 10px)", 10f, 10f)]
    [TestCase("translate(0px, 10px)", 0f, 10f)]
    [TestCase("translate(10px, 0px)", 10f, 0f)]
    [TestCase("translateX(10px)", 10f, 0f)]
    [TestCase("translateY(10px)", 0f, 10f)]
    public void ParseTranslate(string data, float x, float y)
    {
        var transform = Transform.Parse(data) as TranslateTransform;

        ClassicAssert.NotNull(transform);
        ClassicAssert.AreEqual(x, transform!.X);
        ClassicAssert.AreEqual(y, transform.Y);
    }

    [Test]
    [TestCase("rotate(90deg)", 90f)]
    [TestCase("rotate(0.5turn)", 180f)]
    [TestCase("rotate(200grad)", 180f)]
    [TestCase("rotate(3.14159265rad)", 180f)]
    public void ParseRotate(string data, float angleDeg)
    {
        var transform = Transform.Parse(data) as RotationTransform;

        ClassicAssert.NotNull(transform);
        ClassicAssert.AreEqual(angleDeg, transform!.Rotation);
    }

    [Test]
    [TestCase("scale(10)", 10f, 10f)]
    [TestCase("scale(10, 10)", 10f, 10f)]
    [TestCase("scale(0, 10)", 0f, 10f)]
    [TestCase("scale(10, 0)", 10f, 0f)]
    [TestCase("scaleX(10)", 10f, 1f)]
    [TestCase("scaleY(10)", 1f, 10f)]
    [TestCase("scale(10%)", 0.1f, 0.1f)]
    [TestCase("scale(10%, 10%)", 0.1f, 0.1f)]
    [TestCase("scale(0, 10%)", 0f, 0.1f)]
    [TestCase("scale(10%, 0)", 0.1f, 0f)]
    [TestCase("scaleX(10%)", 0.1f, 1f)]
    [TestCase("scaleY(10%)", 1f, 0.1f)]
    public void ParseScale(string data, float x, float y)
    {
        var transform = Transform.Parse(data) as ScaleTransform;

        ClassicAssert.NotNull(transform);
        ClassicAssert.AreEqual(x, transform!.ScaleX / 100);
        ClassicAssert.AreEqual(y, transform.ScaleY / 100);
    }

    [Test]
    [TestCase("skew(90deg)", 90f, 0f)]
    [TestCase("skew(0.5turn)", 180f, 0f)]
    [TestCase("skew(200grad)", 180f, 0f)]
    [TestCase("skew(3.14159265rad)", 180f, 0f)]
    [TestCase("skewX(90deg)", 90f, 0f)]
    [TestCase("skewX(0.5turn)", 180f, 0f)]
    [TestCase("skewX(200grad)", 180f, 0f)]
    [TestCase("skewX(3.14159265rad)", 180f, 0f)]
    [TestCase("skew(0, 90deg)", 0f, 90f)]
    [TestCase("skew(0, 0.5turn)", 0f, 180f)]
    [TestCase("skew(0, 200grad)", 0f, 180f)]
    [TestCase("skew(0, 3.14159265rad)", 0f, 180f)]
    [TestCase("skewY(90deg)", 0f, 90f)]
    [TestCase("skewY(0.5turn)", 0f, 180f)]
    [TestCase("skewY(200grad)", 0f, 180f)]
    [TestCase("skewY(3.14159265rad)", 0f, 180f)]
    [TestCase("skew(90deg, 90deg)", 90f, 90f)]
    [TestCase("skew(0.5turn, 0.5turn)", 180f, 180f)]
    [TestCase("skew(200grad, 200grad)", 180f, 180f)]
    [TestCase("skew(3.14159265rad, 3.14159265rad)", 180f, 180f)]
    public void ParseSkew(string data, float x, float y)
    {
        var transform = Transform.Parse(data) as SkewTransform;

        ClassicAssert.NotNull(transform);
        ClassicAssert.AreEqual(x, transform!.SkewX);
        ClassicAssert.AreEqual(y, transform!.SkewY);
    }

    [Test]
    public void ParseFuncs()
    {
        string data = "scale(1,2) translate(3px,4px) rotate(5deg) skew(6deg,7deg)";
        Matrix expected = Matrix.Identity
            .Prepend(Matrix.CreateScale(1, 2))
            .Prepend(Matrix.CreateTranslation(3, 4))
            .Prepend(Matrix.CreateRotation(MathUtilities.Deg2Rad(5))
            .Prepend(Matrix.CreateSkew(MathUtilities.Deg2Rad(6), MathUtilities.Deg2Rad(7))));
        Matrix actual = Matrix.Parse(data);

        ClassicAssert.AreEqual(expected, actual);
    }

    [Test]
    [TestCase("matrix(1,2,3,4,5,6)", 1, 2, 0, 3, 4, 0, 5, 6, 1)]
    [TestCase("matrix(1,2,3,4,5,6,7,8,9)", 1, 2, 3, 4, 5, 6, 7, 8, 9)]
    public void ParseMatrix(string data,
        float m11, float m12, float m13,
        float m21, float m22, float m23,
        float m31, float m32, float m33)
    {
        var expected = new Matrix(m11, m12, m13, m21, m22, m23, m31, m32, m33);
        Matrix actual = Matrix.Parse(data);

        ClassicAssert.AreEqual(expected, actual);
    }
}
