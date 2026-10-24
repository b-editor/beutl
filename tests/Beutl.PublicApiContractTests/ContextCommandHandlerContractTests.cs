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
        var definition = new ContextCommandDefinition("test.pluginCommand")
        {
            Scope = ContextCommandScope.Extension,
            ExecuteAsync = _ => completion.Task
        };
        var execution = new ContextCommandExecution("test.pluginCommand");

        Task operation = definition.RunAsync(null, execution);

        Assert.That(operation, Is.SameAs(completion.Task));
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
        var definition = new ContextCommandDefinition("test.configure") { Scope = ContextCommandScope.Extension };
        var execution = new ContextCommandExecution(definition.Id) { Interaction = interaction };

        Assert.That(new ContextCommandDefinition("test.local").Scope, Is.EqualTo(ContextCommandScope.Context));
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
}
