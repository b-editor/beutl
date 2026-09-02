namespace Beutl.Graphics.Rendering;

/// <summary>One hit test a recording read, and what it answered.</summary>
internal readonly record struct RecordedHitTestRead(
    RenderFragmentReference Reference,
    Point Point,
    bool Concrete,
    bool Result);
