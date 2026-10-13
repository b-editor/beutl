namespace Beutl.Graphics.Rendering.Requests;

/// <summary>One fragment of a reusable recording, with its inputs stored as slots rather than references.</summary>
internal readonly struct ReplayedRenderFragment(
    RenderFragmentReference template,
    object origin,
    int[] inputSlots)
{
    public RenderFragmentReference Template { get; } = template;

    public object Origin { get; } = origin;

    /// <summary>
    /// Where each input came from: a non-negative slot indexes an earlier fragment of this recording, a
    /// negative slot <c>s</c> indexes declared input <c>-s - 1</c>.
    /// </summary>
    public int[] InputSlots { get; } = inputSlots;
}
