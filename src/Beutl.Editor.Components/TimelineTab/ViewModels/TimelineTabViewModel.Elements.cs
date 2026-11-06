using Avalonia;
using Beutl.Collections.Pooled;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class TimelineTabViewModel
{
    // A view model holds dozens of subscriptions and follows every zoom, so only the elements that were shown, selected
    // or asked for have one. It stays until its element leaves the scene, since callers keep hold of it.
    private readonly Dictionary<Element, ElementViewModel> _elementsByModel = [];

    public IReadOnlyCollection<ElementViewModel> RealizedElements => _elementsByModel.Values;

    internal event Action<ElementViewModel>? ElementRealized;

    internal event Action<ElementViewModel>? ElementReleased;

    // The element's view model, created if it has none yet. An element outside the scene has none.
    public ElementViewModel? GetViewModelFor(Element element)
    {
        if (_elementsByModel.TryGetValue(element, out ElementViewModel? viewModel)) return viewModel;
        if (_isDisposed || !ReferenceEquals(element.HierarchicalParent, Scene)) return null;

        viewModel = new ElementViewModel(element, this);
        _elementsByModel.Add(element, viewModel);
        ElementRealized?.Invoke(viewModel);
        return viewModel;
    }

    // For what only concerns an element that already has a view model, such as animating it on screen.
    internal ElementViewModel? FindViewModelFor(Element element) => _elementsByModel.GetValueOrDefault(element);

    // Gives the elements in area, in timeline pixels, that are wide enough to show their details their view models, and
    // returns whether it left out any narrower ones. Those are painted from their elements, as a view model holds dozens
    // of subscriptions: at 20,000 clips, making them all for a zoom out to the whole scene stopped the UI for over a second.
    internal bool RealizeElements(Rect area)
    {
        float scale = Options.Value.Scale;
        MediaTime start = area.Left.PixelToTime(scale);
        MediaTime end = area.Right.PixelToTime(scale);
        (int firstLayer, int lastLayer) = FindLayerRange(area.Top, area.Bottom);
        SceneElementIndex index = Scene.ElementIndex;
        using var found = new PooledList<Element>();
        for (int layer = firstLayer; layer <= lastLayer; layer++)
        {
            index.Query(start, end, found, layer);
        }

        bool leftOut = false;
        foreach (Element element in found)
        {
            if (_elementsByModel.ContainsKey(element)) continue;

            if (element.Length.Value.TimeToPixel(scale) >= ElementViewModel.MinimumThumbnailWidth)
                GetViewModelFor(element);
            else
                leftOut = true;
        }

        return leftOut;
    }

    // Where an element without a view model is on the timeline, in pixels.
    internal Rect GetElementBounds(Element element)
    {
        float scale = Options.Value.Scale;
        return new Rect(
            element.Start.Value.TimeToPixel(scale),
            CalculateLayerTop(element.ZIndex.Value),
            Math.Max(0, element.Length.Value.TimeToPixel(scale)),
            FrameNumberHelper.LayerHeight);
    }

    // Looping over this tolerates a view model being created along the way.
    private ElementViewModel[] RealizedElementsSnapshot() => [.. _elementsByModel.Values];

    private void TrackSceneElements()
    {
        foreach (Element element in Scene.Children)
        {
            RealizeIfItHasAPortal(element);
        }

        Scene.Children.TrackCollectionChanged(
                item =>
                {
                    _logger.LogDebug("Element added {Id}.", item.Id.Value);
                    AddLayerHeaders(item.ZIndex.Value + 1);
                    RealizeIfItHasAPortal(item);
                },
                item =>
                {
                    _logger.LogDebug("Element removed {Id}.", item.Id.Value);
                    ReleaseElement(item);
                },
                () =>
                {
                    _logger.LogDebug("All elements cleared.");
                    foreach (Element element in _elementsByModel.Keys.ToArray())
                    {
                        ReleaseElement(element);
                    }
                })
            .AddTo(_disposables);

        // An element with a view model reports these through it.
        void OnSceneEdited(object? sender, EventArgs e)
        {
            if (sender is not Element element) return;

            AddLayerHeaders(element.ZIndex.Value + 1);
            RealizeIfItHasAPortal(element);
        }

        Scene.Edited += OnSceneEdited;
        Disposable.Create(() => Scene.Edited -= OnSceneEdited).AddTo(_disposables);
    }

    // A portal's scope also shows on the rows below its element, where the search by row does not find the element.
    private void RealizeIfItHasAPortal(Element element)
    {
        if (_elementsByModel.ContainsKey(element)) return;

        foreach (EngineObject item in element.Objects)
        {
            if (item is PortalObject)
            {
                GetViewModelFor(element);
                return;
            }
        }
    }

    private void ReleaseElement(Element element)
    {
        if (!_elementsByModel.Remove(element, out ElementViewModel? viewModel)) return;

        SelectedElements.Remove(viewModel);
        ElementReleased?.Invoke(viewModel);
        viewModel.Dispose();
    }
}
