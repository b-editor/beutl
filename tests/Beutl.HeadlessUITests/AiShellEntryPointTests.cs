using Avalonia.Headless.NUnit;
using Avalonia.Controls;
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
    public async Task AiMenu_OpensNonModalToolsAndReusesEachTab()
    {
        await TestReset.ResetShellAsync();
        MainViewModel mainViewModel = TestShell.MainViewModel;
        EditViewModel editor = await OpenEditor("ai-shell-entry-points");
        var mainView = new MainView { DataContext = mainViewModel };
        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(mainViewModel.ToolTabExtensions, Does.Contain(AiJobCenterTabExtension.Instance));
                Assert.That(mainViewModel.ToolTabExtensions, Does.Contain(AiImageGenerationTabExtension.Instance));
                Assert.That(mainViewModel.ToolTabExtensions, Does.Contain(AiImageEditTabExtension.Instance));
                Assert.That(mainViewModel.ToolTabExtensions, Does.Contain(AiSubtitleTabExtension.Instance));
                Assert.That(mainViewModel.ToolTabExtensions, Does.Contain(AiVideoGenerationTabExtension.Instance));
                Assert.That(AiJobCenterTabExtension.Instance.CanMultiple, Is.False);
                Assert.That(AiImageGenerationTabExtension.Instance.CanMultiple, Is.False);
                Assert.That(AiImageEditTabExtension.Instance.CanMultiple, Is.False);
                Assert.That(AiSubtitleTabExtension.Instance.CanMultiple, Is.False);
                Assert.That(AiVideoGenerationTabExtension.Instance.CanMultiple, Is.False);
                Assert.That(mainViewModel.MenuBar.ShowAiJobs.CanExecute(), Is.True);
                Assert.That(mainViewModel.MenuBar.GenerateImage.CanExecute(), Is.True);
                Assert.That(mainViewModel.MenuBar.EditImage.CanExecute(), Is.True);
                Assert.That(mainViewModel.MenuBar.GenerateSubtitles.CanExecute(), Is.True);
                Assert.That(mainViewModel.MenuBar.GenerateVideo.CanExecute(), Is.True);
            }

            mainViewModel.MenuBar.ShowAiJobs.Execute();
            HeadlessTestHelpers.Settle();
            AiJobCenterViewModel? first = editor.FindToolTab<AiJobCenterViewModel>();

            mainViewModel.MenuBar.ShowAiJobs.Execute();
            HeadlessTestHelpers.Settle();
            AiJobCenterViewModel? second = editor.FindToolTab<AiJobCenterViewModel>();
            int tabCount = editor.DockHost.Factory.EnumerateTools()
                .Count(tool => tool.ToolContext is AiJobCenterViewModel);

            Assert.Multiple(() =>
            {
                Assert.That(first, Is.Not.Null);
                Assert.That(second, Is.SameAs(first));
                Assert.That(tabCount, Is.EqualTo(1));
            });

            mainViewModel.MenuBar.GenerateImage.Execute();
            HeadlessTestHelpers.Settle();
            AiImageGenerationDialogViewModel? generatedImage =
                editor.FindToolTab<AiImageGenerationDialogViewModel>();
            mainViewModel.MenuBar.GenerateImage.Execute();

            mainViewModel.MenuBar.EditImage.Execute();
            HeadlessTestHelpers.Settle();
            AiImageEditDialogViewModel? editedImage = editor.FindToolTab<AiImageEditDialogViewModel>();
            mainViewModel.MenuBar.EditImage.Execute();

            mainViewModel.MenuBar.GenerateSubtitles.Execute();
            HeadlessTestHelpers.Settle();
            AiSubtitleDialogViewModel? subtitles = editor.FindToolTab<AiSubtitleDialogViewModel>();
            mainViewModel.MenuBar.GenerateSubtitles.Execute();

            mainViewModel.MenuBar.GenerateVideo.Execute();
            HeadlessTestHelpers.Settle();
            AiVideoGenerationDialogViewModel? video = editor.FindToolTab<AiVideoGenerationDialogViewModel>();
            mainViewModel.MenuBar.GenerateVideo.Execute();
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(generatedImage, Is.SameAs(editor.FindToolTab<AiImageGenerationDialogViewModel>()));
                Assert.That(editedImage, Is.SameAs(editor.FindToolTab<AiImageEditDialogViewModel>()));
                Assert.That(subtitles, Is.SameAs(editor.FindToolTab<AiSubtitleDialogViewModel>()));
                Assert.That(video, Is.SameAs(editor.FindToolTab<AiVideoGenerationDialogViewModel>()));
                Assert.That(new AiImageGenerationView(), Is.InstanceOf<UserControl>());
                Assert.That(new AiImageEditView(), Is.InstanceOf<UserControl>());
                Assert.That(new AiSubtitleView(), Is.InstanceOf<UserControl>());
                Assert.That(new AiVideoGenerationView(), Is.InstanceOf<UserControl>());
            });
        }
        finally
        {
            mainView.DataContext = null;
        }
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
