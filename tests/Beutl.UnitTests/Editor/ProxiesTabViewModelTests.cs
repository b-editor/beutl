using System.Globalization;
using Beutl;
using Beutl.Animation;
using Beutl.Editor.Components.ProxiesTab.ViewModels;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Reactive.Bindings;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public sealed class ProxiesTabViewModelTests
{
    [Test]
    public void Refresh_BuildsReadySummaryAndClipDisplay()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 2048);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(sourcePath);
        DateTime now = DateTime.UtcNow;
        RegisterProxyEntry(store, new ProxyEntry(
            fingerprint,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "hash/quarter.mp4",
            1536,
            new PixelSize(1920, 1080),
            new PixelSize(480, 270),
            now,
            now,
            null));

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));

        ProxyClipViewModel clip = viewModel.Clips.Single();
        Assert.Multiple(() =>
        {
            Assert.That(
                viewModel.ClipSummary.Value,
                Is.EqualTo(string.Format(CultureInfo.CurrentCulture, Strings.ProxyClipSummaryFormat, 1, 1, 0, 0, 0)));
            Assert.That(viewModel.ProjectUsageText.Value, Is.EqualTo("1.5 KB"));
            Assert.That(viewModel.StoreUsageText.Value, Is.EqualTo("1.5 KB"));
            Assert.That(viewModel.JobSummary.Value, Is.EqualTo(Strings.ProxyQueueIdle));
            Assert.That(viewModel.StatusMessage.Value, Is.EqualTo(Strings.ProxyReady));
            Assert.That(clip.Preset.Value, Is.EqualTo(ProxyPreset.Quarter));
            Assert.That(clip.State.Value, Is.EqualTo(Strings.ProxyReady));
            Assert.That(clip.IsReady.Value, Is.True);
            Assert.That(
                clip.ProxyInfoText.Value,
                Is.EqualTo(string.Format(CultureInfo.CurrentCulture, Strings.ProxyInfoFormat, "1920x1080", "480x270", "1.5 KB")));
            Assert.That(
                clip.SourceInfoText,
                Does.StartWith(string.Format(CultureInfo.CurrentCulture, Strings.ProxySourceInfoFormat, "2 KB", string.Empty)));
        });

        clip.IsSelected.Value = true;

        Assert.That(viewModel.SelectionSummary.Value, Is.EqualTo(Strings.ProxySelectedSingular));
    }

    [Test]
    public void Refresh_SeparatesStaleAndMissingClips()
    {
        string root = CreateRoot();
        string staleSourcePath = CreateSourceFile(root, "stale.mov", 1024);
        string missingSourcePath = CreateSourceFile(root, "missing.mov", 512);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint oldFingerprint = ProxyFingerprint.FromFile(staleSourcePath);
        RegisterProxyEntry(store, new ProxyEntry(
            oldFingerprint,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "hash/quarter.mp4",
            768,
            new PixelSize(1280, 720),
            new PixelSize(320, 180),
            DateTime.UtcNow,
            DateTime.UtcNow,
            null));
        File.AppendAllBytes(staleSourcePath, [1]);
        File.SetLastWriteTimeUtc(staleSourcePath, DateTime.UtcNow.AddMinutes(1));

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, staleSourcePath, missingSourcePath));

        Assert.Multiple(() =>
        {
            Assert.That(
                viewModel.ClipSummary.Value,
                Is.EqualTo(string.Format(CultureInfo.CurrentCulture, Strings.ProxyClipSummaryFormat, 2, 0, 1, 0, 1)));
            Assert.That(viewModel.Clips.Single(c => c.FileName == "stale.mov").State.Value, Is.EqualTo(Strings.ProxyStale));
            Assert.That(viewModel.Clips.Single(c => c.FileName == "missing.mov").State.Value, Is.EqualTo(Strings.ProxyMissing));
        });
    }

    [Test]
    public void Refresh_SelectsGeneratedPresetWhenConfiguredDefaultIsMissing()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 2048);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(sourcePath);
        DateTime now = DateTime.UtcNow;
        RegisterProxyEntry(store, new ProxyEntry(
            fingerprint,
            ProxyPreset.Half,
            ProxyState.Ready,
            "hash/half.mp4",
            1536,
            new PixelSize(1920, 1080),
            new PixelSize(960, 540),
            now,
            now,
            null));

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));

        ProxyClipViewModel clip = viewModel.Clips.Single();
        Assert.Multiple(() =>
        {
            Assert.That(clip.Preset.Value, Is.EqualTo(ProxyPreset.Half));
            Assert.That(clip.State.Value, Is.EqualTo(Strings.ProxyReady));
            Assert.That(clip.IsReady.Value, Is.True);
            Assert.That(
                clip.ProxyInfoText.Value,
                Is.EqualTo(string.Format(CultureInfo.CurrentCulture, Strings.ProxyInfoFormat, "1920x1080", "960x540", "1.5 KB")));
            Assert.That(
                viewModel.ClipSummary.Value,
                Is.EqualTo(string.Format(CultureInfo.CurrentCulture, Strings.ProxyClipSummaryFormat, 1, 1, 0, 0, 0)));
        });

        clip.Preset.Value = ProxyPreset.Eighth;

        Assert.Multiple(() =>
        {
            Assert.That(clip.State.Value, Is.EqualTo(Strings.ProxyMissing));
            Assert.That(clip.IsMissing.Value, Is.True);
            Assert.That(
                viewModel.ClipSummary.Value,
                Is.EqualTo(string.Format(CultureInfo.CurrentCulture, Strings.ProxyClipSummaryFormat, 1, 0, 0, 0, 1)));
        });
    }

    [Test]
    public void Refresh_IncludesVideoSourceNodeSources()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "graph.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));

        using var viewModel = new ProxiesTabViewModel(CreateGraphContext(root, store, sourcePath));

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Clips, Has.Count.EqualTo(1));
            Assert.That(viewModel.Clips.Single().FileName, Is.EqualTo("graph.mov"));
        });
    }

    [Test]
    public void Refresh_IncludesNestedSceneVideoSources()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "nested.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        Scene childScene = CreateScene(root, "child.scene");
        AddSourceVideo(childScene, root, sourcePath);
        Scene parentScene = CreateScene(root, "parent.scene");
        var sceneDrawable = new SceneDrawable();
        sceneDrawable.ReferencedScene.CurrentValue = childScene;
        AddObject(parentScene, root, sceneDrawable);

        using var viewModel = new ProxiesTabViewModel(CreateContext(parentScene, store));

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Clips, Has.Count.EqualTo(1));
            Assert.That(viewModel.Clips.Single().FileName, Is.EqualTo("nested.mov"));
        });
    }

    [Test]
    public void Refresh_IncludesOfflineSourceWhenStoreHasExistingEntry()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "offline.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(sourcePath);
        DateTime now = DateTime.UtcNow;
        var entry = new ProxyEntry(
            fingerprint,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "hash/quarter.mp4",
            512,
            new PixelSize(1920, 1080),
            new PixelSize(480, 270),
            now,
            now,
            null);
        RegisterProxyEntry(store, entry);
        File.Delete(sourcePath);

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));

        ProxyClipViewModel clip = viewModel.Clips.Single();
        Assert.Multiple(() =>
        {
            Assert.That(clip.FileName, Is.EqualTo("offline.mov"));
            Assert.That(clip.Source, Is.EqualTo(fingerprint));
            Assert.That(clip.IsReady.Value, Is.True);
            Assert.That(viewModel.ProjectUsageText.Value, Is.EqualTo("512 B"));
        });
    }

    [Test]
    public void Refresh_IncludesAnimatedSourceVideoValues()
    {
        string root = CreateRoot();
        string firstPath = CreateSourceFile(root, "first.mov", 1024);
        string secondPath = CreateSourceFile(root, "second.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        Scene scene = CreateScene(root, "animated.scene");
        var drawable = new SourceVideo();
        drawable.Source.CurrentValue = CreateVideoSource(firstPath);
        var animation = new KeyFrameAnimation<VideoSource?>();
        animation.KeyFrames.Add(new KeyFrame<VideoSource?>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = CreateVideoSource(secondPath),
        });
        drawable.Source.Animation = animation;
        AddObject(scene, root, drawable);

        using var viewModel = new ProxiesTabViewModel(CreateContext(scene, store));

        Assert.That(
            viewModel.Clips.Select(static clip => clip.FileName),
            Is.EquivalentTo(new[] { "first.mov", "second.mov" }));
    }

    [Test]
    public async Task RegenerateAsync_KeepsExistingProxyUntilReplacementSucceeds()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 2048);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(sourcePath);
        DateTime now = DateTime.UtcNow;
        var entry = new ProxyEntry(
            fingerprint,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "hash/quarter.mp4",
            1536,
            new PixelSize(1920, 1080),
            new PixelSize(480, 270),
            now,
            now,
            null);
        RegisterProxyEntry(store, entry);
        var queue = new TestProxyJobQueue();

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, queue, sourcePath));

        await viewModel.RegenerateAsync(viewModel.Clips.Single());

        Assert.Multiple(() =>
        {
            Assert.That(store.TryGet(entry.Source, entry.Preset), Is.EqualTo(entry));
            Assert.That(queue.Pending().Single().Source, Is.EqualTo(fingerprint));
        });
    }

    [Test]
    public void Refresh_AttachesPendingQueueJobToMatchingClip()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "queued.mov", 4096);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(sourcePath);
        var job = new ProxyJob(fingerprint, ProxyPreset.Quarter)
        {
            Status = ProxyJobStatus.Running,
            LatestProgress = new ProxyJobProgress(0.42, null),
        };
        var queue = new TestProxyJobQueue(job);

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, queue, sourcePath));

        ProxyClipViewModel clip = viewModel.Clips.Single();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.JobSummary.Value, Is.EqualTo(Strings.ProxyQueuedJobSingular));
            Assert.That(clip.HasJob.Value, Is.True);
            Assert.That(clip.JobStatus.Value, Is.EqualTo(Strings.ProxyJobStatusRunning));
            Assert.That(clip.JobProgressValue.Value, Is.EqualTo(0.42).Within(0.001));
            Assert.That(clip.JobProgressText.Value, Is.EqualTo(0.42.ToString("P0", CultureInfo.CurrentCulture)));
        });

        clip.CancelJobCommand.Execute();

        Assert.That(queue.CanceledJobIds, Is.EqualTo(new[] { job.JobId }));
    }

    [Test]
    public void ToggleSelection_InvertsClipSelection()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));

        ProxyClipViewModel clip = viewModel.Clips.Single();
        clip.ToggleSelection();

        Assert.Multiple(() =>
        {
            Assert.That(clip.IsSelected.Value, Is.True);
            Assert.That(viewModel.SelectionSummary.Value, Is.EqualTo(Strings.ProxySelectedSingular));
        });

        clip.ToggleSelection();

        Assert.Multiple(() =>
        {
            Assert.That(clip.IsSelected.Value, Is.False);
            Assert.That(
                viewModel.SelectionSummary.Value,
                Is.EqualTo(string.Format(CultureInfo.CurrentCulture, Strings.ProxySelectedPlural, 0)));
        });
    }

    private static TestEditorContext CreateContext(string root, ProxyStore store, params string[] sourcePaths)
        => CreateContext(root, store, queue: null, sourcePaths);

    private static TestEditorContext CreateContext(
        string root,
        ProxyStore store,
        IProxyJobQueue? queue,
        params string[] sourcePaths)
    {
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "test.scene")),
        };

        foreach (string sourcePath in sourcePaths)
        {
            var source = new VideoSource();
            source.ReadFrom(new Uri(sourcePath));
            var drawable = new SourceVideo();
            drawable.Source.CurrentValue = source;
            var element = new Element
            {
                Start = TimeSpan.Zero,
                Length = TimeSpan.FromSeconds(1),
                IsEnabled = true,
                Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
            };
            element.AddObject(drawable);
            scene.Children.Add(element);
        }

        var context = new TestEditorContext(scene);
        context.AddService(scene);
        context.AddService<IProxyStore>(store);
        if (queue != null)
        {
            context.AddService(queue);
        }

        return context;
    }

    private static TestEditorContext CreateGraphContext(string root, ProxyStore store, string sourcePath)
    {
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "test.scene")),
        };

        var source = new VideoSource();
        source.ReadFrom(new Uri(sourcePath));
        var node = new VideoSourceNode();
        node.Source.Property!.SetValue(source);
        var drawable = new NodeGraphDrawable();
        drawable.Model.CurrentValue!.Nodes.Add(node);
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(drawable);
        scene.Children.Add(element);

        var context = new TestEditorContext(scene);
        context.AddService(scene);
        context.AddService<IProxyStore>(store);
        return context;
    }

    private static TestEditorContext CreateContext(Scene scene, ProxyStore store, IProxyJobQueue? queue = null)
    {
        var context = new TestEditorContext(scene);
        context.AddService(scene);
        context.AddService<IProxyStore>(store);
        if (queue != null)
        {
            context.AddService(queue);
        }

        return context;
    }

    private static Scene CreateScene(string root, string fileName)
    {
        return new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, fileName)),
        };
    }

    private static SourceVideo AddSourceVideo(Scene scene, string root, string sourcePath)
    {
        var drawable = new SourceVideo();
        drawable.Source.CurrentValue = CreateVideoSource(sourcePath);
        AddObject(scene, root, drawable);
        return drawable;
    }

    private static void AddObject(Scene scene, string root, EngineObject obj)
    {
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(1),
            IsEnabled = true,
            Uri = new Uri(Path.Combine(root, $"{Guid.NewGuid():N}.layer")),
        };
        element.AddObject(obj);
        scene.Children.Add(element);
    }

    private static VideoSource CreateVideoSource(string sourcePath)
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(sourcePath));
        return source;
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateSourceFile(string root, string fileName, int bytes)
    {
        string path = Path.Combine(root, fileName);
        File.WriteAllBytes(path, Enumerable.Repeat((byte)7, bytes).ToArray());
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        return path;
    }

    private static void RegisterProxyEntry(ProxyStore store, ProxyEntry entry)
    {
        string proxyPath = Path.Combine(
            store.StoreRootPath,
            entry.ProxyFileRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(proxyPath)!);
        File.WriteAllBytes(proxyPath, Enumerable.Repeat((byte)1, checked((int)entry.ProxyFileSizeBytes)).ToArray());
        store.Register(entry);
    }

    private sealed class TestEditorContext(CoreObject obj) : IEditorContext
    {
        private readonly Dictionary<Type, object> _services = [];

        public CoreObject Object { get; } = obj;

        public EditorExtension Extension => null!;

        public IReactiveProperty<bool> IsEnabled { get; } = new ReactivePropertySlim<bool>(true);

        public IKnownEditorCommands? Commands => null;

        public void AddService<T>(T service)
            where T : notnull
        {
            _services[typeof(T)] = service;
        }

        public object? GetService(Type serviceType)
        {
            return _services.GetValueOrDefault(serviceType);
        }

        public T? FindToolTab<T>(Func<T, bool> condition)
            where T : IToolContext
        {
            return default;
        }

        public T? FindToolTab<T>()
            where T : IToolContext
        {
            return default;
        }

        public bool OpenToolTab(IToolContext item)
        {
            return false;
        }

        public void CloseToolTab(IToolContext item)
        {
        }
    }

    private sealed class TestProxyJobQueue(params ProxyJob[] pendingJobs) : IProxyJobQueue
    {
        private readonly List<ProxyJob> _pendingJobs = [.. pendingJobs];

        public int MaxConcurrency => 1;

        public List<Guid> CanceledJobIds { get; } = [];

        public event EventHandler<ProxyJobChangedEventArgs>? JobChanged;

        public ValueTask<ProxyJob> EnqueueAsync(
            ProxyFingerprint source,
            ProxyPreset preset,
            CancellationToken cancellationToken = default)
        {
            var job = new ProxyJob(source, preset);
            _pendingJobs.Add(job);
            JobChanged?.Invoke(this, new ProxyJobChangedEventArgs
            {
                Job = job,
                Kind = ProxyJobChangeKind.Enqueued,
            });
            return ValueTask.FromResult(job);
        }

        public IReadOnlyList<ProxyJob> Pending() => _pendingJobs;

        public void Cancel(Guid jobId)
        {
            CanceledJobIds.Add(jobId);
        }

        public void CancelAll()
        {
            CanceledJobIds.AddRange(_pendingJobs.Select(static job => job.JobId));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
