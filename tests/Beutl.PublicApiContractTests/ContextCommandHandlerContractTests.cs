using Beutl.Extensibility;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class ContextCommandHandlerContractTests : PublicApiContractTestBase
{
    [Test]
    public async Task Plugin_handler_can_expose_its_complete_operation()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new PluginContextCommandHandler(completion.Task);
        var execution = new ContextCommandExecution("PluginCommand");

        Task operation = handler.ExecuteAsync(execution);

        Assert.That(operation.IsCompleted, Is.False);
        completion.SetResult();
        await operation;
        Assert.That(handler.Execution, Is.SameAs(execution));
    }

    [Test]
    public void Extensibility_does_not_grant_friend_access_to_contract_assembly()
    {
        AssertDoesNotHaveFriendAccess(typeof(IContextCommandHandler).Assembly);
    }

    [Test]
    public void Contract_has_one_awaitable_execution_path_without_a_completion_side_channel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(IContextCommandHandler).GetMethod("Execute"), Is.Null);
            Assert.That(
                typeof(IContextCommandHandler).GetMethod(nameof(IContextCommandHandler.ExecuteAsync))?.ReturnType,
                Is.EqualTo(typeof(Task)));
            Assert.That(typeof(ContextCommandExecution).GetProperty("Completion"), Is.Null);
        });
    }

    private sealed class PluginContextCommandHandler(Task operation) : IContextCommandHandler
    {
        public ContextCommandExecution? Execution { get; private set; }

        public Task ExecuteAsync(ContextCommandExecution execution)
        {
            Execution = execution;
            return operation;
        }
    }
}
