using System.Text.Json.Nodes;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Agents;
using Beutl.Configuration;
using Beutl.Editor.Components.AgentTab;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Testing.Headless;
using Moq;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AgentTabViewModelTests
{
    [AvaloniaTest]
    public async Task StreamsTextReasoningToolsAndPlansIntoTheirTimelineItems()
    {
        await using var workspace = new AgentTabTestWorkspace();
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        var firstChunk = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Bridge.Script = async (request, ct) =>
        {
            var (thread, turn, _, _) = request;
            var now = DateTimeOffset.UtcNow;
            workspace.Bridge.Emit(new AgentReasoningDelta(thread, turn, now, "Thinking"));
            workspace.Bridge.Emit(new AgentAssistantTextDelta(thread, turn, now, "Hello", "message"));
            firstChunk.SetResult();
            await continued.Task.WaitAsync(ct);
            workspace.Bridge.Emit(new AgentToolCallUpdated(thread, turn, now, "tool", AgentToolStatus.Running));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "tool", "render_still", "beutl"));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now, "tool", AgentToolStatus.Completed,
                [new AgentToolOutput("text", "Rendered")]));
            workspace.Bridge.Emit(new AgentPlanUpdated(thread, turn, now, [new AgentPlanStep("Inspect the scene", AgentPlanStepStatus.InProgress)]));
            workspace.Bridge.Emit(new AgentPlanUpdated(thread, turn, now, [new AgentPlanStep("Inspect the scene", AgentPlanStepStatus.Completed)]));
            workspace.Bridge.Emit(new AgentAssistantTextDelta(thread, turn, now, " **world**", "message"));
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, now, "Hello **world**", "message"));
        };

        viewModel.Prompt = "Describe this frame";
        Task sending = viewModel.SendAsync();
        try
        {
            await firstChunk.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.Timeline.OfType<AgentAssistantItem>().Any());
            Assert.That(viewModel.Timeline.OfType<AgentAssistantItem>().Single().Text, Is.EqualTo("Hello"));
            Assert.That(viewModel.Timeline.OfType<AgentAssistantItem>().Single().CanCopy, Is.False);
        }
        finally
        {
            continued.TrySetResult();
            await sending.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Timeline.OfType<AgentUserItem>().Single().Text, Is.EqualTo("Describe this frame"));
            Assert.That(viewModel.Timeline.OfType<AgentAssistantItem>().Single().Text, Is.EqualTo("Hello **world**"));
            Assert.That(viewModel.Timeline.OfType<AgentAssistantItem>().Single().CanCopy, Is.True);
            Assert.That(viewModel.Timeline.OfType<AgentReasoningItem>().Single().Text, Is.EqualTo("Thinking"));
            Assert.That(viewModel.Timeline.OfType<AgentToolItem>().Single().Output, Is.EqualTo("Rendered"));
            Assert.That(viewModel.Timeline.OfType<AgentToolItem>().Single().ToolName, Is.EqualTo("render_still"));
            Assert.That(viewModel.Timeline.OfType<AgentToolItem>().Single().ServerName, Is.EqualTo("beutl"));
            Assert.That(viewModel.Timeline.OfType<AgentPlanItem>().Single().Steps.Single().IsCompleted, Is.True);
        });
    }

    [AvaloniaTest]
    public async Task OnlyPromptsForInteractivePermissionsAndSubmitsOptionAndTextAnswers()
    {
        await using var workspace = new AgentTabTestWorkspace { DefaultPermissionMode = AgentPermissionMode.Supervised };
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        var decisions = new List<AgentPermissionDecision>();
        IReadOnlyDictionary<string, string[]>? answers = null;
        workspace.Bridge.Script = async (request, ct) =>
        {
            decisions.Add(await workspace.Bridge.AskPermissionAsync(request, "Write a file", null, ct));
            decisions.Add(await workspace.Bridge.AskPermissionAsync(request, "Write a file", null, ct));
            answers = await workspace.Bridge.AskQuestionAsync(request,
                [new AgentQuestion("color", "Which color?", [new("Blue"), new("Red")]), new AgentQuestion("label", "Name?"),
                 new AgentQuestion("secret", "Secret?", IsSecret: true)], ct);
        };

        viewModel.Prompt = "Prepare a title card";
        Task sending = viewModel.SendAsync();
        await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.HasActivePermission);
        var card = viewModel.ActivePermission!;
        Assert.That(card.ToolName, Is.EqualTo("Write a file"));
        Assert.That(viewModel.Timeline.OfType<AgentPermissionItem>(), Is.Empty);
        card.Respond(AgentPermissionDecision.AllowAlways);

        await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.HasActiveQuestion);
        var question = viewModel.ActiveQuestion!;
        Assert.That(viewModel.Timeline.OfType<AgentQuestionItem>(), Is.Empty);
        Assert.That(viewModel.ShowComposer, Is.False);
        question.Advance();
        Assert.That(question.CanAdvance, Is.False);
        question.Questions[1].Text = "Opening";
        question.Advance();
        question.Questions[2].Text = "secret-value";
        question.Advance();
        await sending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(decisions, Is.EqualTo(new[]
            {
                AgentPermissionDecision.AllowAlways, AgentPermissionDecision.AllowAlways
            }));
            Assert.That(viewModel.ActivePermission, Is.Null);
            Assert.That(viewModel.HasActivePermission, Is.False);
            Assert.That(viewModel.Timeline.OfType<AgentPermissionItem>().Single(), Is.SameAs(card));
            Assert.That(card.IsPending, Is.False);
            Assert.That(card.ResolutionText, Is.EqualTo($"{AgentStrings.AllowAlways} · Write a file"));
            Assert.That(answers!["color"], Is.EqualTo(new[] { "Blue" }));
            Assert.That(answers!["label"], Is.EqualTo(new[] { "Opening" }));
            Assert.That(answers!["secret"], Is.EqualTo(new[] { "secret-value" }));
            Assert.That(viewModel.ActiveQuestion, Is.Null);
            Assert.That(viewModel.ShowComposer, Is.True);
            Assert.That(viewModel.Timeline.OfType<AgentQuestionItem>().Single(), Is.SameAs(question));
            Assert.That(question.IsPending, Is.False);
            Assert.That(question.ResolutionText, Is.EqualTo($"{AgentStrings.Answered} · Blue, Opening"));
            Assert.That(question.Questions[2].Text, Is.Null);
        });

        using var reopened = workspace.CreateWorkspace(workspace.Editor);
        await reopened.InitializeAsync();
        Assert.That(reopened.Timeline.OfType<AgentQuestionItem>().Single().ResolutionText, Is.EqualTo(AgentStrings.Answered));
        var stored = await workspace.Runtime.LoadTimelineAsync(workspace.ProjectPath, viewModel.SelectedThread!.Id);
        Assert.That(stored.OfType<AgentUserQuestionResolved>().Single().Answered, Is.True);
    }

    [AvaloniaTest]
    public async Task SubagentTimelineScopesStreamsAndReparentsLateChildrenAfterReload()
    {
        await using var workspace = new AgentTabTestWorkspace();
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        workspace.Bridge.Script = (request, _) =>
        {
            var (thread, turn, _, _) = request;
            var now = DateTimeOffset.UtcNow;
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now, "read", AgentToolStatus.Completed,
                [new("text", "Read layer timings")], ParentToolCallId: "agent"));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "orphan", "Orphan", ParentToolCallId: "unknown"));
            workspace.Bridge.Emit(new AgentAssistantTextDelta(thread, turn, now, "Worker output", "shared", "agent"));
            workspace.Bridge.Emit(new AgentReasoningDelta(thread, turn, now, "Worker reasoning", "agent"));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "agent", "Agent", Subagent: new("reviewer", "Inspect scene")));
            workspace.Bridge.Emit(new AgentToolCallStarted(thread, turn, now, "read", "read_scene", ParentToolCallId: "agent"));
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, now, "Parent response", "shared"));
            workspace.Bridge.Emit(new AgentAssistantMessageCompleted(thread, turn, now, "Worker output", "shared", "agent"));
            workspace.Bridge.Emit(new AgentToolCallCompleted(thread, turn, now, "agent", AgentToolStatus.Completed));
            return Task.CompletedTask;
        };

        viewModel.Prompt = "Review this scene";
        await viewModel.SendAsync();
        AssertHierarchy(viewModel);

        using var reopened = workspace.CreateWorkspace(workspace.Editor);
        await reopened.InitializeAsync();
        AssertHierarchy(reopened);

        static void AssertHierarchy(AgentTabViewModel model)
        {
            var agent = model.Timeline.OfType<AgentToolItem>().Single(item => item.IsSubagent);
            Assert.Multiple(() =>
            {
                Assert.That(model.Timeline.OfType<AgentToolItem>().Select(item => item.ToolName), Is.EquivalentTo(new[] { "Agent", "Orphan" }));
                Assert.That(model.Timeline.OfType<AgentAssistantItem>().Single().Text, Is.EqualTo("Parent response"));
                Assert.That(model.Timeline.OfType<AgentReasoningItem>(), Is.Empty);
                Assert.That(agent.Children.OfType<AgentAssistantItem>().Single().Text, Is.EqualTo("Worker output"));
                Assert.That(agent.Children.OfType<AgentReasoningItem>().Single().Text, Is.EqualTo("Worker reasoning"));
                Assert.That(agent.Children.OfType<AgentToolItem>().Single().Output, Is.EqualTo("Read layer timings"));
                Assert.That(agent.Title, Is.EqualTo("Inspect scene"));
                Assert.That(model.Subagents.Counter, Is.EqualTo("1/1"));
            });
        }
    }

    [AvaloniaTest]
    public async Task TasksFromTwoTabsOnlyShowTheirOwnPermissionCards()
    {
        await using var workspace = new AgentTabTestWorkspace { DefaultPermissionMode = AgentPermissionMode.Supervised };
        using var left = workspace.CreateWorkspace(workspace.Editor);
        using var right = workspace.CreateWorkspace(workspace.Editor);
        await Task.WhenAll(left.InitializeAsync(), right.InitializeAsync());
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, AgentPermissionDecision>();
        workspace.Bridge.Script = async (request, ct) =>
        {
            results[request.Text] = await workspace.Bridge.AskPermissionAsync(request, request.Text, null, ct);
        };

        left.Prompt = "First permission";
        right.Prompt = "Second permission";
        Task first = left.SendAsync();
        Task second = right.SendAsync();
        await HeadlessTestHelpers.WaitUntilAsync(() =>
            left.HasActivePermission && right.HasActivePermission);
        var leftCard = left.ActivePermission!;
        var rightCard = right.ActivePermission!;
        Assert.That(leftCard.ToolName, Is.EqualTo("First permission"));
        Assert.That(rightCard.ToolName, Is.EqualTo("Second permission"));
        leftCard.Respond(AgentPermissionDecision.RejectOnce);
        rightCard.Respond(AgentPermissionDecision.AllowOnce);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(results["First permission"], Is.EqualTo(AgentPermissionDecision.RejectOnce));
        Assert.That(results["Second permission"], Is.EqualTo(AgentPermissionDecision.AllowOnce));
    }

    [AvaloniaTest]
    public async Task OverlappingPermissionsAreImmediatelyAvailable()
    {
        await using var workspace = new AgentTabTestWorkspace { DefaultPermissionMode = AgentPermissionMode.Supervised };
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        var askNext = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AgentPermissionDecision[]? decisions = null;
        workspace.Bridge.Script = async (request, ct) =>
        {
            var first = workspace.Bridge.AskPermissionAsync(request, "First tool", null, ct);
            await askNext.Task.WaitAsync(ct);
            var second = workspace.Bridge.AskPermissionAsync(request, "Second tool", null, ct);
            nextRequested.SetResult();
            decisions = await Task.WhenAll(first, second);
        };

        viewModel.Prompt = "Run two tools";
        Task sending = viewModel.SendAsync();
        await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.HasActivePermission);
        var firstCard = viewModel.ActivePermission!;
        Assert.That(firstCard.ToolName, Is.EqualTo("First tool"));
        askNext.SetResult();
        await nextRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.PendingPermissions.Count == 2);
        Assert.That(viewModel.ActivePermission, Is.SameAs(firstCard));
        Assert.That(viewModel.PermissionQueuePosition, Is.EqualTo("1 / 2"));
        Assert.That(viewModel.Timeline.OfType<AgentPermissionItem>(), Is.Empty);

        firstCard.Respond(AgentPermissionDecision.AllowOnce);
        await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.ActivePermission?.ToolName == "Second tool");
        var secondCard = viewModel.ActivePermission!;
        Assert.That(viewModel.Timeline.OfType<AgentPermissionItem>().Single(), Is.SameAs(firstCard));
        Assert.That(firstCard.IsPending, Is.False);
        secondCard.Respond(AgentPermissionDecision.RejectOnce);

        await sending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(viewModel.HasActivePermission, Is.False);
        Assert.That(viewModel.Timeline.OfType<AgentPermissionItem>(), Is.EqualTo(new[] { firstCard, secondCard }));
        Assert.That(decisions, Is.EqualTo(new[] { AgentPermissionDecision.AllowOnce, AgentPermissionDecision.RejectOnce }));
    }

    [AvaloniaTest]
    public async Task RestoresTheSelectedThreadAndCanRenameSwitchAndDeleteThreads()
    {
        await using var workspace = new AgentTabTestWorkspace();
        var state = new JsonObject();
        using (var viewModel = workspace.CreateWorkspace(workspace.Editor))
        {
            await viewModel.InitializeAsync();
            viewModel.Prompt = "First conversation";
            await viewModel.SendAsync();
            var first = viewModel.SelectedThread!;
            await viewModel.NewThreadAsync();
            viewModel.Prompt = "Second conversation";
            await viewModel.SendAsync();
            await viewModel.SelectThreadAsync(first);
            await viewModel.RenameThreadAsync(first, "First renamed");
            viewModel.WriteToJson(state);
        }

        using var restored = workspace.CreateWorkspace(workspace.Editor);
        restored.ReadFromJson(state);
        await restored.InitializeAsync();
        Assert.That(restored.SelectedThread!.Title, Is.EqualTo("First renamed"));
        Assert.That(restored.Timeline.OfType<AgentUserItem>().Single().Text, Is.EqualTo("First conversation"));
        await restored.DeleteThreadAsync(restored.SelectedThread!);
        Assert.That(restored.Threads.Select(thread => thread.Title), Is.EqualTo(new[] { "Second conversation" }));
        Assert.That(restored.Timeline.OfType<AgentUserItem>().Single().Text, Is.EqualTo("Second conversation"));
    }

    [AvaloniaTest]
    public async Task FiltersProjectThreadsAndKeepsTheSelectionWhenEditingAnotherConversation()
    {
        await using var workspace = new AgentTabTestWorkspace();
        var codex = workspace.GetProviders().Single();
        var older = await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, codex, "Opening title", AgentPermissionMode.Supervised);
        await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, AgentProviderDescriptor.ClaudeCode, "Cutaway pass", AgentPermissionMode.Supervised);
        var newest = await workspace.Runtime.CreateThreadAsync(workspace.ProjectPath, codex, "End card", AgentPermissionMode.Supervised);
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();

        viewModel.ThreadSearch = "  OPENING  ";
        Assert.That(viewModel.FilteredThreads.Select(row => row.Thread.Id), Is.EqualTo(new[] { older.Id }));
        viewModel.ThreadSearch = "CoDeX";
        Assert.That(viewModel.FilteredThreads.Select(row => row.Thread.Id), Is.EqualTo(new[] { newest.Id, older.Id }));
        Assert.That(viewModel.RecentThreads, Has.Count.EqualTo(3));

        viewModel.ThreadSearch = "revised";
        Assert.That(viewModel.FilteredThreads, Is.Empty);
        await viewModel.RenameThreadAsync(older, "Revised opening");
        Assert.That(viewModel.FilteredThreads.Single().Thread.Title, Is.EqualTo("Revised opening"));
        Assert.That(viewModel.RecentThreads[0].Thread.Id, Is.EqualTo(older.Id));
        Assert.That(viewModel.SelectedThread?.Id, Is.EqualTo(newest.Id));

        await viewModel.DeleteThreadAsync(older);
        Assert.That(viewModel.FilteredThreads, Is.Empty);
        Assert.That(viewModel.SelectedThread?.Id, Is.EqualTo(newest.Id));
        viewModel.ThreadSearch = " ";
        Assert.That(viewModel.FilteredThreads, Has.Count.EqualTo(2));
    }

    [AvaloniaTest]
    public async Task StarterPromptFillsAnUnsentDraft()
    {
        await using var workspace = new AgentTabTestWorkspace();
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();

        viewModel.ApplyStarterPrompt(viewModel.StarterPrompts[0]);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Prompt, Is.EqualTo(AgentStrings.StarterReviewFramePrompt));
            Assert.That(viewModel.CanSend, Is.True);
            Assert.That(viewModel.ShowEmptyState, Is.True);
            Assert.That(viewModel.Timeline, Is.Empty);
        });
    }

    [AvaloniaTest]
    public async Task StopCancelsAnOutstandingPromptAndClosesItsCard()
    {
        await using var workspace = new AgentTabTestWorkspace { DefaultPermissionMode = AgentPermissionMode.Supervised };
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        workspace.Bridge.Script = async (request, ct) =>
        {
            await workspace.Bridge.AskPermissionAsync(request, "Waiting for permission", null, ct);
        };

        viewModel.Prompt = "Stop this";
        Task sending = viewModel.SendAsync();
        await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.HasActivePermission);
        var card = viewModel.ActivePermission!;
        Assert.That(viewModel.CanStop, Is.True);
        await viewModel.StopAsync();
        await sending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(viewModel.HasActivePermission, Is.False);
        Assert.That(card.IsPending, Is.False);
        Assert.That(card.ResolutionText, Is.EqualTo(AgentStrings.RequestClosed));
        Assert.That(viewModel.Timeline.OfType<AgentPermissionItem>().Single(), Is.SameAs(card));
        Assert.That(viewModel.IsRunning, Is.False);
        Assert.That(viewModel.Timeline.OfType<AgentNoticeItem>().Single().Message, Is.EqualTo(AgentStrings.TurnStopped));
    }

    [AvaloniaTest]
    public async Task DisposeDetachesWithoutClosingAnOutstandingPermission()
    {
        await using var workspace = new AgentTabTestWorkspace { DefaultPermissionMode = AgentPermissionMode.Supervised };
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        workspace.Bridge.Script = async (request, ct) =>
        {
            await workspace.Bridge.AskPermissionAsync(request, "Waiting for permission", null, ct);
        };

        viewModel.Prompt = "Run a tool";
        Task sending = viewModel.SendAsync();
        await HeadlessTestHelpers.WaitUntilAsync(() => viewModel.HasActivePermission);
        var card = viewModel.ActivePermission!;

        viewModel.Dispose();
        Assert.That(viewModel.HasActivePermission, Is.False);
        Assert.That(viewModel.Timeline, Is.Empty);
        using var reattached = workspace.CreateWorkspace(workspace.Editor);
        await reattached.InitializeAsync();
        Assert.That(reattached.ActivePermission, Is.SameAs(card));
        Assert.That(card.IsPending, Is.True);
        card.Respond(AgentPermissionDecision.AllowOnce);
        await sending.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [AvaloniaTest]
    public async Task RestoresEditCountsAndUsesTheHostResultWhenReverting()
    {
        await using var workspace = new AgentTabTestWorkspace { TransactionCount = 2 };
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        viewModel.Prompt = "Make two edits";
        await viewModel.SendAsync();
        var thread = viewModel.SelectedThread!;
        await viewModel.NewThreadAsync();
        await viewModel.SelectThreadAsync(thread);
        var history = viewModel.Timeline.OfType<AgentHistoryItem>().Single();
        Assert.That(history.TransactionCount, Is.EqualTo(2));
        Assert.That(history.CanRevert, Is.True);

        workspace.RevertResult = AgentTurnRevertResult.HistoryChanged;
        await history.RevertAsync();
        Assert.That(history.Result, Is.EqualTo(AgentStrings.HistoryChanged));
        workspace.RevertResult = AgentTurnRevertResult.Reverted;
        await history.RevertAsync();
        Assert.That(history.Result, Is.EqualTo(AgentStrings.TurnReverted));
        Assert.That(history.CanRevert, Is.False);
    }

    [AvaloniaTest]
    public async Task SendsAttachmentsWithTheCurrentContextToggleAndKeepsFollowEditsInSync()
    {
        await using var workspace = new AgentTabTestWorkspace();
        using var viewModel = workspace.CreateWorkspace(workspace.Editor);
        await viewModel.InitializeAsync();
        viewModel.FollowLiveMcpEdits = true;
        Assert.That(workspace.FollowLiveMcpEdits.Value, Is.True);
        workspace.FollowLiveMcpEdits.Value = false;
        Assert.That(viewModel.FollowLiveMcpEdits, Is.False);

        await viewModel.CaptureFrameAsync();
        await viewModel.PickFilesAsync(() => Task.FromResult<IReadOnlyList<string>>([Path.Combine(workspace.Root, "notes.txt")]));
        viewModel.Attachments.Last().Remove();
        viewModel.IncludeSceneContext = false;
        viewModel.Prompt = "Review the attachment";
        await viewModel.SendAsync();
        Assert.That(workspace.IncludedScene, Is.False);
        Assert.That(workspace.SentAttachments.Single().MediaType, Is.EqualTo("image/png"));
        Assert.That(viewModel.Attachments, Is.Empty);
    }
}

