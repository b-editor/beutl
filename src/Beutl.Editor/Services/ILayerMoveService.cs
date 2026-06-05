using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Reorders layers: the elements at <c>oldLayer</c> move to <c>newLayer</c>
/// and every layer between them shifts by one. <see cref="ApplyMove"/>
/// owns the model writes (<see cref="Element.ZIndex"/> on the direct and
/// shifted sets) and commits a single <c>MoveLayer</c> history entry.
/// The returned <see cref="LayerMovePlan"/> lists the affected elements so
/// the View can drive matching animations after the call.
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
