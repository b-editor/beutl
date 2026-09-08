namespace Beutl.Graphics.Rendering;

internal interface IRenderFragmentHandleOwner
{
    void VerifyActive();

    void VerifyOwns(RenderFragmentReference reference);

    /// <summary>Reports what a hit test answered while the owning recording was being made.</summary>
    /// <remarks>
    /// The answer is the one thing <see cref="RenderFragmentHandle"/> exposes that
    /// <see cref="RenderFragmentReference.RecordingFingerprint"/> does not speak for: a hit-test contract
    /// keeps one identity while the state it reads moves, so an input can answer differently and digest the
    /// same. A consumer that forwards the hit test is answered for anyway, because what it publishes names
    /// its input and re-reads the live rule through it. A consumer that branches on the answer bakes the
    /// branch into the fragments it publishes, and those fragments say nothing about having consulted it -
    /// so the answer itself has to be part of what the recording is offered back over.
    /// </remarks>
    void NoteHitTestRead(RenderFragmentReference reference, Point point, bool concrete, bool result);
}