internal sealed class AgentTabTestWorkspace : IAgentWorkspaceHost, IAsyncDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("beutl-agent-tab-");
    private readonly AgentAttachmentStore _attachmentStore;
    private Guid? _revertibleTurn;

    public AgentTabTestWorkspace(IAgentNotificationSoundPlayer? soundPlayer = null, bool probeVersions = false)
    {
        _attachmentStore = new(Root);
        Runtime = new AgentRuntimeManager(Bridge, new AgentThreadStore(Root), new AgentBinaryLocator(probeVersions));
        ThreadSessions = new(Root, soundPlayer);
    }

    public AgentRuntimeManager Runtime { get; }
    public AgentThreadSessionHub ThreadSessions { get; }
    public AgentTabTestBridge Bridge { get; } = new();
    public IEditorContext Editor { get; init; } = Mock.Of<IEditorContext>();
    public string Root => _directory.FullName;
    public string ProjectPath => Path.Combine(Root, "test.beutl");
    public IProperty<bool> FollowLiveMcpEdits { get; } = Property.CreateValue(false);
    public string DefaultProviderId { get; init; } = "";
    public AgentPermissionMode DefaultPermissionMode { get; init; } = AgentPermissionMode.Auto;
    public string? DefaultModel { get; init; }
    public string? DefaultReasoningEffort { get; init; }
    public bool EnableNotifications { get; set; } = true;
    public bool EnableNotificationSound { get; set; } = true;
    public Window? SettingsOwner { get; private set; }
    public bool SteerOnSend
    {
        get;
        set
        {
            field = value;
            RuntimeSettingsChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SteerOnSend)));
        }
    }
    public event EventHandler<PropertyChangedEventArgs>? RuntimeSettingsChanged;
    public AgentComposerSendKey ComposerSendKey
    {
        get;
        set
        {
            field = value;
            RuntimeSettingsChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ComposerSendKey)));
        }
    }
    public int TransactionCount { get; init; }
    public IReadOnlyList<string>? TransactionChanges { get; init; }
    public AgentTurnRevertResult RevertResult { get; set; } = AgentTurnRevertResult.Reverted;
    public bool IncludedScene { get; private set; }
    public IReadOnlyList<AgentAttachedFile> SentAttachments { get; private set; } = [];
    public List<(string Prompt, IEditorContext Editor, bool IncludeScene, IReadOnlyList<AgentAttachedFile> Attachments)> Submissions { get; } = [];

    public IReadOnlyList<AgentProviderDescriptor> ProviderDescriptors { get; init; } =
        [AgentProviderDescriptor.Codex with { Command = Environment.ProcessPath! }];

    public IReadOnlyList<AgentProviderDescriptor> GetProviders() => ProviderDescriptors;
    public IReadOnlyList<AgentComposerSuggestion> BundledSkills { get; init; } = [];

    public AgentTabViewModel CreateWorkspace(IEditorContext editor) => new(this, editor, ProjectPath);

    public Task OpenAiAgentSettingsAsync(Window owner)
    {
        SettingsOwner = owner;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AgentMentionCandidate>> GetMentionCandidatesAsync(
        IEditorContext editor, string projectPath, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentMentionCandidate>>([]);

    public async Task<AgentTurnSummary> RunTurnAsync(
        IEditorContext editor, string projectPath, AgentProviderDescriptor provider, AgentTurnRequest request,
        bool includeSceneContext, IReadOnlyList<AgentAttachedFile> attachments, IReadOnlyList<AgentContextMention>? mentions,
        AgentTurnCallbacks callbacks,
        CancellationToken cancellationToken)
    {
        IncludedScene = includeSceneContext;
        SentAttachments = attachments;
        Submissions.Add((request.Text, editor, includeSceneContext, attachments));
        var result = await Runtime.RunTurnAsync(projectPath, provider, request,
            [new StdioMcpServerSpec("beutl", "test")], callbacks, cancellationToken);
        if (TransactionCount > 0)
        {
            _revertibleTurn = result.TurnId;
            var entry = new AgentTurnHistoryRecorded(request.ThreadId, result.TurnId, DateTimeOffset.UtcNow, TransactionCount, TransactionChanges);
            await Runtime.RecordTurnHistoryAsync(projectPath, entry);
            callbacks.Timeline(entry);
        }
        return result;
    }

    public Task<bool> SteerTurnAsync(IEditorContext editor, string projectPath, AgentTurnRequest request,
        bool includeSceneContext, IReadOnlyList<AgentAttachedFile> attachments, IReadOnlyList<AgentContextMention>? mentions,
        CancellationToken cancellationToken)
    {
        Submissions.Add((request.Text, editor, includeSceneContext, attachments));
        return Runtime.SteerTurnAsync(request, cancellationToken);
    }

    public Task<AgentAttachedFile> CaptureFrameAsync(IEditorContext editor, CancellationToken cancellationToken) =>
        Task.FromResult(new AgentAttachedFile(Path.Combine(Root, "frame.png"), "image/png"));

    public Task<AgentAttachedFile> SavePastedImageAsync(Avalonia.Media.Imaging.Bitmap image, CancellationToken cancellationToken) =>
        _attachmentStore.SavePastedImageAsync(image, cancellationToken);

    public Task<AgentAttachedFile> SavePastedTextAsync(string text, CancellationToken cancellationToken) =>
        _attachmentStore.SavePastedTextAsync(text, cancellationToken);

    public bool CanRevertTurn(IEditorContext editor, Guid turnId) => _revertibleTurn == turnId;

    public Task<AgentTurnRevertResult> RevertTurnAsync(IEditorContext editor, Guid turnId, CancellationToken cancellationToken)
    {
        if (RevertResult == AgentTurnRevertResult.Reverted)
            _revertibleTurn = null;
        return Task.FromResult(RevertResult);
    }

    public async ValueTask DisposeAsync()
    {
        await ThreadSessions.DisposeAsync();
        await Runtime.DisposeAsync();
        _directory.Delete(recursive: true);
    }
}

internal sealed class AgentTabTestBridge : IAgentRuntimeBridge
{
    public event Action<AgentTimelineEvent>? TimelineEventReceived;
    public event Action<Exception>? Disconnected { add { } remove { } }
    public Func<AgentPermissionRequest, CancellationToken, Task<AgentPermissionDecision>>? PermissionHandler { get; set; }
    public Func<AgentUserQuestionRequest, CancellationToken, Task<IReadOnlyDictionary<string, string[]>>>? QuestionHandler { get; set; }
    public Func<AgentTurnRequest, CancellationToken, Task>? Script { get; set; }
    public bool SupportsSteer { get; set; } = true;
    public Func<Guid, Guid, string, AgentBackgroundStopResult>? StopTask { get; set; }
    public Func<Guid, Guid, CancellationToken, Task<AgentCompactionResult>>? Compact { get; set; }
    public AgentSessionOpenRequest? OpenedSession { get; private set; }
    public Func<AgentModelListRequest, IReadOnlyList<AgentModelDescriptor>>? ModelCatalog { get; set; }
    public Func<AgentComposerSuggestionRequest, IReadOnlyList<AgentComposerSuggestion>>? SuggestionCatalog { get; set; }

    public void Emit(AgentTimelineEvent entry) => TimelineEventReceived?.Invoke(entry);

    public async Task<AgentTurnSummary> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
    {
        var (thread, turn, text, _) = request;
        Emit(new AgentTurnStarted(thread, turn, DateTimeOffset.UtcNow));
        Emit(new AgentUserMessage(thread, turn, DateTimeOffset.UtcNow, text, request.Attachments, request.IncludeSceneContext, request.Mentions));
        if (Script is { } script)
            await script(request, cancellationToken);
        Emit(new AgentTurnCompleted(thread, turn, DateTimeOffset.UtcNow));
        return new AgentTurnSummary(turn, AgentTurnStatus.Completed);
    }

    public async Task<AgentPermissionDecision> AskPermissionAsync(AgentTurnRequest turn, string tool, string? server,
        CancellationToken ct, string? description = null, IReadOnlyList<AgentPermissionOption>? options = null)
    {
        var request = new AgentPermissionRequest(Guid.NewGuid().ToString(), turn.ThreadId, turn.TurnId, tool, server,
            Description: description, Options: options);
        Emit(new AgentPermissionRequested(turn.ThreadId, turn.TurnId, DateTimeOffset.UtcNow, request));
        var decision = await PermissionHandler!(request, ct);
        Emit(new AgentPermissionResolved(turn.ThreadId, turn.TurnId, DateTimeOffset.UtcNow, request.RequestId, decision));
        return decision;
    }

    public async Task<IReadOnlyDictionary<string, string[]>> AskQuestionAsync(AgentTurnRequest turn, IReadOnlyList<AgentQuestion> questions, CancellationToken ct)
    {
        var request = new AgentUserQuestionRequest(Guid.NewGuid().ToString(), turn.ThreadId, turn.TurnId, questions);
        Emit(new AgentUserQuestionRequested(turn.ThreadId, turn.TurnId, DateTimeOffset.UtcNow, request));
        bool answered = false;
        try
        {
            var answers = await QuestionHandler!(request, ct);
            answered = answers.Count > 0;
            return answers;
        }
        finally
        {
            Emit(new AgentUserQuestionResolved(turn.ThreadId, turn.TurnId, DateTimeOffset.UtcNow, request.RequestId, answered));
        }
    }

    public Task<AgentWorkerHealth> PingAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<AgentModelDescriptor>> ListModelsAsync(AgentModelListRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(ModelCatalog?.Invoke(request) ?? []);
    public Task<IReadOnlyList<AgentComposerSuggestion>> ListComposerSuggestionsAsync(
        AgentComposerSuggestionRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(SuggestionCatalog?.Invoke(request) ?? []);
    public Task<AgentSessionInfo> OpenSessionAsync(AgentSessionOpenRequest request, CancellationToken cancellationToken = default)
    {
        OpenedSession = request;
        return Task.FromResult(new AgentSessionInfo(request.ExternalSessionId));
    }
    public Task<bool> SteerTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken = default)
    {
        if (SupportsSteer)
            Emit(new AgentUserMessage(request.ThreadId, request.TurnId, DateTimeOffset.UtcNow, request.Text,
                request.Attachments, request.IncludeSceneContext, request.Mentions));
        return Task.FromResult(SupportsSteer);
    }
    public Task<AgentBackgroundStopResult> StopBackgroundTaskAsync(
        Guid threadId, Guid turnId, string toolCallId, CancellationToken cancellationToken = default)
        => Task.FromResult(StopTask?.Invoke(threadId, turnId, toolCallId) ?? AgentBackgroundStopResult.Unsupported);
    public Task<AgentCompactionResult> CompactContextAsync(Guid threadId, Guid turnId, CancellationToken cancellationToken = default)
        => Compact?.Invoke(threadId, turnId, cancellationToken) ?? Task.FromResult(new AgentCompactionResult(AgentCompactionStatus.Unsupported));
    public Task CancelTurnAsync(Guid threadId, Guid turnId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetPermissionModeAsync(Guid threadId, AgentPermissionMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CloseSessionAsync(Guid threadId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
