using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Beutl.ViewModels.Tools;
using Beutl.Views;
using Beutl.Views.Tools;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiShellEntryPointTests
{
    [AvaloniaTest]
    public async Task AiMenu_KeepsEveryWorkflowInOneTabAndTurnsItToTheRightPage()
    {
        await TestReset.ResetShellAsync();
        MainViewModel mainViewModel = TestShell.MainViewModel;
        EditViewModel editor = await OpenEditor("ai-shell-entry-points");
        var mainView = new MainView { DataContext = mainViewModel };
        try
        {
            string[] aiHeaders =
            [
                Strings.Ai,
                Strings.AiJobCenter,
                Strings.AiImageGeneration,
                Strings.AiImageEdit,
                Strings.AiSubtitle,
                Strings.AiVideoGeneration,
            ];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    mainViewModel.ToolTabExtensions.Where(ext => aiHeaders.Contains(ext.Header)),
                    Is.EqualTo(new[] { AiWorkspaceTabExtension.Instance }),
                    "The tab list must offer one AI entry, not one per workflow.");
                Assert.That(mainViewModel.MenuBar.ShowAiJobs.CanExecute(), Is.True);
                Assert.That(mainViewModel.MenuBar.GenerateImage.CanExecute(), Is.True);
                Assert.That(mainViewModel.MenuBar.EditImage.CanExecute(), Is.True);
                Assert.That(mainViewModel.MenuBar.GenerateSubtitles.CanExecute(), Is.True);
                Assert.That(mainViewModel.MenuBar.GenerateVideo.CanExecute(), Is.True);
            }

            AiWorkspaceViewModel? workspace = null;
            foreach ((Action open, AiWorkspaceSection section, Type pageType) in MenuEntries(mainViewModel))
            {
                open();
                HeadlessTestHelpers.Settle();

                AiWorkspaceViewModel? current = editor.FindToolTab<AiWorkspaceViewModel>();

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(current, Is.Not.Null);
                    Assert.That(
                        CountAiTabs(editor),
                        Is.EqualTo(1),
                        "The menu turns the open tab, it never stacks another.");
                    Assert.That(current!.SelectedSection.Value?.Id, Is.EqualTo(section));
                    Assert.That(current.ActiveContent.Value, Is.InstanceOf(pageType));
                    Assert.That(current.Header.Value, Is.EqualTo(current.SelectedSection.Value!.DisplayName));
                }

                workspace ??= current;
                Assert.That(current, Is.SameAs(workspace));
            }

            Assert.That(new AiWorkspaceView(), Is.InstanceOf<UserControl>());
        }
        finally
        {
            mainView.DataContext = null;
        }
    }

    [AvaloniaTest]
    public async Task AiMenu_TurnsTheTabAlreadyOnThatPageRatherThanAnother()
    {
        await TestReset.ResetShellAsync();
        MainViewModel mainViewModel = TestShell.MainViewModel;
        EditViewModel editor = await OpenEditor("ai-two-tabs");
        var mainView = new MainView { DataContext = mainViewModel };
        try
        {
            Assert.That(AiWorkspaceTabExtension.Instance.CanMultiple, Is.True);

            mainViewModel.MenuBar.GenerateImage.Execute();
            HeadlessTestHelpers.Settle();
            AiWorkspaceViewModel first = editor.FindToolTab<AiWorkspaceViewModel>()!;

            AiWorkspaceViewModel second = mainViewModel.CreateAiWorkspaceViewModel(editor);
            Assert.That(editor.OpenToolTab(second), Is.True);
            HeadlessTestHelpers.Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(CountAiTabs(editor), Is.EqualTo(2));
                Assert.That(
                    second.SelectedSection.Value?.Id,
                    Is.EqualTo(AiWorkspaceSection.ImageEdit),
                    "A tab added beside another starts on a page that one is not showing.");
            }

            second.Show(AiWorkspaceSection.Jobs);
            mainViewModel.MenuBar.ShowAiJobs.Execute();
            HeadlessTestHelpers.Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    CountAiTabs(editor),
                    Is.EqualTo(2),
                    "Asking for a page that is already open must not add a third tab.");
                Assert.That(second.SelectedSection.Value?.Id, Is.EqualTo(AiWorkspaceSection.Jobs));
                Assert.That(
                    first.SelectedSection.Value?.Id,
                    Is.EqualTo(AiWorkspaceSection.ImageGeneration),
                    "The tab already on the page answers, so the other one keeps its own.");
            }
        }
        finally
        {
            mainView.DataContext = null;
        }
    }

    [AvaloniaTest]
    public async Task Workspace_BuildsAPageTheFirstTimeItIsShownAndThenKeepsIt()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-workspace-lazy");
        var built = new List<AiWorkspaceSection>();
        using var workspace = new AiWorkspaceViewModel(editor, CreatePages(built));

        object firstLook = workspace.Show(AiWorkspaceSection.Subtitles);
        workspace.Show(AiWorkspaceSection.Jobs);
        object secondLook = workspace.Show(AiWorkspaceSection.Subtitles);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                built,
                Is.EqualTo(new[]
                {
                    AiWorkspaceSection.ImageGeneration,
                    AiWorkspaceSection.Subtitles,
                    AiWorkspaceSection.Jobs,
                }),
                "A page nobody opened must not be built, and none must be built twice.");
            Assert.That(
                secondLook,
                Is.SameAs(firstLook),
                "Coming back to a page must find the work left on it.");
        }
    }

    [AvaloniaTest]
    public async Task Workspace_KeepsEachTabsWorkToItself()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-workspace-independent");
        var built = new List<AiWorkspaceSection>();
        using var left = new AiWorkspaceViewModel(editor, CreatePages(built));
        using var right = new AiWorkspaceViewModel(editor, CreatePages(built));

        object fromLeft = left.Show(AiWorkspaceSection.Subtitles);
        object fromRight = right.Show(AiWorkspaceSection.Subtitles);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                fromRight,
                Is.Not.SameAs(fromLeft),
                "A second tab is a second workbench, so what is typed in one stays there.");
            Assert.That(built.Count(section => section == AiWorkspaceSection.Subtitles), Is.EqualTo(2));
        }
    }

    [AvaloniaTest]
    public async Task Workspace_ClosingATabClosesItsOwnPagesAndOnlyThose()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-workspace-dispose");
        using var kept = new AiWorkspaceViewModel(editor, CreatePages([]));
        var closed = new AiWorkspaceViewModel(editor, CreatePages([]));
        var keptPage = (StubPage)kept.Show(AiWorkspaceSection.Jobs);
        var closedPage = (StubPage)closed.Show(AiWorkspaceSection.Jobs);

        closed.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(closedPage.IsDisposed, Is.True);
            Assert.That(keptPage.IsDisposed, Is.False, "The tab still open keeps the work on its own page.");
        }
    }

    [AvaloniaTest]
    public async Task Workspace_ReopensOnThePageTheLayoutWasSavedOn()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("ai-workspace-layout");
        var saved = new JsonObject();

        using (var workspace = new AiWorkspaceViewModel(editor, CreatePages([])))
        {
            workspace.Show(AiWorkspaceSection.VideoGeneration);
            workspace.WriteToJson(saved);
        }

        var restoredPages = new List<AiWorkspaceSection>();
        using var restored = new AiWorkspaceViewModel(editor, CreatePages(restoredPages));
        restored.ReadFromJson(saved);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored.SelectedSection.Value?.Id, Is.EqualTo(AiWorkspaceSection.VideoGeneration));
            Assert.That(
                restoredPages,
                Does.Not.Contain(AiWorkspaceSection.Subtitles),
                "Restoring a layout must not build the pages it was not saved on.");
        }
    }

    [AvaloniaTest]
    public async Task AiMenu_WithoutScene_OnlyKeepsJobHistoryEnabled()
    {
        await TestReset.ResetShellAsync();
        MainViewModel mainViewModel = TestShell.MainViewModel;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mainViewModel.MenuBar.ShowAiJobs.CanExecute(), Is.True);
            Assert.That(mainViewModel.MenuBar.GenerateImage.CanExecute(), Is.False);
            Assert.That(mainViewModel.MenuBar.EditImage.CanExecute(), Is.False);
            Assert.That(mainViewModel.MenuBar.GenerateSubtitles.CanExecute(), Is.False);
            Assert.That(mainViewModel.MenuBar.GenerateVideo.CanExecute(), Is.False);
        }
    }

    private static (Action Open, AiWorkspaceSection Section, Type PageType)[] MenuEntries(
        MainViewModel mainViewModel) =>
    [
        (mainViewModel.MenuBar.GenerateImage.Execute,
            AiWorkspaceSection.ImageGeneration,
            typeof(AiImageGenerationDialogViewModel)),
        (mainViewModel.MenuBar.EditImage.Execute,
            AiWorkspaceSection.ImageEdit,
            typeof(AiImageEditDialogViewModel)),
        (mainViewModel.MenuBar.GenerateVideo.Execute,
            AiWorkspaceSection.VideoGeneration,
            typeof(AiVideoGenerationDialogViewModel)),
        (() => mainViewModel.MenuBar.GenerateSubtitles.Execute(),
            AiWorkspaceSection.Subtitles,
            typeof(AiSubtitleDialogViewModel)),
        (mainViewModel.MenuBar.ShowAiJobs.Execute,
            AiWorkspaceSection.Jobs,
            typeof(AiJobCenterViewModel)),
    ];

    private static int CountAiTabs(EditViewModel editor)
        => editor.DockHost.Factory.EnumerateTools().Count(tool => tool.ToolContext is AiWorkspaceViewModel);

    private static Func<AiWorkspaceSection, IDisposable> CreatePages(List<AiWorkspaceSection> built)
        => section =>
        {
            built.Add(section);
            return new StubPage();
        };

    private sealed class StubPage : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    private static async Task<EditViewModel> OpenEditor(string name)
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(workspace);
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, workspace))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
    }
}
