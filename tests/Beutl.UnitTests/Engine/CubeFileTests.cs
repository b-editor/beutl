using System.Numerics;
using Beutl.Graphics;

namespace Beutl.UnitTests.Engine;

public class CubeFileTests
{
    private static CubeFile MakeIdentity1D(int size = 4)
    {
        var data = new Vector3[size];
        for (int i = 0; i < size; i++)
        {
            float v = i / (float)(size - 1);
            data[i] = new Vector3(v, v, v);
        }

        return new CubeFile
        {
            Dimention = CubeFileDimension.OneDimension,
            Size = size,
            Min = Vector3.Zero,
            Max = Vector3.One,
            Data = data,
        };
    }

    [Test]
    public void Equals_DifferentInstance_False()
    {
        // CubeFile.Equals は ReferenceEquals 比較なので、内容が同じでも別インスタンスは不一致
        CubeFile a = MakeIdentity1D();
        CubeFile b = MakeIdentity1D();
        Assert.That(a.Equals(b), Is.False);
    }

    [Test]
    public void Equals_NullOrOtherObject_False()
    {
        CubeFile cube = MakeIdentity1D();
        CubeFile? nullCube = null;
        object? otherObject = "not a cube";
        Assert.Multiple(() =>
        {
            Assert.That(cube.Equals(nullCube), Is.False);
            Assert.That(cube.Equals(otherObject), Is.False);
        });
    }

    [Test]
    public void FromStream_Parses1DCubeHeader()
    {
        const string content =
            """
            TITLE "Test1D"
            LUT_1D_SIZE 4
            DOMAIN_MIN 0 0 0
            DOMAIN_MAX 1 1 1
            0.0 0.0 0.0
            0.33 0.33 0.33
            0.66 0.66 0.66
            1.0 1.0 1.0
            """;
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        CubeFile cube = CubeFile.FromStream(ms);

        Assert.Multiple(() =>
        {
            Assert.That(cube.Dimention, Is.EqualTo(CubeFileDimension.OneDimension));
            Assert.That(cube.Size, Is.EqualTo(4));
            Assert.That(cube.Data, Has.Length.EqualTo(4));
            Assert.That(cube.Min, Is.EqualTo(Vector3.Zero));
            Assert.That(cube.Max, Is.EqualTo(new Vector3(1f, 1f, 1f)));
        });
    }
}
