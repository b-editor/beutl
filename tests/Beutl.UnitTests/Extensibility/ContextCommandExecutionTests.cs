using Beutl.Extensibility;

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
    public async Task Completion_carries_the_work_a_handler_started()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = new ContextCommandExecution("EnableVersionControl");

        execution.Completion = gate.Task;

        Assert.That(execution.Completion.IsCompleted, Is.False);

        gate.SetResult();
        await execution.Completion;

        Assert.That(execution.Completion.IsCompletedSuccessfully, Is.True);
    }
}
