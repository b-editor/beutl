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

    [Test]
    public async Task Plugin_can_await_text_and_a_typed_choice_through_the_public_contract()
    {
        IContextCommandInteraction interaction = new PluginInteraction();
        string? input = await interaction.ShowInputAsync(new ContextCommandInputOptions
        {
            Value = "Layout",
            Validate = value => value.Length == 0 ? "Required" : null
        });
        ContextCommandPickItem<int> item = new("Editing", 42, "Timeline");
        var selected = await interaction.ShowQuickPickAsync<int>([item]);
        var definition = new ContextCommandDefinition("Configure") { Scope = ContextCommandScope.Extension };
        var execution = new ContextCommandExecution(definition.Name) { Interaction = interaction };

        Assert.Multiple(() =>
        {
            Assert.That(input, Is.EqualTo("Layout"));
            Assert.That(selected, Is.SameAs(item));
            Assert.That(selected!.Value, Is.EqualTo(42));
            Assert.That(execution.Interaction, Is.SameAs(interaction));
            Assert.That(new ContextCommandDefinition("Local").Scope, Is.EqualTo(ContextCommandScope.Context));
        });
    }

    private sealed class PluginInteraction : IContextCommandInteraction
    {
        public CancellationToken CancellationToken => CancellationToken.None;

        public Task<string?> ShowInputAsync(ContextCommandInputOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(options.Value);

        public Task<ContextCommandPickItem<T>?> ShowQuickPickAsync<T>(
            IReadOnlyList<ContextCommandPickItem<T>> items, ContextCommandPickOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(items.FirstOrDefault());
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
