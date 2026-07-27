using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;

using Beutl.Editor.Components.TerminalTab.ViewModels;
using Beutl.Editor.Components.TerminalTab.Views;
using Beutl.Extensibility;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;

using Dock.Model.Controls;

using Iciclecreek.Terminal;

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
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("The Windows ConPTY path is not exercised by this headless test.");
        }

        await ResetProjectAsync();
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
}
