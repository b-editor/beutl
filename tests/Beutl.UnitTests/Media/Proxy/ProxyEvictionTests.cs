using Beutl.Editor;
using Beutl.Engine;
using Beutl.IO;
using Beutl.Media;
using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public class ProxyEvictionTests
{
    [Test]
    public void Sweep_RemovesLeastRecentlyUsedUntilUnderCap()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry oldEntry = Register(store, root, "old.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry newEntry = Register(store, root, "new.mp4", DateTime.UtcNow, 7);
        var service = new ProxyEvictionService(store, null, maxTotalBytes: 7);

        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(oldEntry.Source, oldEntry.Preset), Is.Null);
            Assert.That(store.TryGet(newEntry.Source, newEntry.Preset), Is.Not.Null);
        });
    }

    [Test]
    public void Sweep_SkipsPinnedEntries()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry pinned = Register(store, root, "pinned.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry candidate = Register(store, root, "candidate.mp4", DateTime.UtcNow, 7);
        var resolver = new ProxyResolver(store);
        var resolution = new ProxyResolution(
            Path.Combine(root, pinned.ProxyFileRelative),
            pinned.Source,
            pinned.Preset,
            pinned.OriginalLogicalFrameSize,
            pinned.ProxyDecodedFrameSize);
        using IDisposable pin = resolver.Pin(resolution);
        var service = new ProxyEvictionService(store, resolver, maxTotalBytes: 7);

        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(pinned.Source, pinned.Preset), Is.Not.Null);
            Assert.That(store.TryGet(candidate.Source, candidate.Preset), Is.Null);
        });
    }

    [Test]
    public void Sweep_ProtectsOpenProjectEntries_EvictingUnrelatedFirst()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        // The open-project proxy is the OLDEST, so pure LRU would reap it first.
        ProxyEntry openProject = Register(store, root, "open.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry unrelated = Register(store, root, "other.mp4", DateTime.UtcNow, 7);
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 7,
            openProjectSourceProvider: () => new HashSet<string> { openProject.Source.AbsolutePath });

        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(openProject.Source, openProject.Preset), Is.Not.Null);
            Assert.That(store.TryGet(unrelated.Source, unrelated.Preset), Is.Null);
        });
    }

    [Test]
    public void Sweep_ProtectsInProjectSource_CollectedFromProjectGraph()
    {
        string root = CreateRoot();
        // Media stored INSIDE the project directory: ExternalResourceCollector would have
        // filtered this out, so its proxy would have fallen back to plain LRU.
        string projectDir = Path.Combine(root, "project");
        Directory.CreateDirectory(projectDir);
        string inProjectSource = Path.Combine(projectDir, "clip.mov");
        File.WriteAllBytes(inProjectSource, [1]);

        var store = new ProxyStore(root);
        // The in-project proxy is the OLDEST, so pure LRU would reap it first.
        ProxyEntry inProject = RegisterForSource(store, root, inProjectSource, "in.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry unrelated = Register(store, root, "other.mp4", DateTime.UtcNow, 7);

        // Drive the real collection path CollectOpenProjectSources uses.
        var graph = new TestHierarchical();
        graph.AddChild(new TestEngineObjectWithFileSource(new TestFileSource(new Uri(inProjectSource))));
        IReadOnlySet<string> collected = ProxySourceEnumerator.EnumerateFileSources(graph);

        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 7,
            openProjectSourceProvider: () => collected);

        ProxyEvictionResult result = service.Sweep();

        Assert.Multiple(() =>
        {
            Assert.That(collected, Has.Count.EqualTo(1));
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(inProject.Source, inProject.Preset), Is.Not.Null);
            Assert.That(store.TryGet(unrelated.Source, unrelated.Preset), Is.Null);
        });
    }

    [Test]
    public void EnumerateFileSources_IncludesAnimatedKeyframeSources()
    {
        string animatedPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "animated-source.mov");
        var keyframeSource = new Beutl.Media.Source.VideoSource();
        keyframeSource.ReadFrom(new Uri(animatedPath));
        var animation = new Beutl.Animation.KeyFrameAnimation<Beutl.Media.Source.VideoSource?>();
        animation.KeyFrames.Add(new Beutl.Animation.KeyFrame<Beutl.Media.Source.VideoSource?>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = keyframeSource,
        });
        var drawable = new Beutl.Graphics.SourceVideo();
        drawable.Source.Animation = animation;

        var graph = new TestHierarchical();
        graph.AddChild(drawable);

        IReadOnlySet<string> collected = ProxySourceEnumerator.EnumerateFileSources(graph);

        Assert.That(collected, Does.Contain(new Uri(animatedPath).LocalPath));
    }

    [Test]
    public void Constructor_NegativeMaxTotalBytes_Throws()
    {
        var store = new ProxyStore(CreateRoot());

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ProxyEvictionService(store, resolver: null, maxTotalBytes: -1));
    }

    [Test]
    public void Sweep_SkipsEntriesWithActiveGeneration()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry generating = Register(store, root, "generating.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry idle = Register(store, root, "idle.mp4", DateTime.UtcNow, 7);
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 7,
            activeGenerationProvider: () => new HashSet<(ProxyFingerprint, ProxyPreset)>
            {
                (generating.Source, generating.Preset),
            });

        ProxyEvictionResult result = service.Sweep();

        // The LRU candidate is being regenerated, so deleting it now could lose the usable proxy if
        // that generation fails; eviction must fall through to the idle entry instead.
        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(generating.Source, generating.Preset), Is.Not.Null);
            Assert.That(store.TryGet(idle.Source, idle.Preset), Is.Null);
        });
    }

    [Test]
    public void Sweep_EvictsOpenProjectEntries_AsLastResort()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry olderOpen = Register(store, root, "older.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry newerOpen = Register(store, root, "newer.mp4", DateTime.UtcNow, 7);
        var protectedSet = new HashSet<string>
        {
            olderOpen.Source.AbsolutePath,
            newerOpen.Source.AbsolutePath,
        };
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 7,
            openProjectSourceProvider: () => protectedSet);

        ProxyEvictionResult result = service.Sweep();

        // Everything is protected, but the cap must still be met: the LRU protected one goes.
        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(olderOpen.Source, olderOpen.Preset), Is.Null);
            Assert.That(store.TryGet(newerOpen.Source, newerOpen.Preset), Is.Not.Null);
        });
    }

    [Test]
    public void SweepForDiskPressure_EvictsWhenHostFreeSpaceLow()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry oldEntry = Register(store, root, "old.mp4", DateTime.UtcNow.AddMinutes(-10), 20);
        ProxyEntry newEntry = Register(store, root, "new.mp4", DateTime.UtcNow, 20);
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            // Cap is generous, so only host free-space pressure can drive eviction.
            maxTotalBytes: 1_000_000,
            minFreeDiskBytes: 100,
            availableFreeSpaceProvider: _ => 90);

        ProxyEvictionResult result = service.SweepForDiskPressure();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(oldEntry.Source, oldEntry.Preset), Is.Null);
            Assert.That(store.TryGet(newEntry.Source, newEntry.Preset), Is.Not.Null);
        });
    }

    [Test]
    public void SweepForDiskPressure_DoesNotEvictWhenEvictionCannotFreeEnough()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry a = Register(store, root, "a.mp4", DateTime.UtcNow.AddMinutes(-10), 5);
        ProxyEntry b = Register(store, root, "b.mp4", DateTime.UtcNow, 5);
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 1_000_000,
            // Shortfall (1000 - 100 = 900) far exceeds the 10 bytes that could be reclaimed.
            minFreeDiskBytes: 1000,
            availableFreeSpaceProvider: _ => 100);

        ProxyEvictionResult result = service.SweepForDiskPressure();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(0));
            Assert.That(store.TryGet(a.Source, a.Preset), Is.Not.Null);
            Assert.That(store.TryGet(b.Source, b.Preset), Is.Not.Null);
        });
    }

    [Test]
    public void SweepForDiskPressure_NoEvictionWhenHeadroomSatisfied()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry a = Register(store, root, "a.mp4", DateTime.UtcNow.AddMinutes(-10), 20);
        ProxyEntry b = Register(store, root, "b.mp4", DateTime.UtcNow, 20);
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            maxTotalBytes: 1_000_000,
            minFreeDiskBytes: 100,
            availableFreeSpaceProvider: _ => 500);

        ProxyEvictionResult result = service.SweepForDiskPressure();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(0));
            Assert.That(store.TryGet(a.Source, a.Preset), Is.Not.Null);
            Assert.That(store.TryGet(b.Source, b.Preset), Is.Not.Null);
        });
    }

    [Test]
    public void SweepForDiskPressure_StillEnforcesCapWhenDiskGoalUnreachable()
    {
        string root = CreateRoot();
        var store = new ProxyStore(root);
        ProxyEntry oldEntry = Register(store, root, "old.mp4", DateTime.UtcNow.AddMinutes(-10), 7);
        ProxyEntry newEntry = Register(store, root, "new.mp4", DateTime.UtcNow, 7);
        var service = new ProxyEvictionService(
            store,
            resolver: null,
            // Over cap by 7 bytes; the disk goal (1000) is unreachable, so only the
            // cap overage should be reclaimed rather than over-evicting.
            maxTotalBytes: 7,
            minFreeDiskBytes: 1000,
            availableFreeSpaceProvider: _ => 0);

        ProxyEvictionResult result = service.SweepForDiskPressure();

        Assert.Multiple(() =>
        {
            Assert.That(result.RemovedCount, Is.EqualTo(1));
            Assert.That(store.TryGet(oldEntry.Source, oldEntry.Preset), Is.Null);
            Assert.That(store.TryGet(newEntry.Source, newEntry.Preset), Is.Not.Null);
        });
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static ProxyEntry Register(ProxyStore store, string root, string fileName, DateTime lastUsed, int bytes)
    {
        string sourcePath = Path.Combine(root, $"{Guid.NewGuid():N}.mov");
        File.WriteAllBytes(sourcePath, [1]);
        return RegisterForSource(store, root, sourcePath, fileName, lastUsed, bytes);
    }

    private static ProxyEntry RegisterForSource(
        ProxyStore store, string root, string sourcePath, string fileName, DateTime lastUsed, int bytes)
    {
        string proxyPath = Path.Combine(root, fileName);
        File.WriteAllBytes(proxyPath, Enumerable.Repeat((byte)1, bytes).ToArray());
        var source = ProxyFingerprint.FromFile(sourcePath);
        var entry = new ProxyEntry(
            source,
            ProxyPreset.Quarter,
            ProxyState.Ready,
            fileName,
            bytes,
            new PixelSize(100, 100),
            new PixelSize(25, 25),
            lastUsed,
            lastUsed,
            null);
        store.Register(entry);
        return entry;
    }

    private sealed class TestHierarchical : Hierarchical
    {
        public void AddChild(IHierarchical child)
        {
            HierarchicalChildren.Add(child);
        }
    }

    private sealed class TestFileSource(Uri? uri) : IFileSource
    {
        public Uri Uri { get; private set; } = uri!;

        public void ReadFrom(Uri uri)
        {
            Uri = uri;
        }
    }

    [SuppressResourceClassGeneration]
    private sealed class TestEngineObjectWithFileSource : EngineObject
    {
        public TestEngineObjectWithFileSource(IFileSource? fileSource)
        {
            ScanProperties<TestEngineObjectWithFileSource>();
            FileSource.CurrentValue = fileSource;
        }

        public IProperty<IFileSource?> FileSource { get; } = Property.Create<IFileSource?>();
    }
}
