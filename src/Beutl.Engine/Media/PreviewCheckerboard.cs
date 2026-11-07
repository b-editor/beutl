namespace Beutl.Media;

/// <summary>
/// The checkerboard the editor's preview can show behind the frame in place of black, so that its transparent parts
/// stand apart from black ones (#4066).
/// </summary>
/// <remarks>
/// The SDR preview draws it with a brush and the HDR preview in its present shader, both from these values. Its squares
/// keep their size on screen whatever the zoom, and start at the frame's top left corner.
/// </remarks>
internal static class PreviewCheckerboard
{
    /// <summary>The side of a square on screen, in device-independent pixels.</summary>
    public const float SquareSize = 8f;

    /// <summary>The sRGB-encoded gray of the square at the frame's top left corner, and of every other square.</summary>
    public const float Light = 0.6f;

    /// <summary>The sRGB-encoded gray of the squares between the light ones.</summary>
    public const float Dark = 0.4f;

    /// <summary>The same grays for the present shader, which takes text.</summary>
    public const string ShaderSource = """
        const float checkerLight = 0.6;
        const float checkerDark = 0.4;
        """;
}
