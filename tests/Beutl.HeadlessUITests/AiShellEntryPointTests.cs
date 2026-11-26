using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.AiWorkspaceTab;
using Beutl.Editor.Components.AiWorkspaceTab.ViewModels;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AiShellEntryPointTests
{
    [AvaloniaTest]
    public async Task CommandPalette_OpensEveryAiPageAndReusesTheTab()
    {
        MainViewModel mainViewModel = TestShell.MainViewModel;
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-shell-entry-points");
        CommandPaletteViewModel palette = mainViewModel.CommandPalette;
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
                Strings.AiVideoEditing,
            ];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    mainViewModel.ToolTabExtensions.Where(ext => aiHeaders.Contains(ext.Header)),
                    Is.EqualTo(new[] { AiWorkspaceTabExtension.Instance }),
                    "The tab list must offer one AI entry, not one per workflow.");
                Assert.That(mainViewModel.MenuItems.Any(item => item.Header == Strings.Ai), Is.False);
                Assert.That(AiToolTabEntry(mainViewModel).CommandParameter, Is.SameAs(AiWorkspaceTabExtension.Instance));
            }

            AiWorkspaceViewModel? workspace = null;
            foreach ((string id, AiWorkspaceSection section, Type pageType) in s_aiPages)
            {
                palette.Open();
                CommandPaletteItemViewModel command = palette.FilteredCommands.Single(item => item.Command.Id == id);
                Assert.That(command.IsEnabled, Is.True, id);
                palette.SelectedCommand.Value = command;
                await palette.ExecuteSelectedAsync();
                HeadlessTestHelpers.Settle();

                AiWorkspaceViewModel? current = editor.FindToolTab<AiWorkspaceViewModel>();

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(current, Is.Not.Null);
                    Assert.That(
                        CountAiTabs(editor),
                        Is.EqualTo(1),
                        "The command reuses the open tab.");
                    Assert.That(current!.SelectedSection.Value?.Id, Is.EqualTo(section));
                    Assert.That(current.ActiveContent.Value, Is.InstanceOf(pageType));
                    Assert.That(current.Header.Value, Is.EqualTo(current.SelectedSection.Value!.DisplayName));
                }

                workspace ??= current;
                Assert.That(current, Is.SameAs(workspace));
            }
        }
        finally
        {
            palette.Close();
        }
    }

    [AvaloniaTest]
    public async Task CommandPalette_DisablesAiPagesWhileTheEditorIsSuspended()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-palette-suspension");
        MainViewModel main = TestShell.MainViewModel;
        CommandPaletteViewModel palette = main.CommandPalette;
        palette.Open();
        try
        {
            Assert.That(AiPagesAreEnabled(true), Is.True);
            using (TestShell.Editor.SuspendEditors())
            {
                await CommandPaletteTestHost.UntilAsync(() => AiPagesAreEnabled(false));
                foreach (var page in s_aiPages)
                    await MainViewExtension.Instance.RunAsync(main, new ContextCommandExecution(page.Id));
                Assert.That(CountAiTabs(editor), Is.Zero);
            }

            await CommandPaletteTestHost.UntilAsync(() => AiPagesAreEnabled(true));
        }
        finally
        {
            palette.Close();
        }

        bool AiPagesAreEnabled(bool enabled) => s_aiPages.All(page =>
            palette.FilteredCommands.Single(item => item.Command.Id == page.Id).IsEnabled == enabled);
    }

    [AvaloniaTest]
    public async Task WorkspaceSectionNames_AreLocalizedForEverySupportedCulture()
    {
        MainViewModel mainViewModel = TestShell.MainViewModel;
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-workspace-localized-section-names");
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (CultureInfo culture in LocalizeService.Instance.SupportedCultures())
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                await using AiWorkspaceViewModel workspace = mainViewModel.CreateAiWorkspaceViewModel(editor);

                string[] expected = culture.Name switch
                {
                    "en-US" =>
                    [
                        "Image generation",
                        "Image editing",
                        "Video generation",
                        "Video editing",
                        "Subtitles",
                        "Jobs",
                    ],
                    "ja-JP" =>
                    [
                        "画像生成",
                        "画像編集",
                        "動画生成",
                        "動画編集",
                        "字幕",
                        "ジョブ",
                    ],
                    "es" =>
                    [
                        "Generación de imágenes",
                        "Edición de imágenes",
                        "Generación de vídeo",
                        "Edición de vídeo",
                        "Subtítulos",
                        "Trabajos",
                    ],
                    "ko-KR" =>
                    [
                        "이미지 생성",
                        "이미지 편집",
                        "동영상 생성",
                        "동영상 편집",
                        "자막",
                        "작업",
                    ],
                    "zh-CN" =>
                    [
                        "图像生成",
                        "图像编辑",
                        "视频生成",
                        "视频编辑",
                        "字幕",
                        "任务",
                    ],
                    _ => throw new InvalidOperationException($"Unexpected supported culture: {culture.Name}")
                };
                string[] actual = workspace.Sections.Select(section => section.DisplayName).ToArray();

                Assert.That(actual, Is.EqualTo(expected), $"AI workspace section labels for {culture.Name}");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [AvaloniaTest]
    public async Task JobCenter_ActivatesTheTabAlreadyOnItsPage()
    {
        MainViewModel mainViewModel = TestShell.MainViewModel;
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-two-tabs");
        Assert.That(AiWorkspaceTabExtension.Instance.CanMultiple, Is.True);

        MainMenuItem aiTab = AiToolTabEntry(mainViewModel);
        aiTab.Command!.Execute(aiTab.CommandParameter);
        HeadlessTestHelpers.Settle();
        AiWorkspaceViewModel first = editor.FindToolTab<AiWorkspaceViewModel>()!;

        AiWorkspaceViewModel second = mainViewModel.CreateAiWorkspaceViewModel(editor);
        Assert.That(editor.OpenToolTab(second), Is.True);
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CountAiTabs(editor), Is.EqualTo(2));
            Assert.That(second.SelectedSection.Value?.Id, Is.EqualTo(AiWorkspaceSection.ImageEdit));
        }

        second.Show(AiWorkspaceSection.Jobs);
        mainViewModel.Status.OpenAiJobCenter();
        HeadlessTestHelpers.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(CountAiTabs(editor), Is.EqualTo(2));
            Assert.That(second.SelectedSection.Value?.Id, Is.EqualTo(AiWorkspaceSection.Jobs));
            Assert.That(first.SelectedSection.Value?.Id, Is.EqualTo(AiWorkspaceSection.ImageGeneration));
        }
    }

    [AvaloniaTest]
    public async Task Workspace_BuildsAPageTheFirstTimeItIsShownAndThenKeepsIt()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-workspace-lazy");
        var built = new List<AiWorkspaceSection>();
        await using var workspace = new AiWorkspaceViewModel(editor, CreatePages(built));

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
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-workspace-independent");
        var built = new List<AiWorkspaceSection>();
        await using var left = new AiWorkspaceViewModel(editor, CreatePages(built));
        await using var right = new AiWorkspaceViewModel(editor, CreatePages(built));

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
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-workspace-dispose");
        await using var kept = new AiWorkspaceViewModel(editor, CreatePages([]));
        var closed = new AiWorkspaceViewModel(editor, CreatePages([]));
        var keptPage = (StubPage)kept.Show(AiWorkspaceSection.Jobs);
        var closedPage = (StubPage)closed.Show(AiWorkspaceSection.Jobs);

        await closed.DisposeAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(closedPage.IsDisposed, Is.True);
            Assert.That(keptPage.IsDisposed, Is.False, "The tab still open keeps the work on its own page.");
        }
    }

    [AvaloniaTest]
    public async Task Workspace_DisposesItsPagesWhenOpeningANewTabFails()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-workspace-open-failure");
        var page = new StubPage();
        var workspace = new AiWorkspaceViewModel(editor, _ => page);

        bool opened = await MainViewModel.TryOpenNewAiWorkspaceAsync(
            workspace,
            static () => false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(opened, Is.False);
            Assert.That(page.IsDisposed, Is.True);
            Assert.That(
                () => workspace.Show(AiWorkspaceSection.Jobs),
                Throws.TypeOf<ObjectDisposedException>());
        }
    }

    [AvaloniaTest]
    public async Task Workspace_AsyncDisposeAwaitsChildBeforeEditorResources()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-workspace-async-dispose");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new AsyncStubPage(started, release);
        await using var workspace = new AiWorkspaceViewModel(editor, _ => page);
        workspace.Show(AiWorkspaceSection.Jobs);

        Task disposal = workspace.DisposeAsync().AsTask();
        await started.Task;
        Assert.That(disposal.IsCompleted, Is.False);
        release.TrySetResult();
        await disposal;
        Assert.That(page.IsDisposed, Is.True);
    }

    [AvaloniaTest]
    public async Task Workspace_AsyncDisposeStartsAllPagesBeforePropagatingFault()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-workspace-faulting-dispose");
        var first = new FaultingPage();
        var second = new StubPage();
        int created = 0;
        var workspace = new AiWorkspaceViewModel(editor, _ =>
            created++ == 0 ? first : second);
        workspace.Show(AiWorkspaceSection.ImageGeneration);
        workspace.Show(AiWorkspaceSection.ImageEdit);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await workspace.DisposeAsync());
        Assert.That(second.IsDisposed, Is.True, "all pages must start disposal even when one faults");
    }

    [AvaloniaTest]
    public async Task Workspace_ReopensOnThePageTheLayoutWasSavedOn()
    {
        (_, _, EditViewModel editor) = await TestShell.OpenNewSceneAsync("ai-workspace-layout");
        var saved = new JsonObject();

        await using (var workspace = new AiWorkspaceViewModel(editor, CreatePages([])))
        {
            workspace.Show(AiWorkspaceSection.VideoGeneration);
            workspace.WriteToJson(saved);
        }

        var restoredPages = new List<AiWorkspaceSection>();
        await using var restored = new AiWorkspaceViewModel(editor, CreatePages(restoredPages));
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
    public async Task CommandPalette_WithoutScene_DisablesEveryAiPage()
    {
        await TestReset.ResetShellAsync();
        CommandPaletteViewModel palette = TestShell.MainViewModel.CommandPalette;
        palette.Open();
        try
        {
            using (Assert.EnterMultipleScope())
            {
                foreach (var page in s_aiPages)
                    Assert.That(palette.FilteredCommands.Single(item => item.Command.Id == page.Id).IsEnabled, Is.False, page.Id);
            }
        }
        finally
        {
            palette.Close();
        }
    }

    private static readonly (string Id, AiWorkspaceSection Section, Type PageType)[] s_aiPages =
    [
        ("main.openAiImageGeneration", AiWorkspaceSection.ImageGeneration, typeof(AiImageGenerationDialogViewModel)),
        ("main.openAiImageEdit", AiWorkspaceSection.ImageEdit, typeof(AiImageEditDialogViewModel)),
        ("main.openAiVideoGeneration", AiWorkspaceSection.VideoGeneration, typeof(AiVideoGenerationDialogViewModel)),
        ("main.openAiVideoEditing", AiWorkspaceSection.VideoEditing, typeof(AiVideoEditingViewModel)),
        ("main.openAiSubtitles", AiWorkspaceSection.Subtitles, typeof(AiSubtitleDialogViewModel)),
        ("main.openAiJobCenter", AiWorkspaceSection.Jobs, typeof(AiJobCenterViewModel)),
    ];

    private static MainMenuItem AiToolTabEntry(MainViewModel mainViewModel)
        => mainViewModel.MenuItems.Single(item => item.Header == Strings.View).Items!
            .Single(item => item.Header == Strings.Tools).Items!
            .Single(item => item.Header == Strings.Ai);

    private static int CountAiTabs(EditViewModel editor)
        => editor.DockHost.Factory.EnumerateTools().Count(tool => tool.ToolContext is AiWorkspaceViewModel);

    private static Func<AiWorkspaceSection, IAsyncDisposable> CreatePages(List<AiWorkspaceSection> built)
        => section =>
        {
            built.Add(section);
            return new StubPage();
        };

    private sealed class StubPage : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public ValueTask DisposeAsync() { IsDisposed = true; return ValueTask.CompletedTask; }
    }

    private sealed class AsyncStubPage(
        TaskCompletionSource started,
        TaskCompletionSource release) : IAsyncDisposable
    {
        public bool IsDisposed { get; private set; }

        public async ValueTask DisposeAsync()
        {
            started.TrySetResult();
            await release.Task;
            IsDisposed = true;
        }
    }

    private sealed class FaultingPage : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.FromException(new InvalidOperationException("fault"));
    }
}
