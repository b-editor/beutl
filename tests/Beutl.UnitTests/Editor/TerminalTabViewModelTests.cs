using Beutl.Editor.Components.TerminalTab;
using Beutl.Editor.Components.TerminalTab.ViewModels;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

using Reactive.Bindings;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class TerminalTabViewModelTests
{
    [Test]
    public void ResolveShell_OnWindows_UsesComSpec()
    {
        (string path, string[] args) = TerminalTabViewModel.ResolveShell(
            name => name == "COMSPEC" ? @"C:\Windows\System32\cmd.exe" : null,
            isWindows: true,
            isMacOS: false);

        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo(@"C:\Windows\System32\cmd.exe"));
            Assert.That(args, Is.Empty);
        });
    }

    [Test]
    public void ResolveShell_OnWindows_FallsBackToCmdExe()
    {
        (string path, string[] args) = TerminalTabViewModel.ResolveShell(
            _ => null,
            isWindows: true,
            isMacOS: false);

        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo("cmd.exe"));
            Assert.That(args, Is.Empty);
        });
    }

    [Test]
    public void ResolveShell_OnUnix_UsesShellEnvironmentVariableAsLoginShell()
    {
        (string path, string[] args) = TerminalTabViewModel.ResolveShell(
            name => name == "SHELL" ? "/opt/homebrew/bin/fish" : null,
            isWindows: false,
            isMacOS: true);

        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo("/opt/homebrew/bin/fish"));
            Assert.That(args, Is.EqualTo(new[] { "-l" }));
        });
    }

    [TestCase(true, "/bin/zsh")]
    [TestCase(false, "/bin/bash")]
    public void ResolveShell_OnUnix_FallsBackToPlatformDefault(bool isMacOS, string expected)
    {
        (string path, string[] args) = TerminalTabViewModel.ResolveShell(
            _ => null,
            isWindows: false,
            isMacOS: isMacOS);

        Assert.Multiple(() =>
        {
            Assert.That(path, Is.EqualTo(expected));
            Assert.That(args, Is.EqualTo(new[] { "-l" }));
        });
    }

    [Test]
    public void ResolveLangFallback_OnWindows_ReturnsNull()
    {
        string? result = TerminalTabViewModel.ResolveLangFallback(
            _ => null, "ja-JP", isWindows: true);

        Assert.That(result, Is.Null);
    }

    [TestCase("LANG")]
    [TestCase("LC_ALL")]
    [TestCase("LC_CTYPE")]
    public void ResolveLangFallback_ReturnsNull_WhenLocaleIsAlreadyConfigured(string variable)
    {
        string? result = TerminalTabViewModel.ResolveLangFallback(
            name => name == variable ? "ja_JP.UTF-8" : null,
            "ja-JP",
            isWindows: false);

        Assert.That(result, Is.Null);
    }

    [TestCase("ja-JP", "ja_JP.UTF-8")]
    [TestCase("en-US", "en_US.UTF-8")]
    [TestCase("", "en_US.UTF-8")]
    public void ResolveLangFallback_BuildsUtf8Locale_WhenEnvironmentHasNoLocale(
        string cultureName, string expected)
    {
        string? result = TerminalTabViewModel.ResolveLangFallback(
            _ => null, cultureName, isWindows: false);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ResolveWorkingDirectory_PrefersProjectDirectory()
    {
        string projectDir = Path.Combine(Path.GetTempPath(), "beutl-terminal-test", "proj");
        string sceneDir = Path.Combine(projectDir, "scenes", "scene1");
        var scene = new Scene(640, 480, string.Empty)
        {
            Uri = new Uri(Path.Combine(sceneDir, "scene1.scene")),
        };
        var project = new Project
        {
            Uri = new Uri(Path.Combine(projectDir, "proj.bproj")),
        };
        project.Items.Add(scene);
        var editorContext = new TestEditorContext(scene);
        editorContext.AddService(scene);

        string? result = TerminalTabViewModel.ResolveWorkingDirectory(editorContext);

        Assert.That(result, Is.EqualTo(projectDir));
    }

    [Test]
    public void ResolveWorkingDirectory_FallsBackToSceneDirectory()
    {
        string sceneDir = Path.Combine(Path.GetTempPath(), "beutl-terminal-test", "scene-only");
        var scene = new Scene(640, 480, string.Empty)
        {
            Uri = new Uri(Path.Combine(sceneDir, "scene1.scene")),
        };
        var editorContext = new TestEditorContext(scene);
        editorContext.AddService(scene);

        string? result = TerminalTabViewModel.ResolveWorkingDirectory(editorContext);

        Assert.That(result, Is.EqualTo(sceneDir));
    }

    [Test]
    public void ResolveWorkingDirectory_ReturnsNull_WhenSceneIsUnavailable()
    {
        var scene = new Scene(640, 480, string.Empty);
        var editorContext = new TestEditorContext(scene);

        string? result = TerminalTabViewModel.ResolveWorkingDirectory(editorContext);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Dispose_RaisesDisposedOnce()
    {
        var scene = new Scene(640, 480, string.Empty);
        var editorContext = new TestEditorContext(scene);
        var viewModel = new TerminalTabViewModel(editorContext);
        int disposedCount = 0;
        viewModel.Disposed += (_, _) => disposedCount++;

        viewModel.Dispose();
        viewModel.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(disposedCount, Is.EqualTo(1));
            Assert.That(viewModel.Extension, Is.SameAs(TerminalTabExtension.Instance));
        });
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
