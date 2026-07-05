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
    public void Refresh_IncludesSourcesFromAllProjectScenes()
    {
        string root = CreateRoot();
        string path1 = CreateSourceFile(root, "scene1.mov", 1024);
        string path2 = CreateSourceFile(root, "scene2.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        Scene scene1 = CreateScene(root, "scene1.scene");
        AddSourceVideo(scene1, root, path1);
        Scene scene2 = CreateScene(root, "scene2.scene");
        AddSourceVideo(scene2, root, path2);
        var project = new Project();
        project.Items.Add(scene1);
        project.Items.Add(scene2);

        using var viewModel = new ProxiesTabViewModel(CreateContext(scene1, store));

        Assert.That(
            viewModel.Clips.Select(static clip => clip.FileName),
            Is.EquivalentTo(new[] { "scene1.mov", "scene2.mov" }));
    }

    [Test]
    public void OnStoreChanged_TouchEvent_PreservesClipSelection()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(sourcePath);
        DateTime now = DateTime.UtcNow;
        RegisterProxyEntry(store, new ProxyEntry(
            fingerprint,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "hash/quarter.mp4",
            512,
            new PixelSize(1920, 1080),
            new PixelSize(480, 270),
            now,
            now,
            null));

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));
        ProxyClipViewModel clip = viewModel.Clips.Single();
        clip.ToggleSelection();

        // A Touch event (preview resolving a proxy) must not rebuild the clip list and drop selection.
        store.Touch(fingerprint, ProxyPreset.Quarter, DateTime.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Clips.Single(), Is.SameAs(clip));
            Assert.That(clip.IsSelected.Value, Is.True);
        });
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

    [Test]
    public async Task DeleteAllForProject_DeletesSharedCacheEntriesWhenConfirmed()
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

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));
        int? confirmedCount = null;
        viewModel.ConfirmDeleteAllForProjectAsync = count =>
        {
            confirmedCount = count;
            return Task.FromResult(true);
        };

        await viewModel.DeleteAllForProjectCommand.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(confirmedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(entry.Source, entry.Preset), Is.Null);
        });
    }

    [Test]
    public async Task DeleteAllForProject_KeepsEntriesWhenConfirmationDeclined()
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

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));
        bool confirmationRequested = false;
        viewModel.ConfirmDeleteAllForProjectAsync = _ =>
        {
            confirmationRequested = true;
            return Task.FromResult(false);
        };

        await viewModel.DeleteAllForProjectCommand.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(confirmationRequested, Is.True);
            Assert.That(store.TryGet(entry.Source, entry.Preset), Is.EqualTo(entry));
        });
    }

    [Test]
    public async Task PresetChange_CancelsInFlightJobForPreviousPreset()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 2048);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(sourcePath);
        var queue = new TestProxyJobQueue();

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, queue, sourcePath));
        ProxyClipViewModel clip = viewModel.Clips.Single();

        clip.Preset.Value = ProxyPreset.Quarter;
        ProxyJob job = await queue.EnqueueAsync(fingerprint, ProxyPreset.Quarter);
        Assert.That(clip.HasJob.Value, Is.True);

        clip.Preset.Value = ProxyPreset.Half;

        Assert.That(queue.CanceledJobIds, Does.Contain(job.JobId));
    }

    [Test]
    public async Task GenerateAll_SkipsSubFloorClipsButSingleGenerateStillEnqueues()
    {
        string root = CreateRoot();
        string heavyPath = CreateSourceFile(root, "heavy.mov", 4096);
        string lightPath = CreateSourceFile(root, "light.mov", 4096);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint heavyFingerprint = ProxyFingerprint.FromFile(heavyPath);
        ProxyFingerprint lightFingerprint = ProxyFingerprint.FromFile(lightPath);
        DateTime now = DateTime.UtcNow;
        RegisterProxyEntry(store, new ProxyEntry(
            heavyFingerprint,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "hash/heavy.mp4",
            1024,
            new PixelSize(3840, 2160),
            new PixelSize(960, 540),
            now,
            now,
            null));
        RegisterProxyEntry(store, new ProxyEntry(
            lightFingerprint,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "hash/light.mp4",
            1024,
            new PixelSize(640, 480),
            new PixelSize(160, 120),
            now,
            now,
            null));
        var queue = new TestProxyJobQueue();

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, queue, heavyPath, lightPath));

        await viewModel.GenerateAllCommand.ExecuteAsync();

        Assert.That(
            queue.Pending().Select(static job => job.Source),
            Is.EquivalentTo(new[] { heavyFingerprint }));

        ProxyClipViewModel lightClip = viewModel.Clips.Single(static c => c.FileName == "light.mov");
        await viewModel.GenerateAsync(lightClip);

        Assert.That(
            queue.Pending().Select(static job => job.Source),
            Is.EquivalentTo(new[] { heavyFingerprint, lightFingerprint }));
    }

    [Test]
    public async Task GenerateAsync_UserInitiatedJumpsAheadOfEarlierBulkEnqueue()
    {
        string root = CreateRoot();
        string heavyPath = CreateSourceFile(root, "heavy.mov", 4096);
        string foregroundPath = CreateSourceFile(root, "foreground.mov", 4096);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint heavyFingerprint = ProxyFingerprint.FromFile(heavyPath);
        DateTime now = DateTime.UtcNow;
        RegisterProxyEntry(store, new ProxyEntry(
            heavyFingerprint,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "hash/heavy.mp4",
            1024,
            new PixelSize(3840, 2160),
            new PixelSize(960, 540),
            now,
            now,
            null));
        ProxyFingerprint foregroundFingerprint = ProxyFingerprint.FromFile(foregroundPath);
        var queue = new TestProxyJobQueue();

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, queue, heavyPath, foregroundPath));

        await viewModel.GenerateAllCommand.ExecuteAsync();
        ProxyClipViewModel foregroundClip = viewModel.Clips.Single(static c => c.FileName == "foreground.mov");
        await viewModel.GenerateAsync(foregroundClip);

        ProxyJob bulkJob = queue.Pending().Single(job => job.Source.AbsolutePath == heavyFingerprint.AbsolutePath);
        ProxyJob foregroundJob = queue.Pending().Single(job => job.Source.AbsolutePath == foregroundFingerprint.AbsolutePath);
        Assert.That(
            foregroundJob.Priority,
            Is.GreaterThan(bulkJob.Priority),
            "the user-initiated clip must enqueue ahead of the earlier bulk sweep");
    }

    [Test]
    public async Task GenerateAll_AllLightProject_ReportsNoEligibleClipsAndEnqueuesNothing()
    {
        string root = CreateRoot();
        string firstPath = CreateSourceFile(root, "light-a.mov", 4096);
        string secondPath = CreateSourceFile(root, "light-b.mov", 4096);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        var queue = new TestProxyJobQueue();

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, queue, firstPath, secondPath));

        await viewModel.GenerateAllCommand.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Clips, Has.Count.EqualTo(2));
            Assert.That(viewModel.StatusMessage.Value, Is.EqualTo(Strings.ProxyBulkNoEligibleClips));
            Assert.That(queue.Pending(), Is.Empty);
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
            => EnqueueAsync(source, preset, priority: 0, cancellationToken);

        public ValueTask<ProxyJob> EnqueueAsync(
            ProxyFingerprint source,
            ProxyPreset preset,
            int priority,
            CancellationToken cancellationToken = default)
        {
            var job = new ProxyJob(source, preset, priority: priority);
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
