using System.ComponentModel;
using Avalonia;
using Beutl.Editor.Components.Helpers;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class TimelineTabViewModel
{
    public int CalculateClickedLayer()
    {
        _logger.LogDebug("Calculating clicked layer from position {Position}.", ClickedPosition);
        return ToLayerNumber(ClickedPosition.Y);
    }

    private void AddLayerHeaders(int count)
    {
        _logger.LogDebug("Adding layer headers up to count {Count}.", count);

        // A new header's Height emits synchronously, reaching CalculateLayerTop and re-entering
        // here; fold that request into the running loop's bound rather than recursing.
        if (_addingLayerHeaders)
        {
            _pendingLayerHeaderCount = Math.Max(_pendingLayerHeaderCount, count);
            return;
        }

        _addingLayerHeaders = true;
        try
        {
            _pendingLayerHeaderCount = count;
            for (int next = NextLayerNumber(); next < _pendingLayerHeaderCount; next = NextLayerNumber())
            {
                LayerHeaders.Add(new LayerHeaderViewModel(next, this));
            }
        }
        finally
        {
            _addingLayerHeaders = false;
            _pendingLayerHeaderCount = 0;
        }

        if (Options.Value.MaxLayerCount != LayerHeaders.Count)
        {
            Options.Value = Options.Value with { MaxLayerCount = LayerHeaders.Count };

            _logger.LogDebug("The number of layers has been changed. ({Count})", count);
        }
    }

    // A header's Number is its row, the list index.
    private int NextLayerNumber() => LayerHeaders.Count;

    // Every header shows the TimelineLayer on its row, so all of them look their model up again whenever a model
    // moves to another row or is added or removed: a layer move, its undo and redo, or a model that a flag or a
    // color created or pruned.
    private IDisposable TrackLayerModels()
    {
        var tracked = new List<TimelineLayer>();

        void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TimelineLayer.ZIndex))
                ResyncLayerHeaderModels();
        }

        void Untrack()
        {
            foreach (TimelineLayer layer in tracked)
                layer.PropertyChanged -= OnLayerPropertyChanged;
            tracked.Clear();
        }

        IDisposable items = Scene.Layers.ForEachItem(
            layer =>
            {
                layer.PropertyChanged += OnLayerPropertyChanged;
                tracked.Add(layer);
                ResyncLayerHeaderModels();
            },
            layer =>
            {
                layer.PropertyChanged -= OnLayerPropertyChanged;
                tracked.Remove(layer);
                ResyncLayerHeaderModels();
            },
            Untrack);

        return Disposable.Create(() =>
        {
            items.Dispose();
            Untrack();
        });
    }

    private void ResyncLayerHeaderModels()
    {
        // One lookup per pass, since a move rewrites the ZIndex of every layer it shifts, one at a time. The first
        // model on a row wins, as in ResyncModel.
        var modelsByRow = new Dictionary<int, TimelineLayer>();
        foreach (TimelineLayer layer in Scene.Layers)
            modelsByRow.TryAdd(layer.ZIndex, layer);

        // A snapshot: a header's new model must not be able to change the list under this loop.
        foreach (LayerHeaderViewModel header in LayerHeaders.ToArray())
            header.SetModel(modelsByRow.GetValueOrDefault(header.Number));
    }

    private void TryApplyLayerCount(int count)
    {
        _logger.LogDebug("Trying to apply layer count {Count}.", count);
        if (LayerHeaders.Count > count)
        {
            for (int i = LayerHeaders.Count - 1; i >= count; i--)
            {
                LayerHeaderViewModel item = LayerHeaders[i];
                // A clipless row whose TimelineLayer still carries lock/mute/solo
                // keeps affecting the editor/compositor; removing its header would
                // leave no UI to see or clear those flags.
                if (item.ItemsCount.Value > 0 || HasFlaggedLayerModel(item.Number))
                    break;

                LayerHeaders.RemoveAt(i);
            }

            if (Options.Value.MaxLayerCount != LayerHeaders.Count)
            {
                Options.Value = Options.Value with { MaxLayerCount = LayerHeaders.Count };

                _logger.LogDebug("The number of layers has been changed. ({Count})", LayerHeaders.Count);
            }
        }
        else
        {
            AddLayerHeaders(count);
        }
    }

    private bool HasFlaggedLayerModel(int zIndex)
        => Scene.Layers.Any(l => l.ZIndex == zIndex
                                 && (l.IsLocked || l.IsAudioMuted || l.IsVideoMuted || l.IsSolo));

    public IObservable<double> GetTrackedLayerTopObservable(IObservable<int> layer)
    {
        return layer.Select(GetTrackedLayerTopObservable).Switch();
    }

    public IObservable<double> GetTrackedLayerTopObservable(int zIndex)
    {
        if (!_trackerCache.TryGetValue(zIndex, out TrackedLayerTopObservable? value))
        {
            value = new TrackedLayerTopObservable(zIndex, this);
            _trackerCache.Add(zIndex, value);
        }

        return value;
    }

    public double CalculateLayerTop(int layer)
    {
        AddLayerHeaders(layer + 1);

        // Reentrant calls from AddLayerHeaders see a partially grown list, so clamp rather than
        // index past the end.
        double sum = 0;
        int count = Math.Min(layer, LayerHeaders.Count);
        for (int i = 0; i < count; i++)
        {
            sum += LayerHeaders[i].Height.Value;
        }

        return sum;
    }

    public int ToLayerNumber(double pixel)
    {
        return FindLayerNumber(pixel, pixel);
    }

    // A clip's margin is hit-tested at the clip's vertical centre, while rows past the last
    // header are counted from its top.
    public int ToLayerNumber(Thickness thickness)
    {
        return FindLayerNumber(thickness.Top + (FrameNumberHelper.LayerHeight / 2), thickness.Top);
    }

    private int FindLayerNumber(double hitY, double overflowY)
    {
        double sum = 0;

        for (int i = 0; i < LayerHeaders.Count; i++)
        {
            LayerHeaderViewModel cur = LayerHeaders[i];
            if (sum <= hitY && hitY <= (sum += cur.Height.Value))
            {
                return i;
            }
        }

        double delta = overflowY - sum;
        int addCount = (int)Math.Ceiling(delta / FrameNumberHelper.LayerHeight);
        int zIndex = addCount + LayerHeaders.Count;
        AddLayerHeaders(zIndex + 1);

        return zIndex;
    }

    internal void RaiseLayerHeightChanged(LayerHeaderViewModel value)
    {
        _layerHeightChanged.OnNext(value);
    }

    private sealed class TrackedLayerTopObservable(int layerNum, TimelineTabViewModel timeline)
        : LightweightObservableBase<double>, IDisposable
    {
        private IDisposable? _disposable2;

        protected override void Deinitialize()
        {
            _disposable2?.Dispose();
            timeline._trackerCache.Remove(layerNum);
        }

        protected override void Initialize()
        {
            _disposable2 = timeline.LayerHeightChanged.Subscribe(OnLayerHeightChanged);
        }

        private void OnLayerHeightChanged(LayerHeaderViewModel obj)
        {
            if (obj.Number < layerNum)
            {
                PublishNext(timeline.CalculateLayerTop(layerNum));
            }
        }

        protected override void Subscribed(IObserver<double> observer, bool first)
        {
            observer.OnNext(timeline.CalculateLayerTop(layerNum));
        }

        public void Dispose()
        {
            PublishCompleted();
        }
    }
}
