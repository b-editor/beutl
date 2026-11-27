using Beutl.Agents;

namespace Beutl.Agents.Tests;

public sealed class AgentThreadStoreTests
{
    [Test]
    public async Task PersistsSentAttachmentsTurnMetricsAndCheckpointChangesWithLegacyDefaults()
    {
        var directory = Directory.CreateTempSubdirectory("beutl-agent-phase3-store-");
        try
        {
            string project = Path.Combine(directory.FullName, "project.beutl");
            var store = new AgentThreadStore(directory.FullName);
            var thread = await store.CreateAsync(project, "claude", "Opening title", AgentPermissionMode.Auto);
            Guid turn = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            AgentUserAttachment[] attachments =
            [
                new(Path.Combine(directory.FullName, "frame.png"), "Current frame", "image/png", "PNG", true),
                new(Path.Combine(directory.FullName, "notes.txt"), "Pasted text", "text/plain", "30 lines", false),
            ];
            var completed = new AgentTurnCompleted(thread.Id, turn, now, 3460, 1000, 240, 800, "claude-opus");
            var phase = new AgentTurnPhaseChanged(thread.Id, turn, now, AgentTurnPhase.Compacting);
            var usage = new AgentTurnUsageUpdated(thread.Id, turn, now, 1000, 240, 800, "claude-opus");
            AgentTimelineEvent[] entries =
            [
                new AgentUserMessage(thread.Id, turn, now, "Use these references", attachments),
                new AgentUserMessage(thread.Id, turn, now, "A legacy prompt"),
                phase, usage, completed,
                new AgentTurnHistoryRecorded(thread.Id, turn, now, 2, ["Move title", "Add fade"]),
                new AgentTurnHistoryRecorded(thread.Id, turn, now, 1),
                new AgentTurnCompleted(thread.Id, turn, now),
            ];
            foreach (var entry in entries)
                await store.AppendAsync(project, entry);
            var timeline = await new AgentThreadStore(directory.FullName).ReadTimelineAsync(project, thread.Id);
            Assert.Multiple(() =>
            {
                Assert.That(((AgentUserMessage)timeline[0]).Attachments, Is.EqualTo(attachments));
                Assert.That(((AgentUserMessage)timeline[1]).Attachments, Is.Null);
                Assert.That(timeline.Skip(2).Take(3), Is.EqualTo(new AgentTimelineEvent[] { phase, usage, completed }));
                Assert.That(((AgentTurnHistoryRecorded)timeline[5]).Changes, Is.EqualTo(new[] { "Move title", "Add fade" }));
                Assert.That(((AgentTurnHistoryRecorded)timeline[6]).Changes, Is.Null);
                Assert.That(((AgentTurnCompleted)timeline[7]).DurationMs, Is.Null);
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ForkAndRewindSaveTheRequestedTurnBoundaryAndClearResumeState()
    {
        var directory = Directory.CreateTempSubdirectory("beutl-agent-fork-");
        try
        {
            string project = Path.Combine(directory.FullName, "project.beutl");
            var store = new AgentThreadStore(directory.FullName);
            var thread = await store.CreateAsync(project, "codex", "Opening", AgentPermissionMode.Supervised, "gpt-6.2-sol");
            Guid first = Guid.NewGuid();
            Guid second = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await store.AppendAsync(project, new AgentSessionStateChanged(thread.Id, null, now, AgentSessionState.Open, "old-session"));
            await store.AppendAsync(project, new AgentTurnStarted(thread.Id, first, now));
            await store.AppendAsync(project, new AgentUserMessage(thread.Id, first, now, "First prompt"));
            await store.AppendAsync(project, new AgentUserMessage(thread.Id, first, now, "Steered prompt"));
            await store.AppendAsync(project, new AgentPermissionRequested(thread.Id, first, now, new("approval", thread.Id, first, "Edit")));
            await store.AppendAsync(project, new AgentTurnCompleted(thread.Id, first, now, 234));
            await store.AppendAsync(project, new AgentTurnHistoryRecorded(thread.Id, first, now, 1, ["Add title"]));
            await store.SetModelAsync(project, thread.Id, thread.Model, "high", CancellationToken.None);
            await store.AppendAsync(project, new AgentTurnStarted(thread.Id, second, now));
            await store.AppendAsync(project, new AgentUserMessage(thread.Id, second, now, "Second prompt"));
            await store.AppendAsync(project, new AgentTurnCompleted(thread.Id, second, now));

            var fork = await store.ForkThreadAsync(project, thread.Id, first, "Fork");
            var beforeFirst = await store.ForkThreadAsync(project, thread.Id, first, "Before first", includeTurn: false);
            var reopened = new AgentThreadStore(directory.FullName);
            var prefix = await reopened.ReadTimelineAsync(project, fork.Id);
            Assert.That(await reopened.ReadTimelineAsync(project, beforeFirst.Id), Is.Empty);
            Assert.Multiple(() =>
            {
                Assert.That(fork.Id, Is.Not.EqualTo(thread.Id));
                Assert.That(fork.Model, Is.EqualTo(thread.Model));
                Assert.That(fork.ExternalSessionId, Is.Null);
                Assert.That(fork.RestorePreviousMessages, Is.True);
                Assert.That(prefix.All(entry => entry.ThreadId == fork.Id && entry.TurnId == first), Is.True);
                Assert.That(prefix.OfType<AgentPermissionRequested>().Single().Request.ThreadId, Is.EqualTo(fork.Id));
                Assert.That(prefix.OfType<AgentUserMessage>().Select(entry => entry.Text), Is.EqualTo(new[] { "First prompt", "Steered prompt" }));
                Assert.That(prefix.Last(), Is.TypeOf<AgentTurnHistoryRecorded>());
            });
            Assert.That((await reopened.GetAsync(project, thread.Id))!.ExternalSessionId, Is.EqualTo("old-session"));
            Assert.That((await reopened.ReadTimelineAsync(project, thread.Id)).OfType<AgentTurnCompleted>(), Has.Exactly(2).Items);

            var rewound = await reopened.RewindTurnAsync(project, thread.Id, second);
            Assert.That(rewound.Thread.ExternalSessionId, Is.Null);
            Assert.That(rewound.Timeline.Last(), Is.TypeOf<AgentThreadModelChanged>());
            Assert.That(rewound.Timeline.SkipLast(1).Select(entry => entry.GetType()), Is.EqualTo(prefix.Select(entry => entry.GetType())));
            Assert.That(await store.GetAsync(project, thread.Id), Is.EqualTo(rewound.Thread));
            Assert.That((await store.ReadTimelineAsync(project, thread.Id)).Where(entry => entry.TurnId.HasValue)
                .Select(entry => entry.TurnId), Is.All.EqualTo(first));
            await Assert.ThatAsync(() => store.RewindTurnAsync(project, thread.Id, second), Throws.TypeOf<KeyNotFoundException>());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ReloadsTimelineAndSessionSeparatelyForEachProject()
    {
        var directory = Directory.CreateTempSubdirectory("beutl-agent-store-");
        try
        {
            string project = Path.Combine(directory.FullName, "first.beutl");
            string otherProject = Path.Combine(directory.FullName, "second.beutl");
            var store = new AgentThreadStore(directory.FullName);
            var thread = await store.CreateAsync(project, "codex", "Original", AgentPermissionMode.Supervised);
            var turnId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var user = new AgentUserMessage(thread.Id, turnId, now, "第一行\nsecond line");
            var session = new AgentSessionStateChanged(thread.Id, null, now, AgentSessionState.Open, "provider-session");
            await store.AppendAsync(project, user);
            await store.AppendAsync(project, session);

            var reopened = new AgentThreadStore(directory.FullName);
            string index = directory.GetFiles("index.json", SearchOption.AllDirectories).Single().FullName;
            Assert.That(await File.ReadAllTextAsync(index), Does.Not.Contain("reasoningEffort"));
            Assert.That((await reopened.GetAsync(project, thread.Id))!.ReasoningEffort, Is.Null);
            await reopened.RenameAsync(project, thread.Id, "Renamed");
            await reopened.SetPermissionModeAsync(project, thread.Id, AgentPermissionMode.Auto);
            var (_, change) = await reopened.SetModelAsync(project, thread.Id, "gpt-6.2-sol", "high", CancellationToken.None);
            Assert.That(change, Is.Not.Null);
            var (_, unchanged) = await reopened.SetModelAsync(project, thread.Id, "gpt-6.2-sol", "high", CancellationToken.None);
            Assert.That(unchanged, Is.Null);
            var metadata = (await new AgentThreadStore(directory.FullName).ListAsync(project)).Single();
            Assert.Multiple(() =>
            {
                Assert.That(metadata.ExternalSessionId, Is.EqualTo("provider-session"));
                Assert.That(metadata.Title, Is.EqualTo("Renamed"));
                Assert.That(metadata.PermissionMode, Is.EqualTo(AgentPermissionMode.Auto));
                Assert.That(metadata.Model, Is.EqualTo("gpt-6.2-sol"));
                Assert.That(metadata.ReasoningEffort, Is.EqualTo("high"));
            });
            Assert.That(await reopened.ReadTimelineAsync(project, thread.Id), Is.EqualTo(new AgentTimelineEvent[] { user, session, change! }));
            Assert.That(await reopened.ListAsync(otherProject), Is.Empty);
            Assert.That(await reopened.DeleteAsync(project, thread.Id), Is.True);
            Assert.That(await reopened.GetAsync(project, thread.Id), Is.Null);
            Assert.That(await reopened.ReadTimelineAsync(project, thread.Id), Is.Empty);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ReloadsScopedEventsAndReadsLegacyAndRichQuestionOptions()
    {
        var directory = Directory.CreateTempSubdirectory("beutl-agent-nesting-");
        try
        {
            string project = Path.Combine(directory.FullName, "scene.beutl");
            var store = new AgentThreadStore(directory.FullName);
            var thread = await store.CreateAsync(project, "claude", "Review this scene", AgentPermissionMode.Auto);
            Guid turn = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var subagent = new AgentToolCallStarted(thread.Id, turn, now, "agent", "Agent", Subagent: new("reviewer", "Review titles"));
            var nested = new AgentToolCallStarted(thread.Id, turn, now, "shell", "Bash", ParentToolCallId: "agent",
                IsBackground: true, BackgroundTitle: "render-preview");
            var updated = new AgentToolCallUpdated(thread.Id, turn, now, "shell", AgentToolStatus.Running, "Rendering", "agent");
            var completed = new AgentToolCallCompleted(thread.Id, turn, now, "shell", AgentToolStatus.Completed, ParentToolCallId: "agent");
            var chunk = new AgentAssistantTextDelta(thread.Id, turn, now, "Looking", "message", "agent");
            var response = new AgentAssistantMessageCompleted(thread.Id, turn, now, "Looks good", "message", "agent");
            var reasoning = new AgentReasoningDelta(thread.Id, turn, now, "Check timing", "agent");
            var rich = new AgentQuestion("q", "Which effects?", [new("Fade", "Smooth out the cut")], Header: "Effects", MultiSelect: true);

            AgentTimelineEvent[] events = [subagent, nested, updated, completed, chunk, response, reasoning];
            foreach (var entry in events)
                await store.AppendAsync(project, entry);
            await store.AppendAsync(project, new AgentUserQuestionRequested(thread.Id, turn, now, new("rich", thread.Id, turn, [rich])));
            await store.AppendAsync(project, new AgentUserQuestionResolved(thread.Id, turn, now, "rich", true));
            string log = directory.GetFiles("*.jsonl", SearchOption.AllDirectories).Single().FullName;
            await File.AppendAllTextAsync(log, $$$"""
                {"type":"userQuestionRequested","threadId":"{{{thread.Id}}}","turnId":"{{{turn}}}","timestamp":"{{{now:O}}}","request":{"requestId":"legacy","threadId":"{{{thread.Id}}}","turnId":"{{{turn}}}","questions":[{"id":"legacy","text":"Which?","options":["A","B"]}]}}
                """ + "\n");

            var timeline = await new AgentThreadStore(directory.FullName).ReadTimelineAsync(project, thread.Id);
            Assert.That(timeline.Take(events.Length), Is.EqualTo(events));
            Assert.That(timeline.OfType<AgentUserQuestionResolved>().Single().Answered, Is.True);
            var questions = timeline.OfType<AgentUserQuestionRequested>().Select(entry => entry.Request.Questions.Single()).ToArray();
            Assert.That(questions[0].Options, Is.EqualTo(rich.Options));
            Assert.That(questions[0].Header, Is.EqualTo(rich.Header));
            Assert.That(questions[0].MultiSelect, Is.True);
            Assert.That(questions[1].Options, Is.EqualTo(new AgentQuestionOption[] { new("A"), new("B") }));
            Assert.That(questions[1].IsOther, Is.True);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task RecoversIncompleteTailAndSerializesConcurrentAppends()
    {
        var directory = Directory.CreateTempSubdirectory("beutl-agent-timeline-");
        try
        {
            string project = Path.Combine(directory.FullName, "project.beutl");
            var first = new AgentThreadStore(directory.FullName);
            var second = new AgentThreadStore(directory.FullName);
            var thread = await first.CreateAsync(project, "acp", "Thread", AgentPermissionMode.Supervised);
            var entry = new AgentAssistantTextDelta(thread.Id, Guid.NewGuid(), DateTimeOffset.UtcNow, "first");
            await first.AppendAsync(project, entry);
            string log = directory.GetFiles("*.jsonl", SearchOption.AllDirectories).Single().FullName;
            await File.AppendAllTextAsync(log, "{\"type\":\"assistant");
            Assert.That(await second.ReadTimelineAsync(project, thread.Id), Is.EqualTo(new[] { entry }));

            await Task.WhenAll(first.AppendAsync(project, entry with { Text = "second" }),
                second.AppendAsync(project, entry with { Text = "third" }));
            var entries = await first.ReadTimelineAsync(project, thread.Id);
            Assert.That(entries.Cast<AgentAssistantTextDelta>().Select(item => item.Text), Is.EquivalentTo(new[] { "first", "second", "third" }));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
