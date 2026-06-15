using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Utilities;
using NUnit.Framework.Legacy;

namespace Beutl.UnitTests.Engine;

public class TransformParserTests
{
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
