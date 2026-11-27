using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.AgentHost;
using Beutl.Agents;
using Beutl.AgentToolkit.Sessions;
using Beutl.Animation;
using Beutl.Configuration;
using Beutl.Editor.Components.AgentTab;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Editor.Components.AgentTab.Views;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.Views;
using Beutl.Editor.Components.ObjectPropertyTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.Views;
using FluentAvalonia.UI.Controls;
using LiveMarkdown.Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class AgentTabEditorActionsTests
{
    [AvaloniaTest]
    public async Task EditorMenusOpenAndFocusTheAgentAndAppendMentionsFileAndFullScaleFrame()
    {
        var (project, scene, editor) = await TestShell.OpenNewSceneAsync("Opening", 640, 360,
            duration: MediaTime.FromSeconds(12));
        await using var workspace = new Workspace();
        string projectFile = project.Uri!.LocalPath;
        string root = Path.GetDirectoryName(projectFile)!;
        string file = Path.Combine(root, "Review notes.txt");
        File.WriteAllText(file, "Check the opening titles.");
        string folder = Directory.CreateDirectory(Path.Combine(root, "references")).FullName;
        var (opening, title) = AddClip(scene, "Opening", "Title graphic", 0, 0);
        var (accent, _) = AddClip(scene, "Opening", "Title accent", 0, 1);
        var (endCard, _) = AddClip(scene, "End card", "Credits", 2, 0);
        await workspace.Host.ThreadSessions.ForProject(projectFile).SaveDraftAsync(null, new AgentDraft("Review opening", [], false));
        var selection = editor.GetRequiredService<IEditorSelection>();
        var window = Show(new EditView { DataContext = editor }, 1440, 900);

        try
        {
            selection.Select(opening);
            selection.SelectedElements.Add(accent);
            var timeline = editor.FindToolTab<TimelineTabViewModel>()!;
            editor.OpenToolTab(timeline);
            await HeadlessTestHelpers.WaitUntilAsync(() => ClipView(window, opening) is not null);
            Border openingClip = ClipView(window, opening)!.border;

            Assert.That(editor.FindToolTab<AgentTabViewModel>(), Is.Null);
            TestShell.Editor.AgentWorkspaceHost = null;
            var (unavailableMenu, unavailableItem) = OpenMenu(openingClip, AgentStrings.SendToAgent);
            Assert.That(unavailableItem.IsEnabled, Is.False);
            unavailableMenu.Hide();
            TestShell.Editor.AgentWorkspaceHost = workspace.Host;

            ClickMenu(openingClip, AgentStrings.SendToAgent);
            var model = await WaitForComposerAsync(window, editor,
                vm => vm.Prompt == "Review opening @Opening @Opening-2 ");
            Assert.That(model.IncludeSceneContext, Is.False);

            // Right-click an unselected item while the two other clips remain selected.
            ClickMenu(ClipView(window, endCard)!.border, AgentStrings.SendToAgent);
            Assert.That(await WaitForComposerAsync(window, editor, vm => vm.Prompt!.EndsWith("@End-card ")),
                Is.SameAs(model));

            selection.Select(null);
            selection.Select(opening);
            await HeadlessTestHelpers.WaitUntilAsync(() => Visible<EngineObjectPropertyView>(window)
                .Any(view => ReferenceEquals((view.DataContext as EngineObjectPropertyViewModel)?.Model, title)));
            var objectView = Visible<EngineObjectPropertyView>(window)
                .Single(view => ReferenceEquals((view.DataContext as EngineObjectPropertyViewModel)?.Model, title));
            ClickMenu(objectView.FindControl<Grid>("headerPanel")!, AgentStrings.SendToAgent);
            await WaitForComposerAsync(window, editor, vm => vm.Prompt!.EndsWith("@Title-graphic "));

            var fileBrowser = editor.FindToolTab<FileBrowserTabViewModel>()!;
            editor.OpenToolTab(fileBrowser);
            fileBrowser.ViewMode.Value = FileBrowserViewMode.List;
            fileBrowser.RootPath.Value = root;
            await HeadlessTestHelpers.WaitUntilAsync(() => FileRow(window, file) is not null && FileRow(window, folder) is not null);
            var (directoryMenu, directoryItem) = OpenMenu(FileRow(window, folder)!, AgentStrings.SendToAgent);
            Assert.That(directoryItem.IsVisible, Is.False);
            directoryMenu.Hide();
            ClickMenu(FileRow(window, file)!, AgentStrings.SendToAgent);
            await WaitForComposerAsync(window, editor, vm => vm.Attachments.Count == 1 && vm.Attachments[0].Path == file);

            var player = Visible<PlayerView>(window).Single();
            var preview = player.FindControl<Panel>("framePanel")!;
            editor.GetRequiredService<IEditorClock>().Seek(MediaTime.FromSeconds(0.5));
            ClickMenu(preview, AgentStrings.SendFrameToAgent);
            await WaitForComposerAsync(window, editor, vm => vm.Attachments.Count == 2 && vm.IncludeSceneContext);
            var frame = model.Attachments.Single(item => item.Path != file);
            using (var bitmap = new Avalonia.Media.Imaging.Bitmap(frame.Path))
            {
                Assert.That(bitmap.PixelSize, Is.EqualTo(new Avalonia.PixelSize(640, 360)));
                Assert.That(frame.File.MediaType, Is.EqualTo("image/png"));
            }

            // Use the same composer in a wide window for a readable capture of the draft and thumbnails.
            var screenshot = Show(new AgentTabView { DataContext = model }, 900, 520);
            try { Capture(screenshot, "links-send-to-agent-composer.png"); }
            finally { screenshot.Close(); }

            AgentTurnRequest? submitted = null;
            workspace.Bridge.Script = (request, _) =>
            {
                submitted = request;
                return Task.CompletedTask;
            };
            Assert.That(model.CanSend, Is.True);
            await model.SendAsync();
            var mentions = submitted!.Context!.Mentions!;
            Assert.Multiple(() =>
            {
                Assert.That(submitted.Text, Is.EqualTo("Review opening @Opening @Opening-2 @End-card @Title-graphic"));
                Assert.That(mentions.Select(mention => mention.TargetId), Is.EqualTo(new[]
                {
                    opening.Id.Value.ToString("D"), accent.Id.Value.ToString("D"), endCard.Id.Value.ToString("D"), title.Id.Value.ToString("D")
                }));
                Assert.That(mentions.Take(3).All(mention => mention.Kind == AgentContextMentionKind.Element), Is.True);
                Assert.That(mentions[0].StartSeconds, Is.EqualTo(0));
                Assert.That(mentions[0].EndSeconds, Is.EqualTo(2));
                Assert.That(mentions[3].Kind, Is.EqualTo(AgentContextMentionKind.Object));
                Assert.That(mentions[3].Name, Is.EqualTo("Title graphic"));
                Assert.That(mentions[3].TypeName, Is.EqualTo(typeof(RectShape).FullName));
                Assert.That(mentions[3].ElementId, Is.EqualTo(opening.Id.Value.ToString("D")));
                Assert.That(mentions.All(mention => mention.SceneId == scene.Id.Value.ToString("D")), Is.True);
                Assert.That(submitted.Context.AttachedFiles.Select(attachment => attachment.Path), Is.EqualTo(new[] { file, frame.Path }));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task MarkdownLinksClickThroughToSceneNavigationFileManagerAndExternalConfirmation()
    {
        var (project, scene, editor) = await TestShell.OpenNewSceneAsync("Opening review", 640, 360,
            duration: MediaTime.FromSeconds(18));
        await using var workspace = new Workspace();
        using var notifications = new NotificationCapture(TestShell.Notifications);
        scene.Start.Value = MediaTime.FromSeconds(1);
        var (clip, shape) = AddClip(scene, "Opening titles", "Title graphic", 4, 0);
        var marker = new SceneMarker { Name = { Value = "Review marker" }, Time = { Value = MediaTime.FromSeconds(4) } };
        scene.Markers.Add(marker);
        var animation = new KeyFrameAnimation<float>();
        var keyFrame = new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1.2) }, Value = { Value = 75 } };
        animation.KeyFrames.Add(keyFrame, out _);
        shape.Opacity.SetKeyFrameAnimation(animation);

        Scene second;
        using (TestShell.Editor.SuppressProjectItemActivation())
            second = ProjectOperations.AddScene(project, new SceneCreateOptions(640, 360, TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5), "Credits", FrameRate: new Rational(30)));
        ProjectOperations.Save(project);
        Assert.That(TestShell.Editor.TryGetTabItem(second, out _), Is.False);

        string file = Path.Combine(Path.GetDirectoryName(project.Uri!.LocalPath)!, "review notes.txt");
        File.WriteAllText(file, "Review notes");
        string timeLink = "beutl://time/00:02.500";
        string elementLink = $"beutl://element/{clip.Id.Value:D}";
        string objectLink = $"beutl://object/{shape.Id.Value:D}?element={clip.Id.Value:D}&scene={scene.Id.Value:D}&property=Width";
        string fileUri = new Uri(file).AbsoluteUri;
        string keyFrameLink = $"beutl://object/{keyFrame.Id.Value:D}";
        string markerLink = $"beutl://object/{marker.Id.Value:D}";
        string elementTimeLink = $"{elementLink}?time=9.02";
        string timeSceneLink = $"beutl://time/-1?scene={second.Id.Value:D}";
        string sceneLink = $"beutl://scene/{second.Id.Value:D}?time=999";
        string sceneObjectLink = $"beutl://object/{scene.Id.Value:D}";
        string staleLink = $"beutl://element/{Guid.NewGuid():D}";
        const string fileLink = "review%20notes.txt";
        const string staleFile = "deleted-notes.txt";
        const string website = "https://beutl.beditor.net/docs";
        string reply = $"""
            ### Opening review

            The title is ready to check at the opening beat.

            - [Opening at 00:02.500]({timeLink})
            - [Opening titles layer]({elementLink})
            - [Title graphic width]({objectLink})
            - [Review notes]({fileLink})

            [Opacity keyframe]({keyFrameLink}) · [Scene marker]({markerLink}) · [Later in the opening]({elementTimeLink})

            [Credits start]({timeSceneLink}) · [Credits end]({sceneLink}) · [Return to opening]({sceneObjectLink})

            [Open notes by file URI]({fileUri}) · [Beutl guide]({website})

            [Removed layer]({staleLink}) · [Removed notes]({staleFile})
            """;

        var launches = new List<ProcessStartInfo>();
        workspace.Host.LaunchMarkdownFileManagerAsync = startInfo =>
        {
            launches.Add(startInfo);
            return Task.FromResult(true);
        };
        string? launchedUrl = null;
        var originalLaunch = AgentMarkdownRenderer.LaunchUrl;
        AgentMarkdownRenderer.LaunchUrl = url => launchedUrl = url;
        using var model = workspace.Host.CreateWorkspace(editor)!;
        var editorWindow = Show(new EditView { DataContext = editor }, 1440, 900);
        var agentView = new AgentTabView { DataContext = model };
        var window = Show(agentView, 900, 620);
        try
        {
            await model.InitializeAsync();
            workspace.Bridge.Script = (request, _) =>
            {
                workspace.Bridge.Emit(new AgentAssistantMessageCompleted(request.ThreadId, request.TurnId, DateTimeOffset.UtcNow, reply));
                return Task.CompletedTask;
            };
            model.Prompt = "Review the opening cut.";
            Assert.That(model.CanSend, Is.True);
            await model.SendAsync();
            await HeadlessTestHelpers.WaitUntilAsync(() => FindLink(agentView, website) is { } external
                && external.Block.GetInlineGeometry(external.Link) is not null);
            Capture(window, "links-markdown-actions.png");

            IEditorClock clock = editor.GetRequiredService<IEditorClock>();
            ClickLink(agentView, timeLink);
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(3.5)));

            var highlighted = new List<Element>();
            var timeline = editor.FindToolTab<TimelineTabViewModel>()!;
            using var highlight = timeline.HighlightElement.Subscribe(highlighted.Add);
            ClickLink(agentView, elementLink);
            await HeadlessTestHelpers.WaitUntilAsync(() => highlighted.Contains(clip));
            Assert.Multiple(() =>
            {
                Assert.That(editor.GetRequiredService<IEditorSelection>().SelectedObject.Value, Is.SameAs(clip));
                Assert.That(clock.CurrentTime.Value, Is.EqualTo(clip.Start.Value));
            });

            var properties = editor.FindToolTab<ElementPropertyTabViewModel>()!;
            EngineObjectPropertyViewModel? revealed = null;
            IPropertyEditorContext? revealedProperty = null;
            using var reveal = properties.RevealRequested.Subscribe(target => (revealed, revealedProperty) = target);
            ClickLink(agentView, objectLink);
            await HeadlessTestHelpers.WaitUntilAsync(() => revealed is not null);
            Assert.That(revealed!.Model, Is.SameAs(shape));
            Assert.That(revealedProperty, Is.SameAs(revealed.FindPropertyEditor(shape, "Width")).And.Not.Null);
            Assert.That(properties.IsSelected.Value, Is.True);

            ClickLink(agentView, keyFrameLink);
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(5.2)));
            ClickLink(agentView, markerLink);
            var objectProperties = editor.FindToolTab<ObjectPropertyTabViewModel>()!;
            Assert.That(objectProperties.ChildContext.Value?.Target, Is.SameAs(marker));
            Assert.That(objectProperties.IsSelected.Value, Is.True);

            ClickLink(agentView, elementTimeLink);
            Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromFrames(301, new Rational(30))));
            ClickLink(agentView, timeSceneLink);
            var secondEditor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value!;
            Assert.That(secondEditor.Scene, Is.SameAs(second));
            IEditorClock secondClock = secondEditor.GetRequiredService<IEditorClock>();
            Assert.That(secondClock.CurrentTime.Value, Is.EqualTo(second.Start.Value));
            ClickLink(agentView, sceneLink);
            Assert.That(secondClock.CurrentTime.Value, Is.EqualTo(second.Start.Value + second.Duration.Value - MediaTime.FromFrames(1, new Rational(30))));
            ClickLink(agentView, sceneObjectLink);
            Assert.That(TestShell.Editor.SelectedTabItem.Value?.Context.Value, Is.SameAs(editor));

            Assert.That(FindLink(agentView, fileLink)!.Value.Link.HRef!.IsAbsoluteUri, Is.False);
            ClickLink(agentView, fileLink);
            await HeadlessTestHelpers.WaitUntilAsync(() => launches.Count == 1);
            ClickLink(agentView, fileUri);
            await HeadlessTestHelpers.WaitUntilAsync(() => launches.Count == 2);
            ProcessStartInfo expected = FileManagerLauncher.CreateStartInfo(file, isDirectory: false);
            foreach (var launch in launches)
            {
                Assert.That(launch.FileName, Is.EqualTo(expected.FileName));
                Assert.That(launch.Arguments, Is.EqualTo(expected.Arguments));
                Assert.That(launch.ArgumentList, Is.EqualTo(expected.ArgumentList));
            }

            ClickLink(agentView, website);
            await HeadlessTestHelpers.WaitUntilAsync(() => Visible<FAContentDialog>(window).Any());
            Assert.That(launchedUrl, Is.Null);
            var dialog = Visible<FAContentDialog>(window).Single();
            HeadlessTestHelpers.Click(Visible<Button>(dialog).Single(button => button.Name == "PrimaryButton"));
            await HeadlessTestHelpers.WaitUntilAsync(() => launchedUrl == website);

            ClickLink(agentView, staleLink);
            ClickLink(agentView, staleFile);
            await HeadlessTestHelpers.WaitUntilAsync(() => notifications.All.Count(IsMissingWarning) == 2);
            Assert.That(launches, Has.Count.EqualTo(2));
        }
        finally
        {
            AgentMarkdownRenderer.LaunchUrl = originalLaunch;
            window.Close();
            editorWindow.Close();
        }
    }

    private static bool IsMissingWarning(Notification notice)
        => notice.Type == NotificationType.Warning && notice.Message == AgentStrings.LinkUnavailable;

    private static (Element Element, RectShape Shape) AddClip(Scene scene, string name, string objectName, double start, int layer)
    {
        var element = new Element
        {
            Name = { Value = name },
            Start = { Value = MediaTime.FromSeconds(start) },
            Length = { Value = MediaTime.FromSeconds(2) },
            ZIndex = { Value = layer },
            Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, $"{Guid.NewGuid():N}.belm"))
        };
        var shape = new RectShape
        {
            Name = { Value = objectName },
            Width = { Value = layer == 0 ? 410 : 245 },
            Height = { Value = layer == 0 ? 198 : 88 },
            Fill = { Value = new SolidColorBrush(Color.Parse(layer == 0 ? "#536DF2" : "#FDC784")) }
        };
        element.AddObject(shape);
        scene.Children.Add(element);
        return (element, shape);
    }

    private static ElementView? ClipView(Visual root, Element element) => Visible<ElementView>(root)
        .SingleOrDefault(view => ReferenceEquals((view.DataContext as ElementViewModel)?.Model, element));

    private static Border? FileRow(Window window, string path) => Visible<FileBrowserTabView>(window)
        .SelectMany(Visible<Border>).SingleOrDefault(border => border.Classes.Contains("file-item")
            && border.DataContext is FileSystemItemViewModel item && item.FullPath == path);

    private static (FAMenuFlyout Menu, FAMenuFlyoutItem Item) OpenMenu(Control control, string label)
    {
        var menu = (FAMenuFlyout)control.ContextFlyout!;
        HeadlessTestHelpers.Click(control, button: MouseButton.Right, move: true, ticks: 3);
        Assert.That(menu.IsOpen, Is.True, $"The {control.Name} context menu did not open.");
        return (menu, menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == label));
    }

    private static void ClickMenu(Control control, string label)
    {
        var (menu, item) = OpenMenu(control, label);
        Assert.That(item.IsVisible && item.IsEnabled, Is.True);
        item.BringIntoView();
        HeadlessTestHelpers.Render(3);
        HeadlessTestHelpers.Click(item, move: true, ticks: 3);
        Assert.That(menu.IsOpen, Is.False);
    }

    private static async Task<AgentTabViewModel> WaitForComposerAsync(Window window, EditViewModel editor, Func<AgentTabViewModel, bool> ready)
    {
        await HeadlessTestHelpers.WaitUntilAsync(() => editor.FindToolTab<AgentTabViewModel>() is { IsSelected.Value: true } model
            && ready(model) && Visible<TextBox>(window).Any(box => box.Name == "PromptBox" && box.IsFocused),
            message: "The selected Agent tab did not receive the editor action or focus its composer.");
        return editor.FindToolTab<AgentTabViewModel>()!;
    }

    private static (MarkdownTextBlock Block, Link Link)? FindLink(AgentTabView view, string destination)
    {
        foreach (var block in Visible<MarkdownTextBlock>(view))
        {
            if (block.Inlines?.OfType<Link>().FirstOrDefault(link => link.HRef?.OriginalString == destination) is { } link)
                return (block, link);
        }
        return null;
    }

    private static void ClickLink(AgentTabView view, string destination)
    {
        var (block, link) = FindLink(view, destination) ?? throw new AssertionException($"Markdown did not render a link for {destination}.");
        var point = block.GetInlineGeometry(link)!.Rects[0].Center;
        Assert.That(block.GetLinkAt(point), Is.SameAs(link));
        HeadlessTestHelpers.Click(block, point, move: true, ticks: 3);
    }

    private static IEnumerable<T> Visible<T>(Visual root) where T : Control
        => root.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible);

    private static Window Show(Control control, int width, int height)
    {
        var window = new Window { Content = control, Width = width, Height = height, RequestedThemeVariant = ThemeVariant.Dark };
        if (control is AgentTabView)
            window.Bind(Window.BackgroundProperty, window.GetResourceObservable("DockSurfaceWorkbenchBrush"));
        window.Show();
        HeadlessTestHelpers.Render(5);
        return window;
    }

    private static void Capture(Window window, string fileName)
    {
        string directory = Path.Combine(Path.GetTempPath(), "beutl-agent-links");
        Directory.CreateDirectory(directory);
        HeadlessTestHelpers.Render(5);
        using var bitmap = window.CaptureRenderedFrame() ?? throw new AssertionException("No headless frame was rendered.");
        bitmap.Save(Path.Combine(directory, fileName), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private sealed class Workspace : IAsyncDisposable
    {
        private readonly IAgentWorkspaceHost? _previous;
        private readonly AgentHostEndpoint _endpoint;
        private readonly AgentRuntimeManager _runtime;
        public BeutlAgentWorkspaceHost Host { get; }
        public AgentTabTestBridge Bridge { get; } = new();

        public Workspace()
        {
            string home = TestShell.NewWorkspace($"agent-links-{Guid.NewGuid():N}");
            var config = new AiAgentConfig
            {
                StdioCommand = { Value = "test-mcp" },
                CodexExecutablePath = { Value = Environment.ProcessPath! },
                DefaultRuntimeProviderId = { Value = "codex" }
            };
            _endpoint = new AgentHostEndpoint(TestShell.Project, TestShell.Editor, UnavailableAgentAiBackend.Instance, config, home);
            _runtime = new AgentRuntimeManager(Bridge, new AgentThreadStore(home), new AgentBinaryLocator(probeVersions: false));
            Host = new BeutlAgentWorkspaceHost(_runtime, _endpoint, config, home);
            _previous = TestShell.Editor.AgentWorkspaceHost;
            TestShell.Editor.AgentWorkspaceHost = Host;
        }

        public async ValueTask DisposeAsync()
        {
            TestShell.Editor.AgentWorkspaceHost = _previous;
            await TestReset.ResetShellAsync();
            await Host.ThreadSessions.DisposeAsync();
            await _runtime.DisposeAsync();
            await _endpoint.DisposeAsync();
        }
    }
}
