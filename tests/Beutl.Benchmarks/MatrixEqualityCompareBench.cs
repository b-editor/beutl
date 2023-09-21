
using System.Runtime.CompilerServices;

using BenchmarkDotNet.Attributes;

using Beutl.Graphics;

// Equalのほうが早い
public class MatrixEqualityCompareBench
{
    [Benchmark]
    public unsafe void SequenceEqual()
    {
        Matrix left = Matrix.Identity;
        Matrix right = Matrix.Identity;
        for (int i = 0; i < 50000; i++)
        {
            var leftSpan = new Span<float>(Unsafe.AsPointer(ref Unsafe.AsRef(in left)), 9);
            var rightSpan = new Span<float>(Unsafe.AsPointer(ref Unsafe.AsRef(in right)), 9);
            _ = leftSpan.SequenceEqual(rightSpan);
        }
    }

    [Benchmark]
    public void Equal()
    {
        Matrix left = Matrix.Identity;
        Matrix right = Matrix.Identity;
        for (int i = 0; i < 50000; i++)
        {
            _ = left.M11 == right.M11 &&
                left.M12 == right.M12 &&
                left.M13 == right.M13 &&
                left.M21 == right.M21 &&
                left.M22 == right.M22 &&
                left.M23 == right.M23 &&
                left.M31 == right.M31 &&
                left.M32 == right.M32 &&
                left.M33 == right.M33;
        }
    }

}
