using System.Collections.Specialized;
using System.Reactive;
using Avalonia;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

// Where an element's part of a boundary transition sits inside the element, in timeline pixels, which
// fractions of the transition's duration it covers, and the easing its curve follows. Only a part whose
// element sets its own side can be resized.
public sealed record TransitionPartLayout(
    bool IsVisible,
    Thickness Margin,
    double Width,
    double StartFraction,
    double EndFraction,
    Easing? Easing,
    string? ToolTip,
    bool IsResizable)
{
    public static readonly TransitionPartLayout Hidden =
        new(false, default, 0, 0, 1, null, null, false);
}

public sealed partial class ElementViewModel
{
    private readonly ReactivePropertySlim<TransitionPartLayout> _enterPart = new(TransitionPartLayout.Hidden);
    private readonly ReactivePropertySlim<TransitionPartLayout> _exitPart = new(TransitionPartLayout.Hidden);
    // While a side's duration is dragged, the duration it is being dragged to. Both elements of the boundary
    // hold it, since each draws its own half of the same ramp.
    private TransitionDurationOverride? _transitionPreview;

    public IReadOnlyReactiveProperty<TransitionPartLayout> EnterTransitionPart => _enterPart;

    public IReadOnlyReactiveProperty<TransitionPartLayout> ExitTransitionPart => _exitPart;

    private void InitializeTransition()
    {
        _enterPart.AddTo(_disposables);
        _exitPart.AddTo(_disposables);

        // A boundary also moves when the element across it changes, and every edit reaches Scene.Edited.
        Observable.FromEventPattern(h => Scene.Edited += h, h => Scene.Edited -= h)
            .Select(_ => Unit.Default)
            .Merge(Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                    h => Scene.Children.CollectionChanged += h,
                    h => Scene.Children.CollectionChanged -= h)
                .Select(_ => Unit.Default))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => UpdateTransitionParts())
            .AddTo(_disposables);

        Timeline.Scale
            .Subscribe(_ => UpdateTransitionParts())
            .AddTo(_disposables);
    }

    public static string GetTransitionName(Type transitionType)
    {
        return TypeDisplayHelpers.GetLocalizedName(transitionType);
    }

    // The type of the transition at edge, as the boundary blends; null when the edge has none.
    public Type? GetTransitionType(ElementEdge edge)
    {
        return GetTransition(edge)?.GetType();
    }

    public bool HasTransitionPartner(ElementEdge edge)
    {
        return ElementTransitionEdits.FindPartner(Model, edge) != null;
    }

    public void ApplyTransition(ElementEdge edge, Type transitionType)
    {
        if (!IsEditable.Value) return;

        Timeline.EditorContext.GetRequiredService<IElementAttributeService>().ApplyTransition(Model, edge, transitionType);
    }

    // Opens the transition at edge in the property tab, first adding the default one when the edge has
    // none and the element can be edited.
    public void OpenTransition(ElementEdge edge)
    {
        if (GetTransition(edge) == null)
        {
            ApplyTransition(edge, typeof(CrossDissolveTransition));
        }

        EditTransition(edge);
    }

    // Opens the transition at edge in the property tab, where its type and properties are edited: the
    // side that decides how the boundary blends, since the other side's settings other than its duration
    // draw nothing. That duration is dragged on the timeline.
    public void EditTransition(ElementEdge edge)
    {
        if (GetTransition(edge) is not { } transition) return;

        IEditorContext editorContext = Timeline.EditorContext;
        ObjectPropertyTabViewModel tab = editorContext.FindToolTab<ObjectPropertyTabViewModel>()
                                         ?? new ObjectPropertyTabViewModel(editorContext);
        tab.NavigateCore(transition, false, null);
        editorContext.OpenToolTab(tab);
    }

    // The side that decides how the boundary at edge blends, or this element's own side there when it
    // blends nothing (it is disabled or has no length), so it can still be edited or removed.
    private ClipTransition? GetTransition(ElementEdge edge)
    {
        return GetBoundary(edge)?.Transition ?? ElementTransitionEdits.GetTransition(Model, edge);
    }

    // The duration this element's own side adds at edge, or none when it sets no transition there.
    public TimeSpan? GetOwnTransitionDuration(ElementEdge edge)
    {
        return ElementTransitionEdits.GetTransition(Model, edge)?.Duration.CurrentValue;
    }

    // Redraws the boundary at the duration a drag would commit, on both of its elements, without touching
    // the document; null ends the preview.
    public void PreviewTransitionDuration(ElementEdge edge, TimeSpan? duration)
    {
        TransitionDurationOverride? preview =
            duration is { } value && ElementTransitionEdits.GetTransition(Model, edge) is { } transition
                ? new TransitionDurationOverride(transition, value)
                : null;
        SetTransitionPreview(preview);
        if (ElementTransitionEdits.FindPartner(Model, edge) is { } partner
            && Timeline.GetViewModelFor(partner) is { } partnerViewModel)
        {
            partnerViewModel.SetTransitionPreview(preview);
        }
    }

    public void CommitTransitionDuration(ElementEdge edge, TimeSpan duration)
    {
        PreviewTransitionDuration(edge, null);
        if (IsEditable.Value)
        {
            Timeline.EditorContext.GetRequiredService<IElementAttributeService>()
                .SetTransitionDuration(Model, edge, duration);
        }
    }

    private void SetTransitionPreview(TransitionDurationOverride? preview)
    {
        _transitionPreview = preview;
        UpdateTransitionParts();
    }

    private TransitionBoundary? GetBoundary(ElementEdge edge)
    {
        return edge == ElementEdge.Start
            ? ElementTransitions.GetBoundaryAtStart(Model, _transitionPreview)
            : ElementTransitions.GetBoundaryAtEnd(Model, _transitionPreview);
    }

    private void UpdateTransitionParts()
    {
        if (_isDisposed) return;

        if (!Timeline.MayTakePartInTransition(Model))
        {
            _enterPart.Value = TransitionPartLayout.Hidden;
            _exitPart.Value = TransitionPartLayout.Hidden;
            return;
        }

        float scale = Timeline.Options.Value.Scale;
        _enterPart.Value = CreatePart(ElementEdge.Start, scale);
        _exitPart.Value = CreatePart(ElementEdge.End, scale);
    }

    private TransitionPartLayout CreatePart(ElementEdge edge, float scale)
    {
        if (GetBoundary(edge) is not { } boundary) return TransitionPartLayout.Hidden;

        TimeRange region = boundary.Region;
        TimeRange range = Model.Range;
        TimeSpan partStart = region.Start > range.Start ? region.Start : range.Start;
        TimeSpan partEnd = region.End < range.End ? region.End : range.End;
        if (partEnd <= partStart) return TransitionPartLayout.Hidden;

        double regionTicks = region.Duration.Ticks;
        double startFraction = (partStart - region.Start).Ticks / regionTicks;
        double endFraction = (partEnd - region.Start).Ticks / regionTicks;
        ClipTransition transition = boundary.Transition;
        string toolTip = $"{GetTransitionName(transition.GetType())} "
                         + $"({region.Duration.TotalSeconds:0.##}s)";
        return new TransitionPartLayout(
            true,
            new Thickness((partStart - range.Start).TimeToPixel(scale), 0, 0, 0),
            (partEnd - partStart).TimeToPixel(scale),
            Math.Clamp(startFraction, 0, 1),
            Math.Clamp(endFraction, 0, 1),
            transition.Easing.CurrentValue,
            toolTip,
            ElementTransitionEdits.GetTransition(Model, edge) is { IsEnabled: true });
    }
}
