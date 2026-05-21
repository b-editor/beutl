using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Reorders layers by shifting <see cref="Element.ZIndex"/> values and
/// committing a single history entry. <see cref="PlanMove"/> enumerates the
/// elements between the old and new layer so the View can drive matching
/// animations; the View is responsible for writing the new <see cref="Element.ZIndex"/>
/// values (the same writes drive the animation), then <see cref="CommitMove"/>
/// closes the transaction with a single MoveLayer entry.
/// </summary>
public interface ILayerMoveService
{
    LayerMovePlan PlanMove(
        Scene scene,
        int oldLayer,
        int newLayer,
        IReadOnlyList<Element> directElements);

    void CommitMove(LayerMovePlan plan);
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
