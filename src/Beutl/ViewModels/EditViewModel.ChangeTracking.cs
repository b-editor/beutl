using System.ComponentModel;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Operations;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.Logging;
using Dispatcher = Avalonia.Threading.Dispatcher;

namespace Beutl.ViewModels;

public partial class EditViewModel
{
    private bool _autoSaveFailed;

    private void OnEditorConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is EditorConfig config)
        {
            if (e.PropertyName is nameof(EditorConfig.FrameCacheColorType) or nameof(EditorConfig.FrameCacheScale))
            {
                _logger.LogInformation("Updating FrameCacheManager options due to EditorConfig change.");
                FrameCacheManager.Value.Options = FrameCacheManager.Value.Options with
                {
                    ColorType = (FrameCacheColorType)config.FrameCacheColorType,
                    Scale = (FrameCacheScale)config.FrameCacheScale
                };
            }
            else if (e.PropertyName is nameof(EditorConfig.IsFrameCacheEnabled))
            {
                _logger.LogInformation("Updating FrameCacheManager IsEnabled due to EditorConfig change.");
                FrameCacheManager.Value.IsEnabled = config.IsFrameCacheEnabled;
                if (!config.IsFrameCacheEnabled)
                {
                    FrameCacheManager.Value.Clear();
                }
            }
            else if (e.PropertyName is nameof(EditorConfig.IsNodeCacheEnabled)
                     or nameof(EditorConfig.NodeCacheMaxPixels)
                     or nameof(EditorConfig.NodeCacheMinPixels))
            {
                _logger.LogInformation("Updating RenderNodeCacheHelper options due to EditorConfig change.");
                Renderer.Value.CacheOptions = RenderCacheOptions.CreateFromGlobalConfiguration();
            }
        }
    }

    private void OnProxyStoreChanged(object? sender, ProxyStoreChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // A store swap replaces every entry, and e.Source is not meaningful — clear the whole frame
        // cache and re-render rather than invalidating a single source's ranges.
        if (e.Kind == ProxyStoreChangeKind.Reset)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed || FrameCacheManager.Value.IsDisposed)
                    return;

                FrameCacheManager.Value.Clear();
                Player.QueuePreviewRender();
            });
            return;
        }

        if (e.Kind is not (ProxyStoreChangeKind.Registered
            or ProxyStoreChangeKind.StateChanged
            or ProxyStoreChangeKind.Deleted))
        {
            return;
        }

        // e.Source.AbsolutePath is already the fingerprint's normalized path.
        bool schedule;
        lock (_pendingProxyInvalidations)
        {
            _pendingProxyInvalidations.Add(e.Source.AbsolutePath);
            schedule = !_proxyInvalidationScheduled;
            _proxyInvalidationScheduled = true;
        }

        // A bulk generate (FR-008 / US2 AC4) fires one store event per proxy state change.
        // Coalesce the burst into a single UI-thread scene walk per tick instead of one
        // walk per event, so the timeline stays smooth while proxies are generated.
        if (schedule)
        {
            Dispatcher.UIThread.Post(FlushPendingProxyInvalidations);
        }
    }

    private void FlushPendingProxyInvalidations()
    {
        HashSet<string> changedSources;
        lock (_pendingProxyInvalidations)
        {
            _proxyInvalidationScheduled = false;
            // Disposal may have run between the Post and this callback; Scene is nulled and the frame
            // cache disposed by then, so drop the pending work and bail rather than touch them.
            if (_disposed || _pendingProxyInvalidations.Count == 0)
            {
                _pendingProxyInvalidations.Clear();
                return;
            }

            changedSources = new HashSet<string>(_pendingProxyInvalidations, StringComparer.Ordinal);
            _pendingProxyInvalidations.Clear();
        }

        // Invalidate only frames of clips that use a changed source, not the whole
        // timeline cache (FR-023; unrelated clips stay editable during a bulk generate).
        // FrameCacheManager only invalidates by frame range, so each changed source is
        // mapped to the ranges of the elements that reference it.
        FrameCacheManager cache = FrameCacheManager.Value;
        if (cache.IsDisposed)
        {
            return;
        }

        List<TimeRange> affectedRanges = [];
        foreach (Element element in Scene.Children)
        {
            if (ElementUsesAnySource(element, changedSources))
            {
                affectedRanges.Add(element.Range);
            }
        }

        if (affectedRanges.Count == 0)
        {
            return;
        }

        int rate = Player.GetFrameRate();
        cache.DeleteAndUpdateBlocks(affectedRanges
            .Select(range => FrameCacheRanges.ToFrameRange(range, rate)));

        // While paused, the shown bitmap is cloned into PlayerViewModel and does not observe the
        // deletion above, so re-render when the playhead sits in a changed range.
        TimeSpan playhead = Player.CurrentFrame.Value;
        foreach (TimeRange range in affectedRanges)
        {
            if (range.Start <= playhead && playhead < range.End)
            {
                Player.QueuePreviewRender();
                break;
            }
        }
    }

    private static bool ElementUsesAnySource(Element element, IReadOnlySet<string> changedSources)
    {
        // Cover every proxy-aware holder (SourceVideo, VideoSourceNode graph inputs, referenced
        // scenes, and their animated values) so cached frames of a graph/referenced-scene clip are
        // invalidated too, not just those of a top-level SourceVideo's current value.
        foreach (VideoSource source in ProxySourceEnumerator.EnumerateVideoSources(element))
        {
            if (source is not { HasUri: true } || source.Uri is not { IsFile: true } uri)
            {
                continue;
            }

            // Resolve the element's path the same way store change events are keyed (symlink target
            // resolved before folding), so a source referenced via a symlink still matches.
            string elementPath = ProxyFingerprint.ResolveComparableKey(uri.LocalPath);
            if (changedSources.Contains(elementPath))
            {
                return true;
            }
        }

        return false;
    }

    private void OnChangeOperations(IList<ChangeOperation> list)
    {
        if (list.Count == 0)
        {
            return;
        }

        // 影響を受けるタイムレンジを取得
        if (MediaReferencesChanged())
        {
            ScheduleMediaFingerprints();
            NotifyMissingMedia();
        }
        List<TimeRange> affectedRanges = GetAffectedTimeRanges(list);

        // フレームキャッシュを更新
        if (affectedRanges.Count > 0)
        {
            Task.Run(() =>
            {
                int rate = Player.GetFrameRate();
                FrameCacheManager.Value.DeleteAndUpdateBlocks(affectedRanges
                    .Select(item => FrameCacheRanges.ToFrameRange(item, rate)));
            });
        }

        // 自動保存
        if (!IsAutoSaveSuppressedForTesting)
        {
            AutoSave(list);
        }
    }

    private void AutoSave(IList<ChangeOperation> list)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                using IDisposable fileWrite =
                    await EditorService.BeginProjectFileWriteAsync(
                        _autoSaveCancellation.Token);
                if (_disposed)
                {
                    return;
                }

                _autoSaveFailed = false;
                _autoSaveService.AutoSave(list);
                if (!_autoSaveFailed)
                {
                    CaptureSavedMediaUris();
                    ScheduleMediaFingerprints();
                }
                SaveState();
            }
            catch (OperationCanceledException)
                when (_autoSaveCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception occurred while auto-saving the editor state.");
            }
        });
    }

    private List<TimeRange> GetAffectedTimeRanges(IList<ChangeOperation> list)
    {
        return [.. list.SelectMany(GetAffectedTimeRangesFromOperation).Where(range => !range.IsEmpty)];
    }

    private IEnumerable<TimeRange> GetAffectedTimeRangesFromOperation(ChangeOperation operation)
    {
        // IUpdatePropertyValueOperationの場合
        if (operation is IUpdatePropertyValueOperation updateOp)
        {
            TimeRange? range = GetAffectedTimeRangeFromUpdateOperation(updateOp);
            if (range.HasValue)
            {
                yield return range.Value;
            }

            yield break;
        }

        // ICollectionChangeOperationの場合
        if (operation is ICollectionChangeOperation collectionOp)
        {
            // Objectプロパティから影響を受けるElementを探す
            Element? element = FindElementFromObject(collectionOp.Object);
            if (element != null)
            {
                yield return element.Range;
            }

            // Itemsから複数のElementを探す
            foreach (Element? item in collectionOp.Items
                         .Select(i => i is CoreObject coreObj ? FindElementFromObject(coreObj) : null)
                         .Where(i => i != null))
            {
                yield return item!.Range;
            }
        }
    }

    private TimeRange? GetAffectedTimeRangeFromUpdateOperation(IUpdatePropertyValueOperation updateOp)
    {
        CoreObject obj = updateOp.Object;
        string propertyPath = updateOp.PropertyPath;

        // ElementのStartまたはLengthプロパティの変更の場合
        if (obj is Element element)
        {
            string propertyName = GetPropertyNameFromPath(propertyPath);
            if (propertyName == nameof(Element.Start) || propertyName == nameof(Element.Length))
            {
                // 変更前後の両方の範囲を含む
                TimeRange currentRange = element.Range;

                // OldValueから変更前の範囲を計算
                if (updateOp.OldValue is TimeSpan oldTimeSpan)
                {
                    TimeRange oldRange = propertyName == nameof(Element.Start)
                        ? currentRange.WithStart(oldTimeSpan)
                        : currentRange.WithDuration(oldTimeSpan);
                    return currentRange.Union(oldRange);
                }

                return currentRange;
            }

            // その他のElementプロパティの場合
            return element.Range;
        }

        // Video-mute, solo, and audio-mute can all change graphics output (audio-mute
        // via audio-driven visualizers); only lock is editor-only and cache-neutral.
        if (obj is TimelineLayer layer)
        {
            string propertyName = GetPropertyNameFromPath(propertyPath);
            // ZIndex is absent because a layer's ZIndex only changes in
            // LayerMoveService.ApplyMove, whose Element.ZIndex writes already
            // invalidate the same frame ranges via the Element branch above.
            bool affectsGraphics = propertyName is nameof(TimelineLayer.IsVideoMuted)
                or nameof(TimelineLayer.IsSolo)
                or nameof(TimelineLayer.IsAudioMuted);
            if (!affectsGraphics) return null;

            // Solo re-filters every layer; video/audio-mute only affect their own zIndex.
            bool soloChanged = propertyName == nameof(TimelineLayer.IsSolo);
            TimeRange? union = null;
            foreach (Element el in Scene.Children)
            {
                if (!soloChanged && el.ZIndex != layer.ZIndex) continue;
                union = union is { } u ? u.Union(el.Range) : el.Range;
            }

            return union;
        }

        // Element以外のオブジェクトの場合、親を辿ってElementを探す
        Element? parentElement = FindElementFromObject(obj);
        if (parentElement != null)
        {
            return parentElement.Range;
        }

        return null;
    }

    private static string GetPropertyNameFromPath(string propertyPath)
    {
        if (propertyPath.Contains('.'))
        {
            string[] parts = propertyPath.Split('.');
            return parts[^1];
        }

        return propertyPath;
    }

    private static Element? FindElementFromObject(CoreObject obj)
    {
        if (obj is Element element)
        {
            return element;
        }

        if (obj is IHierarchical hierarchical)
        {
            return hierarchical.EnumerateAncestors<Element>().FirstOrDefault();
        }

        return null;
    }
}
