namespace Beutl.Media;

/// <summary>The CIE 1931 xy chromaticities of a display's red, green and blue primaries.</summary>
internal readonly record struct DisplayPrimaries(
    float RedX, float RedY, float GreenX, float GreenY, float BlueX, float BlueY)
{
    private const float D65X = 0.3127f;
    private const float D65Y = 0.3290f;

    /// <summary>The sRGB / ITU-R BT.709 primaries.</summary>
    public static DisplayPrimaries Srgb { get; } = new(0.64f, 0.33f, 0.30f, 0.60f, 0.15f, 0.06f);

    /// <summary>
    /// Whether the primaries form a triangle around the D65 white point, as a display's do. A zeroed or garbled report
    /// fails.
    /// </summary>
    public bool IsValid
    {
        get
        {
            // D65's barycentric coordinates in the triangle, all positive when it lies inside. NaNs fail every test.
            float det = (GreenY - BlueY) * (RedX - BlueX) + (BlueX - GreenX) * (RedY - BlueY);
            if (!(MathF.Abs(det) > 1e-6f))
                return false;

            float red = ((GreenY - BlueY) * (D65X - BlueX) + (BlueX - GreenX) * (D65Y - BlueY)) / det;
            float green = ((BlueY - RedY) * (D65X - BlueX) + (RedX - BlueX) * (D65Y - BlueY)) / det;
            float blue = 1f - red - green;
            return red > 0f && green > 0f && blue > 0f && RedY > 0f && GreenY > 0f && BlueY > 0f;
        }
    }
}
