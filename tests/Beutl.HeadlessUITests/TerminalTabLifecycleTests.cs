using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.TerminalTab.ViewModels;
using Beutl.Editor.Components.TerminalTab.Views;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Dock.Model.Controls;
using Moq;
using Reactive.Bindings;
using RoyalTerminal.Avalonia.Controls;
using RoyalTerminal.Terminal;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TerminalTabLifecycleTests
{
    [AvaloniaTest]
    public async Task SwitchingToolTabs_ReusesTerminalViewAndShell()
    {
        await TestReset.ResetShellAsync();

        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("The Windows ConPTY path is not exercised by this headless test.");
        }

        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("terminal-tab-lifecycle");
        IToolDock bottomDock = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Bottom)!;
        var terminalContext = new TerminalTabViewModel(editor);
        Assert.That(editor.DockHost.OpenToolTab(terminalContext, bottomDock), Is.True);

        var terminalDockable = editor.DockHost.Factory.EnumerateTools()
            .Single(item => ReferenceEquals(item.ToolContext, terminalContext));
        BeutlToolDockable otherDockable = bottomDock.VisibleDockables!
            .OfType<BeutlToolDockable>()
            .First(item => !ReferenceEquals(item, terminalDockable));
        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        TerminalControl firstTerminal;
        try
        {
            window.Show();
            editor.DockHost.Factory.SetActiveDockable(terminalDockable);
            HeadlessTestHelpers.Render();

            firstTerminal = FindTerminal(view);
            await HeadlessTestHelpers.WaitUntilAsync(
                () => firstTerminal.HasActiveSession, TimeSpan.FromSeconds(5), "the terminal shell did not start");
            int pid = firstTerminal.Pty!.ChildPid;

            editor.DockHost.Factory.SetActiveDockable(otherDockable);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(firstTerminal.HasActiveSession, Is.True);
                Assert.That(firstTerminal.Pty!.ChildPid, Is.EqualTo(pid));
                Assert.That(terminalContext.IsProcessExited.Value, Is.False);
            });

            editor.DockHost.Factory.SetActiveDockable(terminalDockable);
            HeadlessTestHelpers.Render();

            TerminalControl secondTerminal = FindTerminal(view);
            Assert.Multiple(() =>
            {
                Assert.That(secondTerminal, Is.SameAs(firstTerminal));
                Assert.That(secondTerminal.HasActiveSession, Is.True);
                Assert.That(secondTerminal.Pty!.ChildPid, Is.EqualTo(pid));
                Assert.That(terminalContext.IsProcessExited.Value, Is.False);
            });

            // A hidden tab still receives the shell's final OSC title and exit notification.
            editor.DockHost.Factory.SetActiveDockable(otherDockable);
            HeadlessTestHelpers.Render();
            firstTerminal.SendInput("exec /bin/sh -c 'printf \"\\033]2;finished\\007\"; read reply'\r");
            await HeadlessTestHelpers.WaitUntilAsync(() => terminalContext.TerminalTitle.Value == "finished");
            firstTerminal.SendInput("\r");
            await HeadlessTestHelpers.WaitUntilAsync(() => terminalContext.IsProcessExited.Value);

            editor.DockHost.Factory.SetActiveDockable(terminalDockable);
            HeadlessTestHelpers.Render();
            var restart = terminalDockable.ToolContent!.FindControl<Button>("RestartButton")!;
            Assert.That(restart.IsEffectivelyVisible, Is.True);
            HeadlessTestHelpers.Click(restart);
            await HeadlessTestHelpers.WaitUntilAsync(() => firstTerminal.HasActiveSession);
            Assert.That(firstTerminal.Pty!.ChildPid, Is.Not.EqualTo(pid));
            Assert.That(terminalContext.IsProcessExited.Value, Is.False);
        }
        finally
        {
            editor.CloseToolTab(terminalContext);
            window.Close();
            HeadlessTestHelpers.Settle();
        }

        Assert.That(firstTerminal.HasPty, Is.False);
    }

    [AvaloniaTest]
    public async Task FailedLaunch_CanRestartInTheSceneDirectory()
    {
        var root = Directory.CreateTempSubdirectory("beutl-terminal-");
        try
        {
            string directory = Path.Combine(root.FullName, "scene folder");
            var scene = new Scene(640, 480, string.Empty) { Uri = new Uri(Path.Combine(directory, "test.scene")) };
            var editor = TestShell.CreateEditorContextMock();
            editor.Setup(x => x.GetService(typeof(Beutl.Configuration.GlobalConfiguration))).Returns(TestShell.Configuration);
            editor.Setup(x => x.GetService(typeof(Beutl.Media.FontManager))).Returns(TestShell.FontManager);
            editor.Setup(x => x.GetService(typeof(Beutl.Media.Decoding.DecoderRegistry))).Returns(TestShell.Decoders);
            editor.Setup(x => x.GetService(typeof(Beutl.Services.INotificationService))).Returns(TestShell.Notifications);
            editor.Setup(x => x.GetService(typeof(Scene))).Returns(scene);
            using var owner = new TerminalTabViewModel(editor.Object);
            using var view = new TerminalTabView { DataContext = owner };
            using var host = new ControlHost(view);
            var terminal = view.FindControl<TerminalControl>("Terminal")!;
            await HeadlessTestHelpers.WaitUntilAsync(() => owner.IsProcessExited.Value);
            Assert.That(terminal.HasPty, Is.False);

            Directory.CreateDirectory(directory);
            const string marker = "beutl-cwd-ok";
            File.WriteAllText(Path.Combine(directory, "marker.txt"), marker);
            HeadlessTestHelpers.Click(view.FindControl<Button>("RestartButton")!);
            await HeadlessTestHelpers.WaitUntilAsync(() => terminal.HasActiveSession);
            terminal.SendInput(OperatingSystem.IsWindows() ? "type marker.txt\r" : "cat marker.txt\r");
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                terminal.TryExportSnapshot(TerminalSnapshotExportFormat.PlainText, default, out string text)
                && text.Contains(marker, StringComparison.Ordinal));
            Assert.That(owner.IsProcessExited.Value, Is.False);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [AvaloniaTest]
    public async Task SwitchingToolTabs_RecreatesNonPersistentToolContent()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("tool-tab-content-lifecycle");
        IToolDock bottomDock = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Bottom)!;
        var transientContext = new TransientToolContext();
        Assert.That(editor.DockHost.OpenToolTab(transientContext, bottomDock), Is.True);
        BeutlToolDockable timelineDockable = editor.DockHost.Factory.EnumerateTools()
            .Single(item => ReferenceEquals(item.ToolContext.Extension, TimelineTabExtension.Instance));
        BeutlToolDockable transientDockable = editor.DockHost.Factory.EnumerateTools()
            .Single(item => ReferenceEquals(item.ToolContext, transientContext));
        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        try
        {
            window.Show();
            editor.DockHost.Factory.SetActiveDockable(timelineDockable);
            HeadlessTestHelpers.Render();
            TimelineTabView firstTimeline = view.GetVisualDescendants()
                .OfType<TimelineTabView>()
                .Single();

            editor.DockHost.Factory.SetActiveDockable(transientDockable);
            HeadlessTestHelpers.Render();
            editor.DockHost.Factory.SetActiveDockable(timelineDockable);
            HeadlessTestHelpers.Render();

            TimelineTabView secondTimeline = view.GetVisualDescendants()
                .OfType<TimelineTabView>()
                .Single();
            Assert.That(secondTimeline, Is.Not.SameAs(firstTimeline));
        }
        finally
        {
            editor.CloseToolTab(transientContext);
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public void TerminalControl_ThemeResources_ResolvePerVariant()
    {
        using var view = new TerminalTabView();
        var panel = new Border { Padding = new Thickness(12, 0, 0, 0), Child = view };
        var window = new Window { Content = panel, Width = 480, Height = 160 };
        panel.Bind(Border.BackgroundProperty, panel.GetResourceObservable("DockSurfacePanelBrush"));
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("DockSurfaceWorkbenchBrush"));
        using var host = new ControlHost(window);
        var terminal = view.FindControl<TerminalControl>("Terminal")!;
        terminal.WriteOutput("$ echo 日本語\r\n日本語\r\n"u8);

        foreach (ThemeVariant variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            using var theme = ThemeVariantScope.Use(variant);
            HeadlessTestHelpers.Render();
            Assert.That(terminal.DefaultForeground.R, variant == ThemeVariant.Dark
                ? Is.GreaterThan(terminal.DefaultBackground.R)
                : Is.LessThan(terminal.DefaultBackground.R));
            Assert.That(terminal.Theme!.CursorColor, Is.EqualTo(terminal.DefaultForeground.ToUInt32()));
            using var frame = window.CaptureRenderedFrame()!;
            var dockBackground = HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(6, 100));
            var terminalBackground = HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(240, 100));
            Assert.That(terminalBackground, Is.EqualTo(dockBackground), $"The terminal should match the surrounding {variant} dock panel");
            HeadlessCapture.SaveIfRequested(view, nameof(TerminalTabLifecycleTests), variant.ToString());
        }
    }

    private static TerminalControl FindTerminal(EditView view)
    {
        return view.GetVisualDescendants()
            .OfType<TerminalTabView>()
            .Single()
            .FindControl<TerminalControl>("Terminal")!;
    }

    private sealed class TransientToolContext : IToolContext
    {
        public ToolTabExtension Extension => TransientToolExtension.Instance;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

        public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>("Transient");

        public void Dispose()
        {
            IsSelected.Dispose();
        }

        public object? GetService(Type serviceType) => serviceType == typeof(Beutl.Services.INotificationService) ? TestShell.Notifications : serviceType == typeof(Beutl.Media.FontManager) ? TestShell.FontManager : serviceType == typeof(Beutl.Media.Decoding.DecoderRegistry) ? TestShell.Decoders : serviceType == typeof(Beutl.Configuration.GlobalConfiguration) ? TestShell.Configuration : null;

        public void ReadFromJson(JsonObject json)
        {
        }

        public void WriteToJson(JsonObject json)
        {
        }
    }

    private sealed class TransientToolExtension : ToolTabExtension
    {
        public static readonly TransientToolExtension Instance = new();

        public override bool CanMultiple => false;

        public override bool TryCreateContent(
            IEditorContext editorContext,
            [NotNullWhen(true)] out Control? control)
        {
            control = new Border();
            return true;
        }

        public override bool TryCreateContext(
            IEditorContext editorContext,
            [NotNullWhen(true)] out IToolContext? context)
        {
            context = new TransientToolContext();
            return true;
        }
    }
}
