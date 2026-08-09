using Beutl.Api.Services;
using Beutl.Editor;
using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class EditorServiceTests
{
    [Test]
    public async Task Project_file_writes_and_worktree_mutations_are_mutually_exclusive()
    {
        var editorService = new EditorService(new ExtensionProvider());
        using (IDisposable fileWrite = await editorService.BeginProjectFileWriteAsync(
                   CancellationToken.None))
        {
            Assert.That(editorService.TryBeginWorktreeMutation(), Is.Null);
        }

        using (IDisposable worktreeMutation = editorService.TryBeginWorktreeMutation()!)
        {
            ValueTask<IDisposable> pendingWrite = editorService.BeginProjectFileWriteAsync(
                CancellationToken.None);
            Assert.That(pendingWrite.IsCompleted, Is.False);
            worktreeMutation.Dispose();
            using IDisposable fileWrite = await pendingWrite;
        }
    }

    [Test]
    public async Task Project_file_write_leases_are_serialized()
    {
        var editorService = new EditorService(new ExtensionProvider());
        IDisposable first = await editorService.BeginProjectFileWriteAsync(
            CancellationToken.None);
        ValueTask<IDisposable> second = editorService.BeginProjectFileWriteAsync(
            CancellationToken.None);

        Assert.That(second.IsCompleted, Is.False);

        first.Dispose();
        using IDisposable secondLease = await second;
    }

    [Test]
    public void SaveProjectFilesAsync_requires_a_project_uri()
    {
        var editorService = new EditorService(new ExtensionProvider());
        var project = new Project();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await editorService.SaveProjectFilesAsync(project, CancellationToken.None));
    }

    [Test]
    public async Task SaveProjectFilesAsync_serializes_away_from_the_calling_thread()
    {
        int serializationThread = 0;
        var editorService = new EditorService(
            new ExtensionProvider(),
            (_, _) => serializationThread = Environment.CurrentManagedThreadId);
        var project = new Project { Uri = new Uri("file:///project.bep") };
        int callingThread = Environment.CurrentManagedThreadId;

        Assert.That(
            await editorService.SaveProjectFilesAsync(project, CancellationToken.None),
            Is.True);
        Assert.That(serializationThread, Is.Not.EqualTo(callingThread));
    }
}
