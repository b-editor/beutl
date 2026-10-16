using Beutl.Extensibility;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class ContextCommandHandlerContractTests
{
    [Test]
    public void Plugin_handler_can_expose_its_complete_operation()
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new PluginContextCommandHandler(completion.Task);
        var execution = new ContextCommandExecution("PluginCommand");

        Task operation = handler.ExecuteAsync(execution);
    }

    [Test]
    public void Contract_has_one_awaitable_execution_path_without_a_completion_side_channel()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(IContextCommandHandler).GetMethod("Execute"), Is.Null);
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
        var definition = new ContextCommandDefinition("Configure") { Id = "test.configure", Scope = ContextCommandScope.Extension };
        var execution = new ContextCommandExecution(definition.Name) { Interaction = interaction };

        Assert.That(new ContextCommandDefinition("Local") { Id = "test.local" }.Scope, Is.EqualTo(ContextCommandScope.Context));
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
        public Task ExecuteAsync(ContextCommandExecution execution) => operation;
    }
}
