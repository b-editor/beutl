using Beutl.Agents;

namespace Beutl.Agents.Tests;

public sealed class AgentDraftStoreTests
{
    [Test]
    public async Task ReloadSeparatesThreadAndNewDraftsAndKeepsRecentSentPrompts()
    {
        var directory = Directory.CreateTempSubdirectory("beutl-agent-drafts-");
        try
        {
            var store = new AgentDraftStore(directory.FullName);
            string project = Path.Combine(directory.FullName, "project.beutl");
            Guid thread = Guid.NewGuid();
            Guid other = Guid.NewGuid();
            var attachment = Path.Combine(directory.FullName, "frame.png");
            await store.SaveAsync(project, null, new AgentDraft("New thread", []));
            await store.SaveAsync(project, thread, new AgentDraft("Existing thread", [attachment], false, ["image/png"]));
            await store.SaveAsync(project, other, new AgentDraft("Other thread", []));
            for (int i = 0; i < AgentDraftStore.HistoryLimit + 1; i++)
                await store.RecordPromptAsync(project, thread, $"Prompt {i}");
            await store.RecordPromptAsync(project, thread, $"Prompt {AgentDraftStore.HistoryLimit}");

            var reloaded = new AgentDraftStore(directory.FullName);
            var restored = await reloaded.LoadAsync(project, thread);
            Assert.That(restored.Draft!.Prompt, Is.EqualTo("Existing thread"));
            Assert.That(restored.Draft.AttachmentPaths, Is.EqualTo(new[] { attachment }));
            Assert.That(restored.Draft.AttachmentMediaTypes, Is.EqualTo(new[] { "image/png" }));
            Assert.That(restored.Draft.IncludeSceneContext, Is.False);
            Assert.That(restored.History, Is.EqualTo(Enumerable.Range(1, AgentDraftStore.HistoryLimit).Select(i => $"Prompt {i}")));
            Assert.That((await reloaded.LoadAsync(project, null)).Draft!.Prompt, Is.EqualTo("New thread"));
            Assert.That((await reloaded.LoadAsync(project, other)).Draft!.Prompt, Is.EqualTo("Other thread"));
            Assert.That((await reloaded.LoadAsync(Path.Combine(directory.FullName, "other.beutl"), thread)).Draft, Is.Null);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public async Task ConcurrentStoreInstancesPublishDraftAndHistoryTogetherAndCancelledSaveLeavesFileIntact()
    {
        var directory = Directory.CreateTempSubdirectory("beutl-agent-drafts-");
        try
        {
            var store = new AgentDraftStore(directory.FullName);
            var otherStore = new AgentDraftStore(directory.FullName);
            string project = Path.Combine(directory.FullName, "project.beutl");
            Guid thread = Guid.NewGuid();
            await Task.WhenAll(store.SaveAsync(project, thread, new AgentDraft("Unsent", [])),
                otherStore.RecordPromptAsync(project, thread, "Sent"));
            string projectDirectory = new AgentThreadStore(directory.FullName).ProjectDirectory(project);
            string path = Path.Combine(projectDirectory, "drafts.json");
            byte[] before = await File.ReadAllBytesAsync(path);

            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThatAsync(() => otherStore.SaveAsync(project, thread, new AgentDraft("Incomplete", []), cancelled.Token),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(before));
            Assert.That(Directory.GetDirectories(projectDirectory, ".beutl-output-*"), Is.Empty);
            var restored = await new AgentDraftStore(directory.FullName).LoadAsync(project, thread);
            Assert.That(restored.Draft!.Prompt, Is.EqualTo("Unsent"));
            Assert.That(restored.History, Is.EqualTo(new[] { "Sent" }));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
