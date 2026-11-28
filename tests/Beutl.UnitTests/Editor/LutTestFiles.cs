using System.Numerics;
using Beutl.Graphics;

namespace Beutl.UnitTests.Editor;

internal static class LutTestFiles
{
    public static CubeFile CreateCube(bool invert = false)
    {
        var data = new Vector3[8];
        for (int b = 0; b < 2; b++)
        {
            for (int g = 0; g < 2; g++)
            {
                for (int r = 0; r < 2; r++)
                {
                    data[r + g * 2 + b * 4] = invert
                        ? new Vector3(1 - r, 1 - g, 1 - b)
                        : new Vector3(r, g, b);
                }
            }
        }

        return new CubeFile
        {
            Title = invert ? "inverted" : "identity",
            Dimention = CubeFileDimension.ThreeDimension,
            Size = 2,
            Min = Vector3.Zero,
            Max = Vector3.One,
            Data = data
        };
    }

    public static CubeFile CreateOneDimensionalCube()
        => new()
        {
            Title = "identity-1d",
            Dimention = CubeFileDimension.OneDimension,
            Size = 2,
            Min = Vector3.Zero,
            Max = Vector3.One,
            Data = [Vector3.Zero, Vector3.One]
        };

    public static void WriteCube(string path, bool invert = false)
    {
        var lines = new List<string> { "LUT_3D_SIZE 2" };
        for (int b = 0; b < 2; b++)
        {
            for (int g = 0; g < 2; g++)
            {
                for (int r = 0; r < 2; r++)
                {
                    lines.Add(invert ? $"{1 - r} {1 - g} {1 - b}" : $"{r} {g} {b}");
                }
            }
        }

        File.WriteAllLines(path, lines);
    }
}
