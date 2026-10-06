using System.Collections.Specialized;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Models;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class TimelineTabViewModel
{
    // ClickedPositionから最後にクリックしたレイヤーを計算します。
    public int CalculateClickedLayer()
    {
        _logger.LogDebug("Calculating clicked layer from position {Position}.", ClickedPosition);
        return ToLayerNumber(ClickedPosition.Y);
    }

    // zindexまでLayerHeaderを追加します。
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

    // Headers can be renumbered by a layer move, so the next row follows the last header's
    // Number rather than the list index.
    private int NextLayerNumber()
        => LayerHeaders.Count == 0 ? 0 : LayerHeaders[^1].Number.Value + 1;

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
                if (item.ItemsCount.Value > 0 || HasFlaggedLayerModel(item.Number.Value))
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
        lock (_trackerCache)
        {
            if (!_trackerCache.TryGetValue(zIndex, out TrackedLayerTopObservable? value))
            {
                value = new TrackedLayerTopObservable(zIndex, this);
                _trackerCache.Add(zIndex, value);
            }

            return value;
        }
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
        double sum = 0;

        for (int i = 0; i < LayerHeaders.Count; i++)
        {
            LayerHeaderViewModel cur = LayerHeaders[i];
            if (sum <= pixel && pixel <= (sum += cur.Height.Value))
            {
                return i;
            }
        }

        double delta = pixel - sum;
        int addCount = (int)Math.Ceiling(delta / FrameNumberHelper.LayerHeight);
        int zIndex = addCount + LayerHeaders.Count;
        AddLayerHeaders(zIndex + 1);

        return zIndex;
    }

    public int ToLayerNumber(Thickness thickness)
    {
        double sum = 0;

        for (int i = 0; i < LayerHeaders.Count; i++)
        {
            LayerHeaderViewModel cur = LayerHeaders[i];
            double top = thickness.Top + (FrameNumberHelper.LayerHeight / 2);
            if (sum <= top && top <= (sum += cur.Height.Value))
            {
                return i;
            }
        }

        double delta = thickness.Top - sum;
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
        private IDisposable? _disposable1;
        private IDisposable? _disposable2;

        protected override void Deinitialize()
        {
            _disposable1?.Dispose();
            _disposable2?.Dispose();
            timeline._trackerCache.Remove(layerNum);
        }

        protected override void Initialize()
        {
            _disposable1 = timeline.LayerHeaders.CollectionChangedAsObservable()
                .Subscribe(OnCollectionChanged);

            _disposable2 = timeline.LayerHeightChanged.Subscribe(OnLayerHeightChanged);
        }

        private void OnLayerHeightChanged(LayerHeaderViewModel obj)
        {
            if (obj.Number.Value < layerNum)
            {
                PublishNext(timeline.CalculateLayerTop(layerNum));
            }
        }

        protected override void Subscribed(IObserver<double> observer, bool first)
        {
            observer.OnNext(timeline.CalculateLayerTop(layerNum));
        }

        private void OnCollectionChanged(NotifyCollectionChangedEventArgs obj)
        {
            if (obj.Action == NotifyCollectionChangedAction.Move)
            {
                if (layerNum != obj.OldStartingIndex
                    && ((layerNum > obj.OldStartingIndex && layerNum <= obj.NewStartingIndex)
                        || (layerNum < obj.OldStartingIndex && layerNum >= obj.NewStartingIndex)))
                {
                    PublishNext(timeline.CalculateLayerTop(layerNum));
                }
            }
        }

        public void Dispose()
        {
            PublishCompleted();
        }
    }
}
