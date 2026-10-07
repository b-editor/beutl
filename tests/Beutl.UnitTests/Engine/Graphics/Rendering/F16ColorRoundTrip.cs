using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

internal static class F16ColorRoundTrip
{
    /// <summary>RGB may cross two F16 quantizations; coverage alpha must remain bit-identical.</summary>
    public static bool Equal(Bitmap expected, Bitmap actual)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
            return false;
        ReadOnlySpan<ushort> a = expected.GetPixelSpan<ushort>();
        ReadOnlySpan<ushort> b = actual.GetPixelSpan<ushort>();
        for (int index = 0; index < a.Length; index++)
        {
            if (index % 4 == 3)
            {
                if (a[index] != b[index])
                    return false;
            }
            else if (Math.Abs(Ordered(a[index]) - Ordered(b[index])) > 2)
            {
                return false;
            }
        }

        return true;
    }

    private static int Ordered(ushort code) => (code & 0x8000) != 0 ? 0x8000 - (code & 0x7fff) : 0x8000 + code;
}
