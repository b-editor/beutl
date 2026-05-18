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
            Title = "identity-1d",
            Dimention = CubeFileDimension.OneDimension,
            Size = size,
            Min = Vector3.Zero,
            Max = Vector3.One,
            Data = data,
        };
    }

    private static CubeFile MakeIdentity3D(int size = 2)
    {
        var data = new Vector3[size * size * size];
        for (int z = 0; z < size; z++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = x + y * size + z * size * size;
                    data[index] = new Vector3(
                        x / (float)(size - 1),
                        y / (float)(size - 1),
                        z / (float)(size - 1)
                    );
                }
            }
        }

        return new CubeFile
        {
            Title = "identity-3d",
            Dimention = CubeFileDimension.ThreeDimension,
            Size = size,
            Min = Vector3.Zero,
            Max = Vector3.One,
            Data = data,
        };
    }

    [Test]
    public void Equals_SameInstance_True()
    {
        CubeFile cube = MakeIdentity1D();
        Assert.That(cube.Equals(cube), Is.True);
        Assert.That(cube.Equals((object)cube), Is.True);
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
    public void GetHashCode_SameInstance_StableAcrossCalls()
    {
        CubeFile cube = MakeIdentity1D();
        Assert.That(cube.GetHashCode(), Is.EqualTo(cube.GetHashCode()));
    }

    [Test]
    public void ToLUT_OnNonOneDimension_Throws()
    {
        CubeFile cube = MakeIdentity3D();
        var r = new byte[256];
        var g = new byte[256];
        var b = new byte[256];
        Assert.Throws<InvalidOperationException>(() => cube.ToLUT(1f, r, g, b));
    }

    [Test]
    public void ToLUT_InvalidArrayLength_Throws()
    {
        CubeFile cube = MakeIdentity1D();
        var r = new byte[100];
        var g = new byte[256];
        var b = new byte[256];
        Assert.Throws<ArgumentException>(() => cube.ToLUT(1f, r, g, b));
    }

    [Test]
    public void ToLUT_IdentityWithFullStrength_ProducesIdentity()
    {
        CubeFile cube = MakeIdentity1D(size: 256);
        var r = new byte[256];
        var g = new byte[256];
        var b = new byte[256];

        cube.ToLUT(1f, r, g, b);

        // 完全恒等LUTかつstrength=1の場合、各チャネルは入力と等しくなる
        for (int i = 0; i < 256; i++)
        {
            Assert.That(r[i], Is.EqualTo((byte)i));
            Assert.That(g[i], Is.EqualTo((byte)i));
            Assert.That(b[i], Is.EqualTo((byte)i));
        }
    }

    [Test]
    public void ToLUT_StrengthZero_ReturnsLinearRamp()
    {
        // strength=0 のとき LUT 値の寄与 (v*255 * strength) はゼロになり、
        // パススルー項 add = i*(1-0) = i だけが残るため、出力はリニアランプ
        CubeFile cube = MakeIdentity1D(size: 256);
        var r = new byte[256];
        var g = new byte[256];
        var b = new byte[256];

        cube.ToLUT(0f, r, g, b);

        for (int i = 0; i < 256; i++)
        {
            Assert.That(r[i], Is.EqualTo((byte)i));
            Assert.That(g[i], Is.EqualTo((byte)i));
            Assert.That(b[i], Is.EqualTo((byte)i));
        }
    }

    [Test]
    public void TrilinearInterplate_Endpoints_ReturnCornerValues()
    {
        CubeFile cube = MakeIdentity3D(size: 2);

        Vector3 black = cube.TrilinearInterplate(0, 0, 0);
        Assert.Multiple(() =>
        {
            Assert.That(black.X, Is.EqualTo(0f));
            Assert.That(black.Y, Is.EqualTo(0f));
            Assert.That(black.Z, Is.EqualTo(0f));
        });
    }

    [Test]
    public void Properties_SetViaInitializer()
    {
        var data = new Vector3[8];
        var cube = new CubeFile
        {
            Title = "myLUT",
            Dimention = CubeFileDimension.ThreeDimension,
            Size = 2,
            Min = new Vector3(0.1f, 0.2f, 0.3f),
            Max = new Vector3(0.9f, 0.8f, 0.7f),
            Data = data,
        };

        Assert.Multiple(() =>
        {
            Assert.That(cube.Title, Is.EqualTo("myLUT"));
            Assert.That(cube.Dimention, Is.EqualTo(CubeFileDimension.ThreeDimension));
            Assert.That(cube.Size, Is.EqualTo(2));
            Assert.That(cube.Min, Is.EqualTo(new Vector3(0.1f, 0.2f, 0.3f)));
            Assert.That(cube.Max, Is.EqualTo(new Vector3(0.9f, 0.8f, 0.7f)));
            Assert.That(cube.Data, Is.SameAs(data));
        });
    }

    [Test]
    public void FromStream_Parses1DCubeHeader()
    {
        const string content = """
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
            Assert.That(cube.Title, Is.EqualTo("Test1D"));
            Assert.That(cube.Dimention, Is.EqualTo(CubeFileDimension.OneDimension));
            Assert.That(cube.Size, Is.EqualTo(4));
            Assert.That(cube.Data, Has.Length.EqualTo(4));
            Assert.That(cube.Min, Is.EqualTo(Vector3.Zero));
            Assert.That(cube.Max, Is.EqualTo(new Vector3(1f, 1f, 1f)));
        });
    }
}
