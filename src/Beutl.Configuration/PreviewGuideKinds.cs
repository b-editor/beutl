namespace Beutl.Configuration;

/// <summary>The guides the preview draws over the frame. They are never rendered into a frame or an export.</summary>
[Flags]
public enum PreviewGuideKinds
{
    None = 0,

    /// <summary>The centered 90% of the frame, inside which titles stay legible.</summary>
    TitleSafe = 1 << 0,

    /// <summary>The centered 95% of the frame, inside which action stays on screen.</summary>
    ActionSafe = 1 << 1,

    RuleOfThirds = 1 << 2,

    CenterCross = 1 << 3,

    Diagonals = 1 << 4,

    /// <summary>A grid of <see cref="EditorConfig.PreviewGuideGridColumns"/> by <see cref="EditorConfig.PreviewGuideGridRows"/> cells.</summary>
    CustomGrid = 1 << 5,

    /// <summary>The frames of <see cref="EditorConfig.PreviewGuideAspectRatios"/>, each centered in the scene's frame.</summary>
    AspectRatio = 1 << 6,
}
