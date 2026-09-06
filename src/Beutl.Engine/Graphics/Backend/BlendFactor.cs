namespace Beutl.Graphics.Backend;

/// <summary>
/// Specifies blend factors for color blending operations.
/// </summary>
public enum BlendFactor
{
    /// <summary>
    /// Factor is (0, 0, 0, 0).
    /// </summary>
    Zero = 0,

    /// <summary>
    /// Factor is (1, 1, 1, 1).
    /// </summary>
    One = 1,

    /// <summary>
    /// Factor is (Rs, Gs, Bs, As) - source color.
    /// </summary>
    SrcColor = 2,

    /// <summary>
    /// Factor is (1-Rs, 1-Gs, 1-Bs, 1-As) - one minus source color.
    /// </summary>
    OneMinusSrcColor = 3,

    /// <summary>
    /// Factor is (Rd, Gd, Bd, Ad) - destination color.
    /// </summary>
    DstColor = 4,

    /// <summary>
    /// Factor is (1-Rd, 1-Gd, 1-Bd, 1-Ad) - one minus destination color.
    /// </summary>
    OneMinusDstColor = 5,

    /// <summary>
    /// Factor is (As, As, As, As) - source alpha.
    /// </summary>
    SrcAlpha = 6,

    /// <summary>
    /// Factor is (1-As, 1-As, 1-As, 1-As) - one minus source alpha.
    /// </summary>
    OneMinusSrcAlpha = 7,

    /// <summary>
    /// Factor is (Ad, Ad, Ad, Ad) - destination alpha.
    /// </summary>
    DstAlpha = 8,

    /// <summary>
    /// Factor is (1-Ad, 1-Ad, 1-Ad, 1-Ad) - one minus destination alpha.
    /// </summary>
    OneMinusDstAlpha = 9
}
