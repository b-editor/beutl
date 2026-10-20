using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Sessions;
using Beutl.Editor;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

// Writers that are not the user's gesture wait for its edit session and then run in order (#2874).
[NonParallelizable]
[TestFixture]
public class EditSessionWriterTests
{
    [AvaloniaTest]
    public async Task A_generative_result_waits_for_the_open_edit_session()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("session-generative");
        var host = new EditorGenerativeRunHost(editor);
        bool applied = false;
        EditSession session = editor.HistoryManager.BeginEdit("Drag");

        Task result = host.InvokeOnUIThreadAsync(() => applied = true);
        await HeadlessTestHelpers.WaitUntilAsync(() => editor.HistoryManager.QueuedEditCount == 1, TimeSpan.FromMinutes(1));
        Assert.That(applied, Is.False);

        session.Complete();
        await result.WaitAsync(TimeSpan.FromMinutes(1));
        Assert.That(applied, Is.True);
    }

    [AvaloniaTest]
    public async Task An_element_batch_waits_for_the_open_edit_session()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("session-adder");
        EditSession session = editor.HistoryManager.BeginEdit("Drag");

        ValueTask<ElementAddResult> adding = editor.GetRequiredService<IElementAdder>().AddAsync([new ElementDescription(
            TimeSpan.Zero, TimeSpan.FromSeconds(1), 0, new ElementSource.EngineObject(() => new RectShape()))],
            CancellationToken.None);
        await HeadlessTestHelpers.WaitUntilAsync(() => editor.HistoryManager.QueuedEditCount == 1, TimeSpan.FromMinutes(1));
        Assert.That(editor.Scene.Children, Is.Empty);

        session.Complete();
        ElementAddResult result = await adding.AsTask().WaitAsync(TimeSpan.FromMinutes(1));
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(editor.Scene.Children, Has.Count.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task An_MCP_edit_waits_for_the_open_edit_session_and_a_read_does_not()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("session-mcp");
        var binding = new EditViewModelLiveBinding(editor);
        bool edited = false;
        bool read = false;
        EditSession session = editor.HistoryManager.BeginEdit("Drag");

        Task editing = Task.Run(() => binding.InvokeEdit(() => edited = true, TimeSpan.FromMinutes(1), CancellationToken.None));
        await HeadlessTestHelpers.WaitUntilAsync(() => editor.HistoryManager.QueuedEditCount == 1, TimeSpan.FromMinutes(1));
        await Task.Run(() => binding.Invoke(() => read = true)).WaitAsync(TimeSpan.FromMinutes(1));
        ValueTask<int> undo = binding.ExecuteHistoryMutationAsync(_ => false, history => history.UndoCount, sessionWait: TimeSpan.FromMinutes(1));
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(read, Is.True);
            Assert.That(edited, Is.False);
            Assert.That(undo.IsCompleted, Is.False);
        });

        session.Complete();
        await editing.WaitAsync(TimeSpan.FromMinutes(1));
        await undo.AsTask().WaitAsync(TimeSpan.FromMinutes(1));
        Assert.That(edited, Is.True);
    }

    [AvaloniaTest]
    public async Task An_MCP_edit_fails_with_user_editing_when_the_session_outlasts_its_deadline()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("session-mcp-deadline");
        var binding = new EditViewModelLiveBinding(editor);
        bool edited = false;
        EditSession session = editor.HistoryManager.BeginEdit("Drag");

        Task editing = Task.Run(() => binding.InvokeEdit(() => edited = true, TimeSpan.FromMilliseconds(100), CancellationToken.None));
        await HeadlessTestHelpers.WaitUntilAsync(() => editing.IsCompleted, TimeSpan.FromMinutes(1));
        session.Complete();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(editing.Exception?.InnerException, Is.TypeOf<UserEditingException>());
            Assert.That(edited, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task A_version_control_transition_waits_for_the_open_edit_session()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync("session-restore");
        EditSession session = editor.HistoryManager.BeginEdit("Drag");

        Task waiting = TestShell.Editor.WaitForEditSessionsAsync();
        HeadlessTestHelpers.Settle();
        Assert.That(waiting.IsCompleted, Is.False);

        session.Cancel();
        await waiting.WaitAsync(TimeSpan.FromMinutes(1));
    }
}
