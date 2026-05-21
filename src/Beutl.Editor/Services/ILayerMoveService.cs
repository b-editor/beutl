using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Reorders layers by shifting <see cref="Element.ZIndex"/> values and
/// committing a single history entry. The service produces a
/// <see cref="LayerMovePlan"/> describing the affected elements so the View
/// can drive ZIndex animations before <see cref="CommitMove"/> finalizes the
/// model state.
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
