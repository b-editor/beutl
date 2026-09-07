using Beutl.Extensibility;

namespace Beutl.UnitTests.Extensibility;

[TestFixture]
public class ContextCommandExecutionTests
{
    [Test]
    public async Task Handler_contract_exposes_the_complete_operation()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new AwaitableHandler(gate.Task);
        var execution = new ContextCommandExecution("TestCommand");

        Task operation = handler.ExecuteAsync(execution);

        Assert.That(operation.IsCompleted, Is.False);

        gate.SetResult();
        await operation;

        Assert.Multiple(() =>
        {
            Assert.That(operation.IsCompletedSuccessfully, Is.True);
            Assert.That(handler.LastExecution, Is.SameAs(execution));
        });
    }

    private sealed class AwaitableHandler(Task operation) : IContextCommandHandler
    {
        public ContextCommandExecution? LastExecution { get; private set; }

        public Task ExecuteAsync(ContextCommandExecution execution)
        {
            LastExecution = execution;
            return operation;
        }
    }
}
