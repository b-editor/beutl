using Beutl.ViewModels;

namespace Beutl.Services;

internal sealed class CommandPaletteInteractionSession(CommandPaletteViewModel owner, string title)
    : IContextCommandInteraction, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();

    public CancellationToken CancellationToken => _cancellation.Token;

    public Task<string?> ShowInputAsync(ContextCommandInputOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return ShowInputCoreAsync(options, cancellationToken);
    }

    private async Task<string?> ShowInputCoreAsync(ContextCommandInputOptions options, CancellationToken cancellationToken)
    {
        return await owner.RequestPromptAsync(this,
            () => new CommandPalettePromptViewModel(
                options.Title ?? title, options.Prompt, options.Placeholder, isInput: true, options.Validate),
            options.Value, cancellationToken) as string;
    }

    public async Task<ContextCommandPickItem<T>?> ShowQuickPickAsync<T>(
        IReadOnlyList<ContextCommandPickItem<T>> items,
        ContextCommandPickOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        return await owner.RequestPromptAsync(this,
            () => new CommandPalettePromptViewModel(
                options?.Title ?? title, options?.Prompt, options?.Placeholder, isInput: false,
                choices: items.Select(item => new CommandPaletteChoiceViewModel(item.Label, item.Description, item)).ToArray()),
            string.Empty, cancellationToken) as ContextCommandPickItem<T>;
    }

    public void Cancel() => _cancellation.Cancel();

    public void Dispose() => _cancellation.Dispose();
}
