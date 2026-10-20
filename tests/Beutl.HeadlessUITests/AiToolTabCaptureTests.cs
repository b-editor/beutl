using System.Globalization;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Beutl.Api.Services;
using Beutl.Controls.Styling.Themes;
using Beutl.Language;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Tools;
using Beutl.Views.Tools;

namespace Beutl.HeadlessUITests;

// Produces PNG captures of the AI workspace pages for manual design review.
[TestFixture]
[Explicit("Produces PNG captures for manual design review; not a regression test.")]
public class AiToolTabCaptureTests
{
    [AvaloniaTest]
    public async Task Capture_ai_workspace_pages_all_locales_dark()
    {
        ThemeCaptureTests.UseCaptureTheme(ThemeVariant.Dark, BeutlDarkBorderTheme.AccentColor);
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-workspace-capture");
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (CultureInfo culture in LocalizeService.Instance.SupportedCultures())
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                AiWorkspaceViewModel workspace = TestShell.MainViewModel.CreateAiWorkspaceViewModel(editor);
                try
                {
                    foreach (AiWorkspaceSectionViewModel section in workspace.Sections)
                    {
                        workspace.Show(section.Id);
                        HeadlessTestHelpers.Settle();
                        Capture(
                            new AiWorkspaceView { DataContext = workspace },
                            380,
                            900,
                            $"ai-workspace-{culture.Name}-{section.Id.ToString().ToLowerInvariant()}.png");
                    }
                }
                finally
                {
                    await workspace.DisposeAsync();
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [AvaloniaTest]
    public async Task Capture_ai_job_center_inline_confirmation_dark()
    {
        ThemeCaptureTests.UseCaptureTheme(ThemeVariant.Dark, BeutlDarkBorderTheme.AccentColor);
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-jobcenter-capture");
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

    private static void Capture(Control content, int width, int height, string name)
    {
        var window = new Window { Content = content, Width = width, Height = height };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(5);
            ThemeCaptureTests.Capture(window, name);
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
