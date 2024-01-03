using NUnit.Framework;

using static Beutl.Graphics.ColorMatrix;

namespace Beutl.Graphics.UnitTests;

public class ColorMatrixTests
{
    [Test]
    public void Multiply()
    {
        var left = new Matrix4x5(
            new(1, 2, 3, 4, 5),
            new(6, 7, 8, 9, 10),
            new(11, 12, 13, 14, 15),
            new(16, 17, 18, 19, 20));
        var right = new Matrix4x5(
            new(1, 0, 0, 0, 0),
            new(0, 1, 0, 0, 0),
            new(0, 0, 1, 0, 0),
            new(0, 0, 0, 1, 0));

        Matrix4x5 result = left * right;

        Assert.That(result, Is.EqualTo(left));
    }
}
