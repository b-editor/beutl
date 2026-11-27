using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.AgentHost;
using Beutl.Agents;
using Beutl.Configuration;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Editor.Components.AgentTab.Views;
using Beutl.Editor.Components.Helpers;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class AgentTabPhase2Tests
{
    private static RawInputModifiers CommandModifier => KeyGestureHelper.GetCommandModifier() == KeyModifiers.Meta
        ? RawInputModifiers.Meta : RawInputModifiers.Control;

    [AvaloniaTest]
    public async Task ComposerHighlightsFileDropsAndDeduplicatesAttachments()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        string notes = Path.Combine(workspace.Root, "notes.txt");
        string captions = Path.Combine(workspace.Root, "captions.srt");
        File.WriteAllText(notes, "Notes");
        File.WriteAllText(captions, "Captions");
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 590);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var composer = Composer(view);
            var prompt = composer.FindControl<TextBox>("PromptBox")!;
            var card = composer.FindControl<Border>("ComposerCard")!;
            var indicator = composer.FindControl<Border>("DropIndicator")!;
            Assert.That(DragDrop.GetAllowDrop(card), Is.True);
            Assert.That(indicator.IsVisible, Is.False);

            using var text = new DataTransfer();
            text.Add(DataTransferItem.CreateText("Not a file drop"));
            Drag(DragDrop.DragOverEvent, text, prompt);
            Assert.That(indicator.IsVisible, Is.False);

            using var files = FileTransfer(notes, notes, captions);
            var over = Drag(DragDrop.DragOverEvent, files, prompt);
            Assert.That(over.DragEffects, Is.EqualTo(DragDropEffects.Copy));
            Assert.That(indicator.IsEffectivelyVisible, Is.True);
            Capture(view, "phase2-drop-overlay");

            Drag(DragDrop.DragLeaveEvent, files, prompt);
            Assert.That(indicator.IsVisible, Is.False);
            Drag(DragDrop.DragOverEvent, files, prompt);
            Assert.That(Drag(DragDrop.DropEvent, files, prompt).DragEffects, Is.EqualTo(DragDropEffects.Copy));
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManage && model.Attachments.Count == 2);
            Assert.That(model.Attachments.Select(item => item.Path), Is.EqualTo(new[] { notes, captions }));
            Assert.That(indicator.IsVisible, Is.False);

            Drag(DragDrop.DropEvent, files, prompt);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManage);
            Assert.That(model.Attachments.Count, Is.EqualTo(2));
            model.Prompt = "Review these files";
            await model.SendAsync();
            var timeline = await workspace.Runtime.LoadTimelineAsync(workspace.ProjectPath, model.SelectedThread!.Id);
            Assert.That(timeline.OfType<AgentUserMessage>().Single().Attachments!.Select(attachment => attachment.Path),
                Is.EqualTo(new[] { notes, captions }));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task ClipboardPastesBitmapFilesAndShortTextAndCollapsesMultilineText()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        string imagePath = Path.Combine(workspace.Root, "opening.png");
        AgentTabViewTests.CreatePreview(imagePath);
        using var bitmap = new Bitmap(imagePath);
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 590);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var composer = Composer(view);
            var prompt = composer.FindControl<TextBox>("PromptBox")!;
            composer.FocusPrompt();
            var clipboard = window.Clipboard!;
            await clipboard.SetBitmapAsync(bitmap);
            Press(window, PhysicalKey.V, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManage && model.Attachments.Count == 1);
            var image = model.Attachments.Single();
            string directory = Path.Combine(workspace.Root, "agent-threads", "attachments");
            Assert.That(image.Label, Is.EqualTo(AgentStrings.PastedImage));
            Assert.That(image.Thumbnail, Is.Not.Null);
            Assert.That(image.File.MediaType, Is.EqualTo("image/png"));
            Assert.That(Path.GetDirectoryName(image.Path), Is.EqualTo(directory));
            Assert.That(Path.GetFileName(image.Path), Does.Match(@"^paste-[a-f0-9]{32}\.png$"));

            string longText = string.Join("\r\n", Enumerable.Range(1, 30).Select(index => $"Opening shot note {index}"));
            Assert.That(longText.Length, Is.LessThan(1500));
            await clipboard.SetTextAsync(longText);
            Press(window, PhysicalKey.V, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManage && model.Attachments.Count == 2);
            var text = model.Attachments.Single(item => item.IsPastedText);
            Assert.Multiple(() =>
            {
                Assert.That(text.Label, Is.EqualTo(AgentStrings.PastedText));
                Assert.That(text.Detail, Is.EqualTo(string.Format(AgentStrings.PastedTextDetail, 30, longText.Length)));
                Assert.That(text.Tooltip, Does.StartWith("Opening shot note 1").And.EndWith("…"));
                Assert.That(Path.GetDirectoryName(text.Path), Is.EqualTo(directory));
                Assert.That(Path.GetFileName(text.Path), Does.Match(@"^pasted-text-[a-f0-9]{32}\.txt$"));
                Assert.That(prompt.Text, Is.Null.Or.Empty);
                Assert.That(model.CanSend, Is.True);
            });
            Assert.That(await File.ReadAllTextAsync(text.Path), Is.EqualTo(longText));
            Assert.That(composer.GetVisualDescendants().OfType<Image>().Any(control => control.Source == image.Thumbnail), Is.True);
            Capture(view, "phase2-pasted-attachments");

            string notes = Path.Combine(workspace.Root, "reference.txt");
            File.WriteAllText(notes, "Reference");
            using var files = FileTransfer(notes, notes);
            await clipboard.SetDataAsync(files);
            Press(window, PhysicalKey.V, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManage && model.Attachments.Count == 3);
            Assert.That(model.Attachments.Count(item => item.Path == notes), Is.EqualTo(1));

            SetPrompt(prompt, "Keep this and replace the rest");
            prompt.SelectionStart = 14;
            prompt.SelectionEnd = prompt.Text!.Length;
            await clipboard.SetTextAsync("the title");
            Press(window, PhysicalKey.V, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => prompt.Text == "Keep this and the title");
            Assert.That(prompt.CaretIndex, Is.EqualTo(prompt.Text!.Length));
            Assert.That(model.Attachments.Count, Is.EqualTo(3));
            await model.SendAsync();
            var timeline = await workspace.Runtime.LoadTimelineAsync(workspace.ProjectPath, model.SelectedThread!.Id);
            var sent = timeline.OfType<AgentUserMessage>().Single().Attachments!;
            Assert.Multiple(() =>
            {
                Assert.That(sent[0].IsImage, Is.True);
                Assert.That(sent[0].Path, Is.EqualTo(image.Path));
                Assert.That(sent[1].MediaType, Is.EqualTo("text/plain"));
                Assert.That(sent[1].Detail, Is.EqualTo(text.Detail));
                Assert.That(sent[2].Path, Is.EqualTo(notes));
            });
            await clipboard.ClearAsync();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task ClipboardPastesEncodedPngAndSendsAnOtherwiseEmptyLongTextDraft()
    {
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        string path = Path.Combine(workspace.Root, "preview.png");
        AgentTabViewTests.CreatePreview(path);
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 590);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            Composer(view).FocusPrompt();
            var clipboard = window.Clipboard!;
            await clipboard.SetValueAsync(DataFormat.CreateBytesPlatformFormat("image/png"), File.ReadAllBytes(path));
            Press(window, PhysicalKey.V, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManage && model.Attachments.Count == 1);
            Assert.That(model.Attachments.Single().Thumbnail, Is.Not.Null);

            string longText = new('x', 1500);
            await clipboard.SetTextAsync(longText);
            Press(window, PhysicalKey.V, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.CanManage && model.Attachments.Count == 2);
            var text = model.Attachments.Single(item => item.IsPastedText);
            Assert.That(model.Prompt, Is.Null.Or.Empty);
            Press(window, PhysicalKey.Enter);
            await HeadlessTestHelpers.WaitUntilAsync(() => workspace.SentAttachments.Count == 2 && !model.IsRunning);
            Assert.That(workspace.SentAttachments, Does.Contain(text.File));
            Assert.That(await File.ReadAllTextAsync(text.Path), Is.EqualTo(longText));
            await clipboard.ClearAsync();
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task MentionPopupFiltersAndSendsExactTargetsAfterRestoringTheDraft()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        var (project, scene, editor) = await TestShell.OpenNewSceneAsync("Mention project", 1920, 1080, 24,
            duration: MediaTime.FromSeconds(8));
        scene.Name.Value = "Opening";
        var firstTitle = new Element
        {
            Name = { Value = "Title overlay" },
            Start = { Value = MediaTime.FromSeconds(0.5) },
            Length = { Value = MediaTime.FromSeconds(1.75) },
            ZIndex = { Value = 2 }
        };
        var secondTitle = new Element
        {
            Name = { Value = "Title overlay" },
            Start = { Value = MediaTime.FromSeconds(3) },
            Length = { Value = MediaTime.FromSeconds(1) },
            ZIndex = { Value = 4 }
        };
        scene.Children.Add(firstTitle);
        scene.Children.Add(secondTitle);
        var closing = new Scene(1280, 720, "Closing");
        closing.Children.Add(new Element { Name = { Value = "Inactive title" } });
        project.Items.Add(closing);

        string home = BeutlHomeIsolation.CurrentHome!;
        string projectPath = project.Uri!.LocalPath;
        string root = Path.GetDirectoryName(projectPath)!;
        string assets = Directory.CreateDirectory(Path.Combine(root, "assets")).FullName;
        string media = Path.Combine(assets, "harbor.png");
        AgentTabViewTests.CreatePreview(media);
        foreach (string folder in new[] { ".git", ".beutl", "bin", "obj", "node_modules" })
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(root, folder)).FullName, "ignored.txt"), "");

        await using var endpoint = new AgentHostEndpoint(TestShell.Project, TestShell.Editor,
            UnavailableAgentAiBackend.Instance, AgentHostTestHelpers.AvailablePort(), "phase2-test-token",
            registryDirectory: Path.Combine(home, "phase2-endpoint"));
        var config = new AiAgentConfig
        {
            DefaultRuntimeProviderId = { Value = "codex" },
            CodexExecutablePath = { Value = Environment.ProcessPath! },
            StdioCommand = { Value = "test-mcp" }
        };
        var bridge = new AgentTabTestBridge();
        await using var runtime = new AgentRuntimeManager(bridge, new AgentThreadStore(home));
        var workspace = new BeutlAgentWorkspaceHost(runtime, endpoint, config, home);
        await using var sessions = workspace.ThreadSessions;
        using var model = workspace.CreateWorkspace(editor)!;
        await model.InitializeAsync();
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 590);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var composer = Composer(view);
            var card = composer.FindControl<Border>("ComposerCard")!;
            var prompt = composer.FindControl<TextBox>("PromptBox")!;
            var popup = composer.FindControl<Popup>("SuggestionsPopup")!;
            var list = composer.FindControl<ListBox>("SuggestionsList")!;
            composer.FocusPrompt();
            window.KeyTextInput("@");
            await HeadlessTestHelpers.WaitUntilAsync(() => popup.IsOpen
                && list.Items.Cast<AgentComposerSuggestionRow>().Any(row => row.Detail == "assets/harbor.png"));
            HeadlessTestHelpers.Render(4);
            var rows = list.Items.Cast<AgentComposerSuggestionRow>().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(rows.Take(3).Select(row => row.KindLabel),
                    Is.EqualTo(new[] { AgentStrings.MentionScene, AgentStrings.MentionElement, AgentStrings.MentionFile }));
                Assert.That(rows.Where(row => row.KindLabel == AgentStrings.MentionScene).Select(row => row.Title),
                    Is.EquivalentTo(new[] { "@Opening", "@Closing" }));
                Assert.That(rows.Count(row => row.KindLabel == AgentStrings.MentionElement), Is.EqualTo(2));
                Assert.That(rows.Any(row => row.Title is "@ignored.txt" or "@Inactive title"), Is.False);
                Assert.That(list.TranslatePoint(default, window)!.Value.Y, Is.LessThan(card.TranslatePoint(default, window)!.Value.Y));
            });
            Capture(view, "phase2-mention-autocomplete");

            window.KeyTextInput("open");
            await WaitForRows(1);
            Press(window, PhysicalKey.Enter);
            Assert.That(prompt.Text, Is.EqualTo("@Opening "));
            Assert.That(model.SelectedThread, Is.Null);

            window.KeyTextInput("@title");
            await WaitForRows(2);
            Press(window, PhysicalKey.ArrowDown);
            Assert.That(((AgentComposerSuggestionRow)list.SelectedItem!).Candidate!.Mention.TargetId,
                Is.EqualTo(secondTitle.Id.Value.ToString("D")));
            Press(window, PhysicalKey.Tab);
            Assert.That(prompt.Text, Is.EqualTo("@Opening @Title-overlay "));

            window.KeyTextInput("@title");
            await WaitForRows(2);
            Press(window, PhysicalKey.Enter, CommandModifier);
            Assert.That(prompt.Text, Is.EqualTo("@Opening @Title-overlay @Title-overlay-2 "));
            Assert.That(model.SelectedThread, Is.Null);

            window.KeyTextInput("@assets/har");
            await WaitForRows(1);
            var fileItem = list.GetVisualDescendants().OfType<ListBoxItem>()
                .Single(item => item.IsEffectivelyVisible && item.DataContext is AgentComposerSuggestionRow { Title: "@harbor.png" });
            HeadlessTestHelpers.Click(fileItem, move: true, ticks: 4);
            const string acceptedPrompt = "@Opening @Title-overlay @Title-overlay-2 @harbor.png ";
            Assert.That(prompt.Text, Is.EqualTo(acceptedPrompt));
            window.KeyTextInput("@clo");
            await WaitForRows(1);
            Press(window, PhysicalKey.Escape);
            Assert.That(popup.IsOpen, Is.False);
            Assert.That(prompt.Text, Is.EqualTo(acceptedPrompt + "@clo"));

            SetPrompt(prompt, acceptedPrompt);
            model.IncludeSceneContext = false;
            var otherThread = await runtime.CreateThreadAsync(projectPath, workspace.GetProviders().Single(provider => provider.Id == "codex"),
                "Other conversation");
            await model.SelectThreadAsync(otherThread);
            await model.NewThreadAsync();
            Assert.That(model.Prompt, Is.EqualTo(acceptedPrompt));
            composer.FocusPrompt();
            AgentContextSnapshot? sent = null;
            bridge.Script = (request, _) =>
            {
                sent = request.Context;
                return Task.CompletedTask;
            };
            Press(window, PhysicalKey.Enter);
            await HeadlessTestHelpers.WaitUntilAsync(() => sent is not null && !model.IsRunning);
            var mentions = sent!.Mentions!;
            Assert.Multiple(() =>
            {
                Assert.That(sent.SceneId, Is.Null);
                Assert.That(mentions.Select(mention => mention.Token), Is.EqualTo(new[] { "@Opening", "@Title-overlay", "@Title-overlay-2", "@harbor.png" }));
                Assert.That(mentions[0].TargetId, Is.EqualTo(scene.Id.Value.ToString("D")));
                Assert.That(mentions[1].TargetId, Is.EqualTo(secondTitle.Id.Value.ToString("D")));
                Assert.That(mentions[1].SceneId, Is.EqualTo(scene.Id.Value.ToString("D")));
                Assert.That(mentions[1].Detail, Is.EqualTo("3–4 s · Layer 4"));
                Assert.That(mentions[2].TargetId, Is.EqualTo(firstTitle.Id.Value.ToString("D")));
                Assert.That(mentions[2].Detail, Is.EqualTo("0.5–2.25 s · Layer 2"));
                Assert.That(mentions[3].Path, Is.EqualTo(media));
            });

            for (int index = 0; index < 205; index++)
                File.WriteAllText(Path.Combine(assets, $"asset-{index:D3}.dat"), "");
            var capped = await workspace.GetMentionCandidatesAsync(editor, projectPath, CancellationToken.None);
            Assert.That(capped.Count(candidate => candidate.Mention.Kind == AgentContextMentionKind.File), Is.EqualTo(200));

            async Task WaitForRows(int count)
            {
                await HeadlessTestHelpers.WaitUntilAsync(() => popup.IsOpen && list.ItemCount == count);
                HeadlessTestHelpers.Render(4);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task ComposerSendKeyUsesTheConfiguredGestureForSendQueueAndSteer()
    {
        await using var workspace = new AgentTabTestWorkspace();
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Bridge.Script = (_, ct) => finish.Task.WaitAsync(ct);
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 590);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var composer = Composer(view);
            var prompt = composer.FindControl<TextBox>("PromptBox")!;
            composer.FocusPrompt();
            SetPrompt(prompt, "Initial turn");
            Press(window, PhysicalKey.Enter, RawInputModifiers.Shift);
            Assert.That(prompt.Text, Does.EndWith(Environment.NewLine));
            Assert.That(workspace.Submissions, Is.Empty);
            Press(window, PhysicalKey.Enter, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.IsRunning && workspace.Submissions.Count == 1);
            Assert.That(workspace.Submissions[0].Prompt, Is.EqualTo("Initial turn"));

            SetPrompt(prompt, "Queue with Enter");
            Press(window, PhysicalKey.Enter);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.QueuedMessages.Count == 1);
            SetPrompt(prompt, "Steer with the command modifier");
            Press(window, PhysicalKey.Enter, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => workspace.Submissions.Count == 2);
            Assert.That(model.QueuedMessages.Count, Is.EqualTo(1));

            workspace.ComposerSendKey = AgentComposerSendKey.CtrlEnter;
            Assert.That(model.SendTooltip, Does.Contain("Shift+Enter"));
            SetPrompt(prompt, "Queue with the command modifier");
            Press(window, PhysicalKey.Enter);
            Assert.That(prompt.Text, Does.EndWith(Environment.NewLine));
            Assert.That(model.QueuedMessages.Count, Is.EqualTo(1));
            Press(window, PhysicalKey.Enter, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.QueuedMessages.Count == 2);
            SetPrompt(prompt, "Steer with the command modifier and Shift");
            Press(window, PhysicalKey.Enter, CommandModifier | RawInputModifiers.Shift);
            await HeadlessTestHelpers.WaitUntilAsync(() => workspace.Submissions.Count == 3);
            Assert.That(model.QueuedMessages.Select(message => message.Text),
                Is.EqualTo(new[] { "Queue with Enter", "Queue with the command modifier" }));
            Assert.That(workspace.Submissions.Skip(1).Select(submission => submission.Prompt),
                Is.EqualTo(new[] { "Steer with the command modifier", "Steer with the command modifier and Shift" }));

            finish.SetResult();
            await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsRunning && model.QueuedMessages.Count == 0);
            SetPrompt(prompt, "Send while idle");
            Press(window, PhysicalKey.Enter, CommandModifier);
            await HeadlessTestHelpers.WaitUntilAsync(() => workspace.Submissions.Count == 6 && !model.IsRunning);
        }
        finally
        {
            finish.TrySetResult();
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task ImeConfirmationDoesNotAcceptSuggestionsSendQueueOrSteer()
    {
        await using var workspace = new AgentTabTestWorkspace
        {
            BundledSkills = [new("test", "Test IME", AgentComposerSuggestionKind.Skill)]
        };
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Bridge.Script = (_, ct) => finish.Task.WaitAsync(ct);
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 590);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var composer = Composer(view);
            var prompt = composer.FindControl<TextBox>("PromptBox")!;
            var popup = composer.FindControl<Popup>("SuggestionsPopup")!;
            composer.FocusPrompt();
            window.KeyTextInput("$te");
            await HeadlessTestHelpers.WaitUntilAsync(() => popup.IsOpen);
            var presenter = prompt.GetVisualDescendants().OfType<TextPresenter>().Single();
            presenter.PreeditText = "確認";
            Press(window, PhysicalKey.Enter);
            Press(window, PhysicalKey.Enter, CommandModifier);
            Assert.That(prompt.Text, Is.EqualTo("$te"));
            Assert.That(workspace.Submissions, Is.Empty);

            presenter.PreeditText = null;
            ImeProcessedEnter(prompt);
            Assert.That(prompt.Text, Is.EqualTo("$te"));
            Assert.That(popup.IsOpen, Is.True);
            Press(window, PhysicalKey.Escape);
            SetPrompt(prompt, "First turn");
            presenter.PreeditText = "変換";
            Press(window, PhysicalKey.Enter);
            Assert.That(workspace.Submissions, Is.Empty);
            presenter.PreeditText = null;
            Press(window, PhysicalKey.Enter);
            await HeadlessTestHelpers.WaitUntilAsync(() => model.IsRunning && workspace.Submissions.Count == 1);

            SetPrompt(prompt, "Follow-up");
            presenter.PreeditText = "候補";
            Press(window, PhysicalKey.Enter);
            Press(window, PhysicalKey.Enter, CommandModifier);
            workspace.ComposerSendKey = AgentComposerSendKey.CtrlEnter;
            Press(window, PhysicalKey.Enter, CommandModifier);
            Press(window, PhysicalKey.Enter, CommandModifier | RawInputModifiers.Shift);
            presenter.PreeditText = null;
            ImeProcessedEnter(prompt);
            Assert.That(prompt.Text, Is.EqualTo("Follow-up"));
            Assert.That(workspace.Submissions.Count, Is.EqualTo(1));
            Assert.That(model.QueuedMessages, Is.Empty);

            finish.SetResult();
            await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsRunning);
        }
        finally
        {
            finish.TrySetResult();
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task ThreadSearchAndThreadActionFlyoutsUseCompactPadding()
    {
        using var theme = AgentTabViewTests.UseDockTheme(ThemeVariant.Dark);
        await using var workspace = new AgentTabTestWorkspace();
        foreach (string title in new[] { "Opening montage", "Color grade", "End card review" })
            await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, workspace.ProviderDescriptors.Single(), title);
        using var model = workspace.CreateWorkspace(workspace.Editor);
        await model.InitializeAsync();
        bool overlays = TestAppBuilder.HeadlessOptions.OverlayPopups;
        TestAppBuilder.HeadlessOptions.OverlayPopups = true;
        var view = new AgentTabView { DataContext = model };
        var window = AgentTabViewTests.CreateDockWindow(view, ThemeVariant.Dark, 390, 590);
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(4);
            var bar = view.GetVisualDescendants().OfType<AgentThreadBar>().Single();
            var switcher = bar.FindControl<Button>("ThreadSwitcherButton")!;
            HeadlessTestHelpers.Click(switcher, move: true, ticks: 4);
            var search = bar.FindControl<TextBox>("ThreadSearchBox")!;
            Assert.That(search.FindAncestorOfType<FlyoutPresenter>()!.Padding, Is.EqualTo(new Thickness(8)));
            Assert.That(bar.FindControl<ItemsControl>("ThreadMatches")!.ItemCount, Is.EqualTo(3));
            window.MouseMove(new Point(4, 42));
            Capture(view, "phase2-thread-picker");

            var rename = bar.FindControl<ItemsControl>("ThreadMatches")!.GetVisualDescendants().OfType<Button>()
                .First(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == AgentStrings.RenameThread);
            HeadlessTestHelpers.Click(rename, move: true, ticks: 4);
            var renameField = bar.FindControl<TextBox>("RenameTextBox")!;
            Assert.That(renameField.IsEffectivelyVisible, Is.True);
            Assert.That(renameField.FindAncestorOfType<FlyoutPresenter>()!.Padding, Is.EqualTo(new Thickness(8)));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            TestAppBuilder.HeadlessOptions.OverlayPopups = overlays;
        }
    }

    private static AgentComposerView Composer(AgentTabView view) => view.GetVisualDescendants().OfType<AgentComposerView>().Single();

    private static DataTransfer FileTransfer(params string[] paths)
    {
        var transfer = new DataTransfer();
        foreach (string path in paths)
        {
            var file = new Mock<IStorageFile>();
            file.SetupGet(item => item.Path).Returns(new Uri(path));
            file.SetupGet(item => item.Name).Returns(Path.GetFileName(path));
            transfer.Add(DataTransferItem.CreateFile(file.Object));
        }
        return transfer;
    }

    private static DragEventArgs Drag(RoutedEvent<DragEventArgs> kind, DataTransfer transfer, Control target)
    {
        var args = new DragEventArgs(kind, transfer, target, new Point(10, 10), KeyModifiers.None);
        target.RaiseEvent(args);
        HeadlessTestHelpers.Render(3);
        return args;
    }

    private static void ImeProcessedEnter(TextBox prompt)
    {
        prompt.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.ImeProcessed,
            PhysicalKey = PhysicalKey.Enter,
            KeyModifiers = KeyGestureHelper.GetCommandModifier()
        });
        HeadlessTestHelpers.Render(3);
    }

    private static void SetPrompt(TextBox prompt, string text)
    {
        prompt.SetCurrentValue(TextBox.TextProperty, text);
        prompt.CaretIndex = prompt.SelectionStart = prompt.SelectionEnd = text.Length;
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        HeadlessTestHelpers.Render(4);
    }

    private static void Capture(AgentTabView view, string name)
    {
        HeadlessTestHelpers.Render(6);
        HeadlessCapture.SaveIfRequested(view, string.Empty, name);
    }
}
