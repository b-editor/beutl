using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Beutl.Api.Services;
using Beutl.Controls.Styling.Themes;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;
using FluentAvalonia.Styling;

namespace Beutl.HeadlessUITests;

// Produces PNG captures of the AI tool tabs that replaced the modal dialogs, so the non-modal
// layout can be reviewed without launching the desktop shell.
[TestFixture]
[Explicit("Produces PNG captures for manual design review; not a regression test.")]
public class AiToolTabCaptureTests
{
    private static string OutputDirectory =>
        Environment.GetEnvironmentVariable("BEUTL_THEME_CAPTURE_DIR")
        ?? Path.Combine(Path.GetTempPath(), "beutl-ai-captures");

    [AvaloniaTest]
    public async Task Capture_ai_tool_tabs_dark()
    {
        await TestReset.ResetShellAsync();
        UseCaptureTheme();
        EditViewModel editor = await OpenEditorForNewScene("ai-tooltab-capture");
        MainViewModel mainViewModel = TestShell.MainViewModel;

        Capture(
            new AiImageGenerationView
            {
                DataContext = mainViewModel.CreateAiImageGenerationToolViewModel(editor),
            },
            420,
            900,
            "ai-image-generation-tab.png");

        Capture(
            new AiImageEditView
            {
                DataContext = mainViewModel.CreateAiImageEditToolViewModel(editor),
            },
            420,
            900,
            "ai-image-edit-tab.png");

        Capture(
            new AiSubtitleView
            {
                DataContext = mainViewModel.CreateAiSubtitleToolViewModel(editor),
            },
            460,
            900,
            "ai-subtitle-tab.png");

        Capture(
            new AiVideoGenerationView
            {
                DataContext = mainViewModel.CreateAiVideoGenerationToolViewModel(editor),
            },
            460,
            900,
            "ai-video-generation-tab.png");
    }

    [AvaloniaTest]
    public async Task Capture_ai_workspace_pages_dark()
    {
        await TestReset.ResetShellAsync();
        UseCaptureTheme();
        EditViewModel editor = await OpenEditorForNewScene("ai-workspace-capture");
        AiWorkspaceViewModel workspace = TestShell.MainViewModel.CreateAiWorkspaceViewModel(editor);

        foreach (AiWorkspaceSectionViewModel section in workspace.Sections)
        {
            workspace.Show(section.Id);
            HeadlessTestHelpers.Settle();
            Capture(
                new AiWorkspaceView { DataContext = workspace },
                380,
                900,
                $"ai-workspace-{section.Id.ToString().ToLowerInvariant()}.png");
        }

        await workspace.DisposeAsync();
    }

    [AvaloniaTest]
    public async Task Capture_ai_job_center_inline_confirmation_dark()
    {
        await TestReset.ResetShellAsync();
        UseCaptureTheme();
        EditViewModel editor = await OpenEditorForNewScene("ai-jobcenter-capture");
        AiJobCenterViewModel viewModel = TestShell.MainViewModel.CreateAiJobCenterViewModel(editor);
        viewModel.ApplySnapshot(new AiJobMonitorSnapshot(
            [
                CreateJob("job-1", "image", "succeeded", """{ "prompt": "A moonlit lake", "size": "1024x1024" }""", "https://beutl.beditor.net/api/contents/file-1"),
                CreateJob("job-2", "video", "running", """{ "prompt": "Slow orbit around a statue", "durationSeconds": 6, "resolution": "1080p" }"""),
                CreateJob("job-3", "image", "failed", """{ "prompt": "Neon alley in the rain" }""", error: "aiProviderError", canRetry: true),
            ],
            NextCursor: null,
            IsLoading: false,
            Error: null));
        HeadlessTestHelpers.Settle();

        Capture(new AiJobCenterView { DataContext = viewModel }, 380, 760, "ai-job-center-tab.png");

        viewModel.RequestDeleteConfirmation(viewModel.Jobs.Single(job => job.Id == "job-3"));
        HeadlessTestHelpers.Settle();
        Capture(
            new AiJobCenterView { DataContext = viewModel },
            380,
            760,
            "ai-job-center-inline-confirmation.png");
    }

    private static AiJob CreateJob(
        string id,
        string kind,
        string status,
        string inputJson,
        string? url = null,
        string? error = null,
        bool canRetry = false)
    {
        using JsonDocument document = JsonDocument.Parse(inputJson);
        return new AiJob(
            new AiJobId(id),
            new AiJobKindId(kind),
            new AiJobStatusId(status),
            document.RootElement.Clone(),
            url is null ? null : new AiContentId($"{id}-file"),
            url is null ? null : new Uri(url),
            error,
            canRetry,
            new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 10, 9, 1, 0, TimeSpan.Zero));
    }

    private static void UseCaptureTheme()
    {
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        Application.Current.Styles.OfType<FluentAvaloniaTheme>().Single().CustomAccentColor =
            BeutlDarkBorderTheme.AccentColor;
    }

    private static void Capture(Control content, int width, int height, string name)
    {
        var window = new Window { Content = content, Width = width, Height = height };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);

            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null, "Headless frame capture returned null.");

            Directory.CreateDirectory(OutputDirectory);
            string path = Path.Combine(OutputDirectory, name);
            frame!.Save(path);
            TestContext.Out.WriteLine($"Saved capture: {path}");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(workspace);
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, workspace))!;
        HeadlessTestHelpers.Settle();
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value!;
    }
}
