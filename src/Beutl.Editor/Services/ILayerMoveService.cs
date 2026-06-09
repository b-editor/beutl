using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Reorders layers: elements at <c>oldLayer</c> move to <c>newLayer</c> and the
/// layers between shift by one. <see cref="ApplyMove"/> owns the
/// <see cref="Element.ZIndex"/> writes (direct + shifted sets) and commits one
/// <c>MoveLayer</c> entry, returning a <see cref="LayerMovePlan"/> of affected
/// elements so the View can drive matching animations afterward.
/// </summary>
public interface ILayerMoveService
{
    LayerMovePlan ApplyMove(
        Scene scene,
        int oldLayer,
        int newLayer,
        IReadOnlyList<Element> directElements);
}

public sealed record LayerMovePlan(
    int Old,
    int New,
    IReadOnlyList<Element> DirectElements,
    IReadOnlyList<Element> ShiftedElements)
{
    public static readonly LayerMovePlan Noop = new(0, 0, [], []);

    public bool IsNoop => Old == New;
}
