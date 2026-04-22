using System.Collections.Concurrent;
using System.Collections.Specialized;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Logging;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.Proxy;

/// <summary>
/// Scene 内の SourceVideo を監視し、プロキシ自動生成モードのときに
/// ProxyGenerationQueue へジョブを投入するコーディネータ。
/// EditViewModel のライフサイクルに紐付く。
/// </summary>
public sealed class ProxyCoordinator : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<ProxyCoordinator>();

    private readonly Scene _scene;
    private readonly ConcurrentDictionary<Element, byte> _trackedElements = new();
    private readonly ConcurrentDictionary<SourceVideo, byte> _trackedSourceVideos = new();
    private readonly object _lock = new();
    private bool _disposed;

    public ProxyCoordinator(Scene scene)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _scene.Children.CollectionChanged += OnSceneChildrenChanged;

        foreach (Element element in _scene.Children)
        {
            AttachElement(element);
        }
    }

    public void RunInitialScan()
    {
        foreach (Element element in _scene.Children)
        {
            foreach (EngineObject obj in element.Objects)
            {
                if (obj is SourceVideo sourceVideo)
                {
                    TryEnqueue(sourceVideo.Source.CurrentValue);
                }
            }
        }
    }

    private void OnSceneChildrenChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (Element element in e.OldItems.OfType<Element>())
            {
                DetachElement(element);
            }
        }

        if (e.NewItems != null)
        {
            foreach (Element element in e.NewItems.OfType<Element>())
            {
                AttachElement(element);
            }
        }
    }

    private void AttachElement(Element element)
    {
        if (!_trackedElements.TryAdd(element, 0)) return;

        element.Objects.CollectionChanged += OnElementObjectsChanged;
        foreach (EngineObject obj in element.Objects)
        {
            AttachEngineObject(obj);
        }
    }

    private void DetachElement(Element element)
    {
        if (!_trackedElements.TryRemove(element, out _)) return;

        element.Objects.CollectionChanged -= OnElementObjectsChanged;
        foreach (EngineObject obj in element.Objects)
        {
            DetachEngineObject(obj);
        }
    }

    private void OnElementObjectsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (EngineObject obj in e.OldItems.OfType<EngineObject>())
            {
                DetachEngineObject(obj);
            }
        }

        if (e.NewItems != null)
        {
            foreach (EngineObject obj in e.NewItems.OfType<EngineObject>())
            {
                AttachEngineObject(obj);
            }
        }
    }

    private void AttachEngineObject(EngineObject obj)
    {
        if (obj is not SourceVideo sourceVideo) return;
        if (!_trackedSourceVideos.TryAdd(sourceVideo, 0)) return;

        sourceVideo.Source.ValueChanged += OnSourceVideoSourceChanged;
        TryEnqueue(sourceVideo.Source.CurrentValue);
    }

    private void DetachEngineObject(EngineObject obj)
    {
        if (obj is not SourceVideo sourceVideo) return;
        if (!_trackedSourceVideos.TryRemove(sourceVideo, out _)) return;

        sourceVideo.Source.ValueChanged -= OnSourceVideoSourceChanged;
    }

    private void OnSourceVideoSourceChanged(object? sender, PropertyValueChangedEventArgs<VideoSource?> e)
    {
        TryEnqueue(e.NewValue);
    }

    private static void TryEnqueue(VideoSource? videoSource)
    {
        if (videoSource is null) return;

        var proxyConfig = GlobalConfiguration.Instance.ProxyConfig;
        if (!proxyConfig.IsEnabled) return;
        if (proxyConfig.GenerationMode != ProxyGenerationMode.Auto) return;

        Uri? uri = videoSource.Uri;
        if (uri is null || !uri.IsFile) return;

        string path = uri.LocalPath;
        if (!File.Exists(path)) return;

        try
        {
            ProxyGenerationQueue.Instance.Enqueue(path, proxyConfig.ActivePreset);
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Failed to enqueue proxy job for {Path}", path);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _scene.Children.CollectionChanged -= OnSceneChildrenChanged;

        foreach (Element element in _trackedElements.Keys.ToArray())
        {
            DetachElement(element);
        }
    }
}
