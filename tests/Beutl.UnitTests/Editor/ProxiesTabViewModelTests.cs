using System.Globalization;
using Beutl;
using Beutl.Animation;
using Beutl.Configuration;
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
    public void Refresh_UsesRunningEvictionCapForStoreCapDisplay()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 2048);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        var context = CreateContext(root, store, sourcePath);
        context.AddService<IProxyStoreCapInfo>(new TestProxyStoreCapInfo(12L * 1024 * 1024 * 1024));

        using var viewModel = new ProxiesTabViewModel(context);

        Assert.That(viewModel.StoreCapText.Value, Is.EqualTo("12 GB"));
    }

    // Changing only the store cap in Settings while the tab is open must refresh StoreCapText; no other
    // signal on the open tab reflects a cap-only change until an unrelated store / job / scene refresh.
    [Test]
    public void ProxyConfigCapChange_RefreshesStoreCapText()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 2048);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        long originalCap = config.MaxTotalBytes;
        try
        {
            config.MaxTotalBytes = 10L * 1024 * 1024 * 1024;
            using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));
            Assert.That(viewModel.StoreCapText.Value, Is.EqualTo("10 GB"), "sanity: the tab shows the cap it was built with");

            config.MaxTotalBytes = 20L * 1024 * 1024 * 1024;

            Assert.That(viewModel.StoreCapText.Value, Is.EqualTo("20 GB"));
        }
        finally
        {
            config.MaxTotalBytes = originalCap;
        }
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

    // With the original offline and several same-path entries, the row must bind to the Ready proxy
    // (ranked like the resolver) rather than an earlier-enumerated Failed/Stale entry, so its state and
    // per-row delete/regenerate target the usable proxy.
    [Test]
    public void Refresh_OfflineSourceWithMultipleEntries_BindsRowToReadyEntry()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "offline.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint readyFp = ProxyFingerprint.FromFile(sourcePath);
        // Same path, older size/mtime: a distinct fingerprint keyed on the same AbsolutePath.
        ProxyFingerprint failedFp = readyFp with
        {
            FileSizeBytes = readyFp.FileSizeBytes + 4096,
            MtimeUtc = readyFp.MtimeUtc.AddMinutes(-10),
        };
        DateTime now = DateTime.UtcNow;
        // Register the Failed entry first so a naive FirstOrDefault would bind the row to it.
        RegisterProxyEntry(store, new ProxyEntry(
            failedFp, ProxyPreset.Quarter, ProxyState.Failed, "hash/failed.mp4", 256,
            new PixelSize(1920, 1080), new PixelSize(480, 270), now, now, "boom"));
        RegisterProxyEntry(store, new ProxyEntry(
            readyFp, ProxyPreset.Quarter, ProxyState.Ready, "hash/ready.mp4", 512,
            new PixelSize(1920, 1080), new PixelSize(480, 270), now, now, null));
        File.Delete(sourcePath);

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));

        ProxyClipViewModel clip = viewModel.Clips.Single();
        Assert.Multiple(() =>
        {
            Assert.That(clip.Source, Is.EqualTo(readyFp), "the offline row must bind to the Ready entry, not the earlier Failed one");
            Assert.That(clip.IsReady.Value, Is.True);
        });
    }

    // With Ready entries from different source versions for the same offline path, the row must bind to
    // the newest version (mirroring ProxyResolver.ResolveByPath), even when an older version's proxy is
    // denser — otherwise delete/regenerate would target a stale proxy the preview no longer decodes.
    [Test]
    public void Refresh_OfflineSourceWithReadyEntriesFromDifferentVersions_BindsNewest()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "offline.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint newFp = ProxyFingerprint.FromFile(sourcePath);
        // Older source version (distinct size/mtime), a denser Half proxy generated earlier.
        ProxyFingerprint oldFp = newFp with
        {
            FileSizeBytes = newFp.FileSizeBytes + 4096,
            MtimeUtc = newFp.MtimeUtc.AddMinutes(-10),
        };
        var oldTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        RegisterProxyEntry(store, new ProxyEntry(
            oldFp, ProxyPreset.Half, ProxyState.Ready, "hash/old-half.mp4", 768,
            new PixelSize(1920, 1080), new PixelSize(960, 540), oldTime, oldTime, null));
        RegisterProxyEntry(store, new ProxyEntry(
            newFp, ProxyPreset.Quarter, ProxyState.Ready, "hash/new-quarter.mp4", 512,
            new PixelSize(1920, 1080), new PixelSize(480, 270), newTime, newTime, null));
        File.Delete(sourcePath);

        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        int originalDefault = config.DefaultPreset;
        try
        {
            // Default preset Half: the denser old Half is under the cap, so density-only ranking would
            // bind the old version; the newest-version filter must beat that and bind the new Quarter.
            config.DefaultPreset = (int)ProxyPreset.Half;
            using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));

            ProxyClipViewModel clip = viewModel.Clips.Single();
            Assert.That(clip.Source, Is.EqualTo(newFp), "the row must bind to the newest source version, not a denser older proxy");
        }
        finally
        {
            config.DefaultPreset = originalDefault;
        }
    }

    // If the newest same-path source has only a Failed entry (regeneration failed) while an older Ready
    // proxy lingers, the row must bind to the newest source's state, not the stale older Ready — matching
    // the preview, which serves no proxy for the current source.
    [Test]
    public void Refresh_OfflineSourceWithNewerFailedVersion_BindsNewestNotOldReady()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "offline.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint newFp = ProxyFingerprint.FromFile(sourcePath);
        ProxyFingerprint oldFp = newFp with
        {
            FileSizeBytes = newFp.FileSizeBytes + 4096,
            MtimeUtc = newFp.MtimeUtc.AddMinutes(-10),
        };
        var oldTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var newTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Older source: a Ready proxy generated earlier. Newer source: only a Failed entry, generated later.
        RegisterProxyEntry(store, new ProxyEntry(
            oldFp, ProxyPreset.Quarter, ProxyState.Ready, "hash/old-ready.mp4", 512,
            new PixelSize(1920, 1080), new PixelSize(480, 270), oldTime, oldTime, null));
        RegisterProxyEntry(store, new ProxyEntry(
            newFp, ProxyPreset.Quarter, ProxyState.Failed, "hash/new-failed.mp4", 0,
            new PixelSize(1920, 1080), new PixelSize(480, 270), newTime, newTime, "boom"));
        File.Delete(sourcePath);

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));

        ProxyClipViewModel clip = viewModel.Clips.Single();
        Assert.Multiple(() =>
        {
            Assert.That(clip.Source, Is.EqualTo(newFp), "the row must bind to the newest source, not the stale older Ready");
            Assert.That(clip.IsReady.Value, Is.False);
        });
    }

    // With multiple Ready entries for an offline path, the row must bind to the fingerprint preview
    // decoding picks: the densest within the default-preset density cap (mirroring ProxyResolver), not
    // simply the densest overall.
    [Test]
    public void Refresh_OfflineSourceWithMultipleReadyEntries_BindsWithinDefaultPresetDensityCap()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "offline.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint sourceFp = ProxyFingerprint.FromFile(sourcePath);
        DateTime now = DateTime.UtcNow;
        // One source version, two Ready presets keyed on the same fingerprint: Quarter density 0.25
        // (480/1920), Half density 0.5 (960/1920). Register the denser Half first.
        RegisterProxyEntry(store, new ProxyEntry(
            sourceFp, ProxyPreset.Half, ProxyState.Ready, "hash/half.mp4", 768,
            new PixelSize(1920, 1080), new PixelSize(960, 540), now, now, null));
        RegisterProxyEntry(store, new ProxyEntry(
            sourceFp, ProxyPreset.Quarter, ProxyState.Ready, "hash/quarter.mp4", 512,
            new PixelSize(1920, 1080), new PixelSize(480, 270), now, now, null));
        File.Delete(sourcePath);

        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        int originalDefault = config.DefaultPreset;
        try
        {
            // Cap at Quarter (density 0.25): the capped pick is the Quarter proxy, not the denser Half.
            config.DefaultPreset = (int)ProxyPreset.Quarter;
            using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));

            Assert.That(viewModel.Clips.Single().Preset.Value, Is.EqualTo(ProxyPreset.Quarter),
                "the row must bind to the density-capped Quarter proxy preview would decode, not the densest Half");
        }
        finally
        {
            config.DefaultPreset = originalDefault;
        }
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

    // A clip referencing media through a symlink whose target was moved/deleted must still match its
    // stored entry (keyed on the resolved target) rather than dropping off the tab.
    [Test]
    public void Refresh_SymlinkedSourceWithMovedTarget_StaysMatchedToStoredEntry()
    {
        string root = CreateRoot();
        string target = CreateSourceFile(root, "target.mov", 1024);
        string link = Path.Combine(root, "link.mov");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore("Creating a symbolic link is not permitted in this environment.");
        }

        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint resolved = ProxyFingerprint.FromFile(link);
        DateTime now = DateTime.UtcNow;
        RegisterProxyEntry(store, new ProxyEntry(
            resolved,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "hash/quarter.mp4",
            512,
            new PixelSize(1920, 1080),
            new PixelSize(480, 270),
            now,
            now,
            null));

        // Move the target away: TryFromFile(link) now fails, forcing the resolved-path fallback.
        File.Move(target, Path.Combine(root, "moved.mov"));

        var scene = CreateScene(root, "symlink.scene");
        AddSourceVideo(scene, root, link);

        using var viewModel = new ProxiesTabViewModel(CreateContext(scene, store));

        Assert.That(viewModel.Clips.Select(static clip => clip.FileName), Does.Contain("link.mov"));
    }

    // Project-wide actions scan every scene, so an edit in a scene other than the tab's own must
    // still refresh the clip list.
    [Test]
    public void SceneEdited_InAnotherProjectScene_RefreshesClipList()
    {
        string root = CreateRoot();
        string path1 = CreateSourceFile(root, "scene1.mov", 1024);
        string path2 = CreateSourceFile(root, "scene2.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        Scene scene1 = CreateScene(root, "scene1.scene");
        AddSourceVideo(scene1, root, path1);
        Scene scene2 = CreateScene(root, "scene2.scene");
        var project = new Project();
        project.Items.Add(scene1);
        project.Items.Add(scene2);

        using var viewModel = new ProxiesTabViewModel(CreateContext(scene1, store))
        {
            RefreshScheduler = static action => action(),
        };
        Assert.That(viewModel.Clips, Has.Count.EqualTo(1));

        AddSourceVideo(scene2, root, path2);

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

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath))
        {
            // Run the coalesced rebuild synchronously: with the real (never-pumped) test dispatcher
            // a scheduled rebuild would silently not run and the assertion below would be vacuous.
            RefreshScheduler = static action => action(),
        };
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
    public void OnStoreChanged_RegisteredEvent_PreservesClipSelection()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath))
        {
            RefreshScheduler = static action => action(),
        };
        ProxyClipViewModel clip = viewModel.Clips.Single();
        clip.ToggleSelection();
        string selectedPath = clip.Path;

        // A background proxy finishing raises Registered, which rebuilds the clip list; the user's
        // selection must survive the rebuild, not just the selected preset.
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

        Assert.That(viewModel.Clips.Single(c => c.Path == selectedPath).IsSelected.Value, Is.True);
    }

    [Test]
    public async Task Delete_CancelsMatchingQueuedGeneration()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        var queue = new TestProxyJobQueue();

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, queue, sourcePath));
        ProxyClipViewModel clip = viewModel.Clips.Single();
        ProxyJob job = await queue.EnqueueAsync(clip.Source, clip.Preset.Value);

        viewModel.Delete(clip);

        // Otherwise the in-flight generation would re-register the proxy on success and undo the delete.
        Assert.That(queue.CanceledJobIds, Does.Contain(job.JobId));
    }

    [Test]
    public void DefaultPresetChanged_UpdatesRowsOnPreviousDefault_KeepsExplicitChoices()
    {
        string root = CreateRoot();
        string firstPath = CreateSourceFile(root, "first.mov", 1024);
        string secondPath = CreateSourceFile(root, "second.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        int originalDefault = config.DefaultPreset;
        try
        {
            config.DefaultPreset = (int)ProxyPreset.Quarter;
            using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, firstPath, secondPath));
            ProxyClipViewModel following = viewModel.Clips[0];
            ProxyClipViewModel explicitChoice = viewModel.Clips[1];
            explicitChoice.Preset.Value = ProxyPreset.Eighth;

            config.DefaultPreset = (int)ProxyPreset.Half;

            Assert.Multiple(() =>
            {
                Assert.That(following.Preset.Value, Is.EqualTo(ProxyPreset.Half));
                Assert.That(explicitChoice.Preset.Value, Is.EqualTo(ProxyPreset.Eighth));
            });
        }
        finally
        {
            config.DefaultPreset = originalDefault;
        }
    }

    // #7: a row the user explicitly set to a preset that happens to equal the current default must keep
    // that choice when the default later changes — the old value-equality sweep overwrote it because it
    // could not tell an explicit pick from a row merely showing the default.
    [Test]
    public void DefaultPresetChanged_KeepsExplicitChoiceEqualToOldDefault()
    {
        string root = CreateRoot();
        string firstPath = CreateSourceFile(root, "first.mov", 1024);
        string secondPath = CreateSourceFile(root, "second.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        int originalDefault = config.DefaultPreset;
        try
        {
            config.DefaultPreset = (int)ProxyPreset.Quarter;
            using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, firstPath, secondPath));
            ProxyClipViewModel following = viewModel.Clips[0];
            ProxyClipViewModel explicitEqual = viewModel.Clips[1];
            // Change away and back so the row explicitly lands on Quarter (the current default) via a real
            // dropdown change: the row now shows the default value but is no longer following it.
            explicitEqual.Preset.Value = ProxyPreset.Half;
            explicitEqual.Preset.Value = ProxyPreset.Quarter;

            config.DefaultPreset = (int)ProxyPreset.Eighth;

            Assert.Multiple(() =>
            {
                Assert.That(following.Preset.Value, Is.EqualTo(ProxyPreset.Eighth), "the following row tracks the new default");
                Assert.That(explicitEqual.Preset.Value, Is.EqualTo(ProxyPreset.Quarter),
                    "the explicit choice equal to the old default must not be swept to the new default");
            });
        }
        finally
        {
            config.DefaultPreset = originalDefault;
        }
    }

    // A row pinned to an existing proxy is IsFollowingDefault=false; once that proxy is deleted and the
    // list rebuilds, the now proxy-less row must revert to following the default (it re-derives its state)
    // rather than staying stuck on the old preset when the default later changes.
    [Test]
    public void DefaultPresetChanged_ProxyPinnedRowRevertsToFollowingAfterProxyDeleted()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyStoreConfig config = GlobalConfiguration.Instance.ProxyStoreConfig;
        int originalDefault = config.DefaultPreset;
        try
        {
            config.DefaultPreset = (int)ProxyPreset.Quarter;
            ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(sourcePath);
            DateTime now = DateTime.UtcNow;
            RegisterProxyEntry(store, new ProxyEntry(
                fingerprint, ProxyPreset.Quarter, ProxyState.Ready, "hash/quarter.mp4", 512,
                new PixelSize(1920, 1080), new PixelSize(480, 270), now, now, null));

            using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath))
            {
                RefreshScheduler = static action => action(),
            };
            Assume.That(viewModel.Clips.Single().IsFollowingDefault, Is.False,
                "the row is pinned to the existing Ready proxy, so it does not follow the default");

            // Deleting the proxy raises a store Changed event that rebuilds the list; the rebuilt row is
            // proxy-less and must re-derive follow state to true.
            store.Delete(fingerprint, ProxyPreset.Quarter);
            Assume.That(viewModel.Clips.Single().IsFollowingDefault, Is.True,
                "the rebuilt proxy-less row reverts to following the default");

            config.DefaultPreset = (int)ProxyPreset.Half;

            Assert.That(viewModel.Clips.Single().Preset.Value, Is.EqualTo(ProxyPreset.Half),
                "the reverted row tracks the new default instead of staying on the stale preset");
        }
        finally
        {
            config.DefaultPreset = originalDefault;
        }
    }

    // Refresh re-associates prior row state by the canonical Source.AbsolutePath, not the raw LocalPath.
    // A symlink and its target resolve to the same AbsolutePath but different LocalPaths; after an explicit
    // preset pick on the merged row, dropping the symlink element (leaving the target) must keep the choice.
    [Test]
    public void Refresh_ExplicitChoiceSurvivesPathSpellingChange_KeyedByAbsolutePath()
    {
        string root = CreateRoot();
        string target = CreateSourceFile(root, "target.mov", 1024);
        string link = Path.Combine(root, "link.mov");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Ignore("Creating a symbolic link is not permitted in this environment.");
        }

        var store = new ProxyStore(Path.Combine(root, "proxies"));
        var scene = new Scene(1920, 1080, string.Empty) { Uri = new Uri(Path.Combine(root, "test.scene")) };
        // Both elements resolve to the same AbsolutePath, so enumeration dedupes them into one row whose
        // Path is the first-seen (link) spelling.
        Element linkElement = CreateVideoElement(root, link);
        Element targetElement = CreateVideoElement(root, target);
        scene.Children.Add(linkElement);
        scene.Children.Add(targetElement);

        using var viewModel = new ProxiesTabViewModel(CreateContext(scene, store))
        {
            RefreshScheduler = static action => action(),
        };
        ProxyClipViewModel clip = viewModel.Clips.Single();
        Assume.That(clip.Path, Is.EqualTo(link), "the merged row takes the first-seen (link) spelling");
        clip.Preset.Value = ProxyPreset.Half;

        // Drop the link element; the target element still yields the same AbsolutePath, so the explicit
        // Half choice must re-associate even though the row's Path spelling is now the target's.
        scene.Children.Remove(linkElement);

        ProxyClipViewModel rebuilt = viewModel.Clips.Single();
        Assert.Multiple(() =>
        {
            Assert.That(rebuilt.Path, Is.EqualTo(target), "the row now uses the target spelling");
            Assert.That(rebuilt.Preset.Value, Is.EqualTo(ProxyPreset.Half),
                "the explicit choice re-associates by AbsolutePath despite the changed Path spelling");
        });
    }

    // Generate All / Delete act on Clips, so the list must track project edits made while the tab
    // is open — not just proxy store/queue events.
    [Test]
    public void SceneEdited_AddingAndRemovingVideoElements_RebuildsClipList()
    {
        string root = CreateRoot();
        string firstPath = CreateSourceFile(root, "first.mov", 1024);
        string secondPath = CreateSourceFile(root, "second.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "test.scene")),
        };
        scene.Children.Add(CreateVideoElement(root, firstPath));

        using var viewModel = new ProxiesTabViewModel(CreateContext(scene, store))
        {
            RefreshScheduler = static action => action(),
        };
        Assert.That(viewModel.Clips, Has.Count.EqualTo(1));

        Element added = CreateVideoElement(root, secondPath);
        scene.Children.Add(added);
        Assert.That(viewModel.Clips, Has.Count.EqualTo(2));

        scene.Children.Remove(added);
        Assert.That(viewModel.Clips, Has.Count.EqualTo(1));
    }

    [Test]
    public void Delete_WhenProxyFileCannotBeDeleted_SurfacesFailureInStatusMessage()
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

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, new UndeletableStore(store), sourcePath));
        ProxyClipViewModel clip = viewModel.Clips.Single();

        viewModel.Delete(clip);

        Assert.That(viewModel.StatusMessage.Value, Is.EqualTo(Strings.ProxyDeleteFailedSingular));
    }

    // The real store's Delete removes the index entry but only best-effort-deletes the file; a surviving
    // .mp4 (a sharing violation while preview decodes it) is a real failure the UI must surface, even
    // though Delete returned true.
    [Test]
    public void Delete_WhenProxyFileSurvivesDelete_SurfacesFailureInStatusMessage()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 1024);
        var inner = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(sourcePath);
        DateTime now = DateTime.UtcNow;
        RegisterProxyEntry(inner, new ProxyEntry(
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

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, new OrphanFileStore(inner), sourcePath));
        ProxyClipViewModel clip = viewModel.Clips.Single();

        viewModel.Delete(clip);

        Assert.That(viewModel.StatusMessage.Value, Is.EqualTo(Strings.ProxyDeleteFailedSingular));
    }

    [Test]
    public void Delete_WhenEntryAlreadyGone_DoesNotReportFailure()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));
        ProxyClipViewModel clip = viewModel.Clips.Single();

        // No entry is registered, so Delete returns false for "not found" — a benign race,
        // not an in-use failure the user must be told about.
        viewModel.Delete(clip);

        Assert.That(viewModel.StatusMessage.Value, Is.Not.EqualTo(Strings.ProxyDeleteFailedSingular));
    }

    [Test]
    public void Delete_CancelsJobKeyedOnStaleEntrySource()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 1024);
        var store = new ProxyStore(Path.Combine(root, "proxies"));

        // A store entry left over from a prior version of the file: same path, older size/mtime, so it
        // matches the current clip by AbsolutePath but is a distinct fingerprint (the clip's EntrySource).
        ProxyFingerprint current = ProxyFingerprint.FromFile(sourcePath);
        ProxyFingerprint stale = current with
        {
            FileSizeBytes = current.FileSizeBytes + 4096,
            MtimeUtc = current.MtimeUtc.AddMinutes(-5),
        };
        DateTime now = DateTime.UtcNow;
        RegisterProxyEntry(store, new ProxyEntry(
            stale,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "hash/quarter.mp4",
            512,
            new PixelSize(1920, 1080),
            new PixelSize(480, 270),
            now,
            now,
            null));

        var job = new ProxyJob(stale, ProxyPreset.Quarter);
        var queue = new TestProxyJobQueue(job);
        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, queue, sourcePath));
        ProxyClipViewModel clip = viewModel.Clips.Single();
        Assume.That(clip.EntrySource, Is.EqualTo(stale));
        Assume.That(clip.Source, Is.Not.EqualTo(stale));

        viewModel.Delete(clip);

        // A job keyed on the stale EntrySource (not the current Source) must still be cancelled, or it
        // would re-register the deleted proxy on success.
        Assert.That(queue.CanceledJobIds, Does.Contain(job.JobId));
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
    public async Task DeleteAllForProject_DeletesProxyRegisteredDuringConfirmation()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 2048);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint fingerprint = ProxyFingerprint.FromFile(sourcePath);
        DateTime now = DateTime.UtcNow;
        var quarter = new ProxyEntry(
            fingerprint, ProxyPreset.Quarter, ProxyState.Ready, "hash/quarter.mp4", 1536,
            new PixelSize(1920, 1080), new PixelSize(480, 270), now, now, null);
        RegisterProxyEntry(store, quarter);

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, sourcePath));

        var half = new ProxyEntry(
            fingerprint, ProxyPreset.Half, ProxyState.Ready, "hash/half.mp4", 3072,
            new PixelSize(1920, 1080), new PixelSize(960, 540), now, now, null);
        viewModel.ConfirmDeleteAllForProjectAsync = _ =>
        {
            // A project job finishes while the confirmation dialog is open and registers a new proxy.
            RegisterProxyEntry(store, half);
            return Task.FromResult(true);
        };

        await viewModel.DeleteAllForProjectCommand.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(store.TryGet(quarter.Source, quarter.Preset), Is.Null);
            Assert.That(store.TryGet(half.Source, half.Preset), Is.Null,
                "a proxy registered during confirmation must be re-snapshotted and deleted");
        });
    }

    [Test]
    public async Task DeleteAllForProject_CancelsJobsForCurrentProjectFingerprints()
    {
        string root = CreateRoot();
        string sourcePath = CreateSourceFile(root, "clip.mov", 2048);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint current = ProxyFingerprint.FromFile(sourcePath);
        ProxyFingerprint stale = current with
        {
            FileSizeBytes = current.FileSizeBytes + 4096,
            MtimeUtc = current.MtimeUtc.AddMinutes(-5),
        };
        DateTime now = DateTime.UtcNow;
        var entry = new ProxyEntry(
            stale,
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
        var currentJob = new ProxyJob(current, ProxyPreset.Quarter);
        var queue = new TestProxyJobQueue(currentJob);

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, queue, sourcePath));
        viewModel.ConfirmDeleteAllForProjectAsync = _ => Task.FromResult(true);

        await viewModel.DeleteAllForProjectCommand.ExecuteAsync();

        Assert.Multiple(() =>
        {
            Assert.That(queue.CanceledJobIds, Does.Contain(currentJob.JobId));
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

    // After an in-place replace, the store holds a stale small-dimension entry for the old
    // fingerprint. It must not drive bulk eligibility for the current (heavy) source — the
    // file-size fallback should make the replaced 4K-but-small-metadata clip eligible.
    // A benign mtime-only change (touch/re-copy, same path + size) must keep using the stored
    // dimensions for bulk eligibility rather than falling back to the coarse file-size floor.
    [Test]
    public async Task GenerateAll_MtimeOnlyChange_StillUsesStoredDimensions()
    {
        string root = CreateRoot();
        string path = CreateSourceFile(root, "touched.mov", 4096);
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint current = ProxyFingerprint.FromFile(path);
        // Same path + size, older mtime: an mtime-only difference from the current fingerprint.
        ProxyFingerprint touched = current with { MtimeUtc = current.MtimeUtc.AddMinutes(-5) };
        DateTime now = DateTime.UtcNow;
        RegisterProxyEntry(store, new ProxyEntry(
            touched,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "hash/touched.mp4",
            1024,
            new PixelSize(3840, 2160),
            new PixelSize(960, 540),
            now,
            now,
            null));
        var queue = new TestProxyJobQueue();

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, queue, path));

        await viewModel.GenerateAllCommand.ExecuteAsync();

        // 3840x2160 clears the pixel floor even though the 4 KB file is far below the size floor.
        Assert.That(queue.Pending().Select(static job => job.Source), Does.Contain(current));
    }

    [Test]
    public async Task GenerateAll_StaleSamePathDimensions_DoNotSuppressHeavyClip()
    {
        string root = CreateRoot();
        string path = Path.Combine(root, "replaced.mov");
        using (FileStream fs = File.Create(path))
            fs.SetLength(33L * 1024 * 1024); // >= MinBulkSourceFileBytes, sparse (no real 33 MB write)
        var store = new ProxyStore(Path.Combine(root, "proxies"));
        ProxyFingerprint current = ProxyFingerprint.FromFile(path);
        ProxyFingerprint outdated = current with { FileSizeBytes = 1000, MtimeUtc = current.MtimeUtc.AddMinutes(-5) };
        DateTime now = DateTime.UtcNow;
        RegisterProxyEntry(store, new ProxyEntry(
            outdated,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            "hash/old.mp4",
            1024,
            new PixelSize(640, 480),
            new PixelSize(160, 120),
            now,
            now,
            null));
        var queue = new TestProxyJobQueue();

        using var viewModel = new ProxiesTabViewModel(CreateContext(root, store, queue, path));

        await viewModel.GenerateAllCommand.ExecuteAsync();

        Assert.That(queue.Pending().Select(static job => job.Source), Does.Contain(current));
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

    private static TestEditorContext CreateContext(string root, IProxyStore store, params string[] sourcePaths)
        => CreateContext(root, store, queue: null, sourcePaths);

    private static TestEditorContext CreateContext(
        string root,
        IProxyStore store,
        IProxyJobQueue? queue,
        params string[] sourcePaths)
    {
        var scene = new Scene(1920, 1080, string.Empty)
        {
            Uri = new Uri(Path.Combine(root, "test.scene")),
        };

        foreach (string sourcePath in sourcePaths)
        {
            scene.Children.Add(CreateVideoElement(root, sourcePath));
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

    private static Element CreateVideoElement(string root, string sourcePath)
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
        return element;
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

    private static TestEditorContext CreateContext(Scene scene, IProxyStore store, IProxyJobQueue? queue = null)
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

    // Simulates a Windows sharing violation: the entry stays registered because its file could not
    // be deleted, which is exactly the shape the ViewModel must report to the user.
    private sealed class UndeletableStore(IProxyStore inner) : IProxyStore
    {
        public string StoreRootPath => inner.StoreRootPath;

        public ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset) => inner.TryGet(source, preset);

        public IReadOnlyList<ProxyEntry> Enumerate() => inner.Enumerate();

        public void Register(ProxyEntry entry) => inner.Register(entry);

        public bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null)
            => inner.TryTransition(source, preset, newState, failureReason);

        public bool Delete(ProxyFingerprint source, ProxyPreset preset) => false;

        public void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc)
            => inner.Touch(source, preset, nowUtc);

        public long GetTotalBytes() => inner.GetTotalBytes();

        public long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths) => inner.GetTotalBytes(sourceAbsolutePaths);

        public Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public Task ReconcileAsync(CancellationToken cancellationToken) => inner.ReconcileAsync(cancellationToken);

        public event EventHandler<ProxyStoreChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }
    }

    // Delegates to a real store but keeps the proxy file on disk after Delete (which still returns true),
    // simulating a sharing violation where the index entry is removed yet File.Delete could not run.
    private sealed class OrphanFileStore(ProxyStore inner) : IProxyStore
    {
        public string StoreRootPath => inner.StoreRootPath;

        public ProxyEntry? TryGet(ProxyFingerprint source, ProxyPreset preset) => inner.TryGet(source, preset);

        public IReadOnlyList<ProxyEntry> Enumerate() => inner.Enumerate();

        public void Register(ProxyEntry entry) => inner.Register(entry);

        public bool TryTransition(ProxyFingerprint source, ProxyPreset preset, ProxyState newState, string? failureReason = null)
            => inner.TryTransition(source, preset, newState, failureReason);

        public bool Delete(ProxyFingerprint source, ProxyPreset preset)
        {
            ProxyEntry? entry = inner.TryGet(source, preset);
            string? path = entry is null
                ? null
                : Path.Combine(inner.StoreRootPath, entry.ProxyFileRelative.Replace('/', Path.DirectorySeparatorChar));
            byte[]? bytes = path is not null && File.Exists(path) ? File.ReadAllBytes(path) : null;

            bool result = inner.Delete(source, preset);

            if (bytes is not null && path is not null)
                File.WriteAllBytes(path, bytes);
            return result;
        }

        public void Touch(ProxyFingerprint source, ProxyPreset preset, DateTime nowUtc) => inner.Touch(source, preset, nowUtc);

        public long GetTotalBytes() => inner.GetTotalBytes();

        public long GetTotalBytes(IReadOnlySet<string> sourceAbsolutePaths) => inner.GetTotalBytes(sourceAbsolutePaths);

        public Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public Task ReconcileAsync(CancellationToken cancellationToken) => inner.ReconcileAsync(cancellationToken);

        public event EventHandler<ProxyStoreChangedEventArgs>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }
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
            int priority = 0,
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

    private sealed class TestProxyStoreCapInfo(long maxTotalBytes) : IProxyStoreCapInfo
    {
        public long MaxTotalBytes { get; } = maxTotalBytes;
    }
}
