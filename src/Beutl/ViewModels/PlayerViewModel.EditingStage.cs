using System.Reactive.Linq;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.NodeGraph;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    // The effect selected for editing whose handles sit on the picture it passes on (see IEditingStageProvider).
    private ReadOnlyReactivePropertySlim<IEditingStageProvider?> _editingStageEffect = null!;
    private volatile GraphEditingFrame? _graphEditingFrame;

    private sealed record GraphEditingFrame(FilterEffect Effect, MediaTime Time, Matrix? Transform);

    // The effect at whose stage the shown frame stops, the effects after it hidden; null when the frame shows them all.
    internal ReactivePropertySlim<IEditingStageProvider?> ShownEditingStage { get; } = new();

    private void InitializeEditingStage()
    {
        _editingStageEffect = _editorSelection.SelectedObject
            .CombineLatest(IsMoveMode, IsPlaying, PathEditor.IsVisible,
                static (selected, move, playing, path) => move && !playing && !path ? selected as IEditingStageProvider : null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        _editingStageEffect.Skip(1).Subscribe(_ =>
        {
            if (!IsPlaybackActive)
                QueueRender();
        }).DisposeWith(_disposables);
        ShownEditingStage.DisposeWith(_disposables);
    }

    // The effects acting on what the stage's effect gives. Null when none does, as the stage is then the whole frame.
    private static IReadOnlySet<EngineObject>? GetEditingStageDisabledEffects(IEditingStageProvider? stage)
        => stage is FilterEffect effect && FilterEffectOrder.After(effect) is { Count: > 0 } after ? after : null;

    private static EngineObject? FindEditingGraph(FilterEffect effect)
    {
        EngineObject? graph = null;
        for (IHierarchical? parent = effect.FindHierarchicalParent<GraphModel>(); parent is not null; parent = parent.HierarchicalParent)
        {
            if (parent is GraphModel { HierarchicalParent: EngineObject owner } && owner is NodeGraphFilterEffect or NodeGraphDrawable)
                graph = owner;
        }
        return graph;
    }

    internal Matrix? GetEditingGraphTransform(FilterEffect effect, MediaTime time)
    {
        if (FindEditingGraph(effect) is null) return Matrix.Identity;

        MediaTime displayedTime = time.RoundToFrame(GetFrameRate(), MidpointRounding.AwayFromZero);
        return _graphEditingFrame is { } frame && ReferenceEquals(frame.Effect, effect) && frame.Time == displayedTime
            ? frame.Transform
            : null;
    }
}
