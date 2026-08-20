using System.Windows.Input;
using Beutl.Extensibility;
using Reactive.Bindings;

namespace Beutl.UnitTests.Extensibility;

[TestFixture]
public class ContextCommandExecutionTests
{
    [Test]
    public void Completion_defaults_to_a_finished_task()
    {
        var execution = new ContextCommandExecution("EnableVersionControl");

        Assert.That(execution.Completion.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public async Task Completion_tracks_an_asynchronous_command_until_its_handler_returns()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AsyncReactiveCommand command = new AsyncReactiveCommand()
            .WithSubscribe(() => gate.Task);
        var execution = new ContextCommandExecution("EnableVersionControl");

        execution.Execute(command);

        Assert.That(execution.Completion.IsCompleted, Is.False);

        gate.SetResult();
        await execution.Completion;

        Assert.That(execution.Completion.IsCompletedSuccessfully, Is.True);
    }

    [Test]
    public void Completion_stays_finished_for_a_synchronous_command()
    {
        var command = new RecordingCommand();
        var execution = new ContextCommandExecution("EnableVersionControl");

        execution.Execute(command);

        Assert.Multiple(() =>
        {
            Assert.That(command.ExecuteCount, Is.EqualTo(1));
            Assert.That(execution.Completion.IsCompletedSuccessfully, Is.True);
        });
    }

    private sealed class RecordingCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public int ExecuteCount { get; private set; }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => ExecuteCount++;
    }
}
