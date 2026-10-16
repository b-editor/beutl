using Beutl.Extensibility;

namespace Beutl.HeadlessUITests;

// Runs a command's definition on a context (null for the extension itself), as a key or the palette does.
internal static class ContextCommandTestExtensions
{
    public static Task RunAsync(this ViewExtension extension, object? context, ContextCommandExecution execution)
        => extension.Definition(execution).RunAsync(context, execution);

    public static bool CanRun(this ViewExtension extension, object? context, ContextCommandExecution execution)
        => extension.Definition(execution).CanRun(context, execution);

    public static ContextCommandDefinition Definition(this ViewExtension extension, ContextCommandExecution execution)
        => extension.ContextCommands.Single(definition => definition.Name == execution.CommandName);
}
