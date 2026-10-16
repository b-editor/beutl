using Beutl.Api.Services;
using Beutl.Extensibility;
using Beutl.Services;
using Beutl.Testing.Headless;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// Shared setup for the command palette tests.
internal static class CommandPaletteTestHost
{
    // A palette whose only command runs execute.
    public static (CommandPaletteViewModel Palette, EditorService Editor) CreatePalette(
        Func<ContextCommandExecution, Task> execute)
    {
        var provider = new ExtensionProvider();
        var editor = new EditorService(provider);
        var manager = new ContextCommandManager(new ContextCommandSettingsStore());
        manager.Register(new PromptExtension());
        var service = new CommandPaletteService(
            manager, new FixedHandlerProvider(new Handler(execute)), () => null, editor, provider);
        return (new CommandPaletteViewModel(service, editor), editor);
    }

    // Renders before every check, so the palette view keeps up with its view model.
    public static Task UntilAsync(Func<bool> condition)
        => HeadlessTestHelpers.WaitUntilAsync(() =>
        {
            HeadlessTestHelpers.Render();
            return condition();
        });

    private sealed class PromptExtension : ViewExtension
    {
        public override IEnumerable<ContextCommandDefinition> ContextCommands =>
        [
            HandlerCommand("Prompt", "Prompt command", "test.prompt")
        ];
    }

    // A command that runs on a Handler context.
    public static ContextCommandDefinition<Handler> HandlerCommand(
        string name, string displayName, string id, ContextCommandKeyGesture[]? keyGestures = null)
        => new(name, displayName, null, keyGestures)
        {
            Id = id,
            ExecuteOnAsync = (handler, execution) => handler.ExecuteAsync(execution)
        };

    public sealed class Handler(Func<ContextCommandExecution, Task> execute)
    {
        public Task ExecuteAsync(ContextCommandExecution execution) => execute(execution);
    }

    // Resolves every command to the same context. With null, only the commands an extension runs itself remain.
    public sealed class FixedHandlerProvider(object? context) : ICommandPaletteHandlerProvider
    {
        public object? Resolve(Type extensionType) => context;
    }
}
