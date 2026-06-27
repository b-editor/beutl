using System.Globalization;
using Beutl;
using Beutl.Editor.Components.ProxiesTab.ViewModels;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
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
        store.Register(new ProxyEntry(
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
            Assert.That(viewModel.SelectedPresetDisplayText.Value, Is.EqualTo(Strings.ProxyPresetQuarter));
            Assert.That(clip.State, Is.EqualTo(Strings.ProxyReady));
            Assert.That(clip.IsReady, Is.True);
            Assert.That(
                clip.ProxyInfoText,
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
        store.Register(new ProxyEntry(
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
            Assert.That(viewModel.Clips.Single(c => c.FileName == "stale.mov").State, Is.EqualTo(Strings.ProxyStale));
            Assert.That(viewModel.Clips.Single(c => c.FileName == "missing.mov").State, Is.EqualTo(Strings.ProxyMissing));
        });
    }

    private static TestEditorContext CreateContext(string root, ProxyStore store, params string[] sourcePaths)
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
        return context;
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
}
