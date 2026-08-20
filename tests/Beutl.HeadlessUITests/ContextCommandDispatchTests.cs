using Avalonia.Headless.NUnit;
using Beutl.Extensibility;
using Beutl.Testing.Headless;
using Reactive.Bindings;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ContextCommandDispatchTests
{
    [AvaloniaTest]
    public async Task Asynchronous_menu_command_publishes_its_work_as_the_execution_completion()
    {
        await TestReset.ResetShellAsync();
        string location = Path.Combine(
            BeutlHomeIsolation.CurrentHome!,
            "context-command-dispatch");
        Directory.CreateDirectory(location);
        await TestShell.Project.CreateProject(640, 480, 30, 44100, "dispatch", location);
        HeadlessTestHelpers.Settle();

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = TestShell.MainViewModel.MenuBar.SaveAll
            .Subscribe(() => gate.Task);
        var execution = new ContextCommandExecution("SaveAll");

        TestShell.MainViewModel.Execute(execution);

        Assert.That(execution.Completion.IsCompleted, Is.False);

        gate.SetResult();
        await execution.Completion;

        Assert.That(execution.Completion.IsCompletedSuccessfully, Is.True);
        await TestReset.ResetShellAsync();
    }

    [AvaloniaTest]
    public async Task Disabled_menu_command_leaves_the_execution_completed()
    {
        await TestReset.ResetShellAsync();
        HeadlessTestHelpers.Settle();

        var execution = new ContextCommandExecution("SaveAll");

        // No project is open, so the command is disabled and nothing may be dispatched.
        TestShell.MainViewModel.Execute(execution);

        Assert.That(execution.Completion.IsCompletedSuccessfully, Is.True);
    }
}
