using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
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

using Iciclecreek.Terminal;

using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TerminalTabLifecycleTests
{
    private static Task ResetProjectAsync() => TestReset.ResetShellAsync();

    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();

        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }

    [AvaloniaTest]
    public async Task SwitchingToolTabs_ReusesTerminalViewAndShell()
    {
        await ResetProjectAsync();

        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("The Windows ConPTY path is not exercised by this headless test.");
        }

        EditViewModel editor = await OpenEditorForNewScene("terminal-tab-lifecycle");
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

        try
        {
            window.Show();
            editor.DockHost.Factory.SetActiveDockable(terminalDockable);
            HeadlessTestHelpers.Render();

            TerminalControl firstTerminal = FindTerminal(view);
            WaitUntil(() => firstTerminal.HasPtyConnection, "the terminal shell did not start");
            int pid = firstTerminal.Pid;

            editor.DockHost.Factory.SetActiveDockable(otherDockable);
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(firstTerminal.HasPtyConnection, Is.True);
                Assert.That(firstTerminal.Pid, Is.EqualTo(pid));
                Assert.That(terminalContext.IsProcessExited.Value, Is.False);
            });

            editor.DockHost.Factory.SetActiveDockable(terminalDockable);
            HeadlessTestHelpers.Render();

            TerminalControl secondTerminal = FindTerminal(view);
            Assert.Multiple(() =>
            {
                Assert.That(secondTerminal, Is.SameAs(firstTerminal));
                Assert.That(secondTerminal.HasPtyConnection, Is.True);
                Assert.That(secondTerminal.Pid, Is.EqualTo(pid));
                Assert.That(terminalContext.IsProcessExited.Value, Is.False);
            });
        }
        finally
        {
            editor.CloseToolTab(terminalContext);
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task SwitchingToolTabs_RecreatesNonPersistentToolContent()
    {
        await ResetProjectAsync();
        EditViewModel editor = await OpenEditorForNewScene("tool-tab-content-lifecycle");
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
    public async Task TerminalControl_ThemeResources_ResolvePerVariant()
    {
        await ResetProjectAsync();
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("The Windows ConPTY path is not exercised by this headless test.");
        }

        EditViewModel editor = await OpenEditorForNewScene("terminal-tab-theme-resources");
        IToolDock bottomDock = editor.DockHost.Factory.GetAnchoredDock(DockAnchor.Bottom)!;
        var terminalContext = new TerminalTabViewModel(editor);
        Assert.That(editor.DockHost.OpenToolTab(terminalContext, bottomDock), Is.True);
        var terminalDockable = editor.DockHost.Factory.EnumerateTools()
            .Single(item => ReferenceEquals(item.ToolContext, terminalContext));
        var view = new EditView { DataContext = editor };
        var window = new Window { Content = view, Width = 900, Height = 700 };

        if (Application.Current is not Application currentApp)
        {
            throw new InvalidOperationException("Application.Current is null");
        }

#pragma warning disable CS8600
        ThemeVariant originalVariant = currentApp.RequestedThemeVariant;
#pragma warning restore CS8600
        try
        {
            window.Show();
            editor.DockHost.Factory.SetActiveDockable(terminalDockable);
            HeadlessTestHelpers.Render();

            TerminalControl terminal = FindTerminal(view);

            Assert.That((terminal.Background as ISolidColorBrush)?.Color, Is.EqualTo(Colors.Transparent));

            Assert.That(terminal.Foreground, Is.Not.Null);

            currentApp.RequestedThemeVariant = ThemeVariant.Dark;
            HeadlessTestHelpers.Render();
            Color darkForeground = (terminal.Foreground as ISolidColorBrush)?.Color ?? default;

            currentApp.RequestedThemeVariant = ThemeVariant.Light;
            HeadlessTestHelpers.Render();
            Color lightForeground = (terminal.Foreground as ISolidColorBrush)?.Color ?? default;

            Assert.That(darkForeground, Is.Not.EqualTo(lightForeground),
                "terminal foreground should vary between Dark and Light theme variants");
        }
        finally
        {
            currentApp.RequestedThemeVariant = originalVariant;
            editor.CloseToolTab(terminalContext);
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static TerminalControl FindTerminal(EditView view)
    {
        return view.GetVisualDescendants()
            .OfType<TerminalTabView>()
            .Single()
            .FindControl<TerminalControl>("Terminal")!;
    }

    private static void WaitUntil(Func<bool> condition, string failureMessage, int timeoutMs = 5000)
    {
        for (int elapsed = 0; elapsed < timeoutMs; elapsed += 50)
        {
            HeadlessTestHelpers.Settle();
            if (condition())
            {
                return;
            }

            Thread.Sleep(50);
        }

        Assert.Fail(failureMessage);
    }

    private sealed class TransientToolContext : IToolContext
    {
        public ToolTabExtension Extension => TransientToolExtension.Instance;

        public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

        public string Header => "Transient";

        public void Dispose()
        {
            IsSelected.Dispose();
        }

        public object? GetService(Type serviceType) => null;

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
