using System.Runtime.InteropServices;
using Avalonia.Input;

namespace Beutl.Extensibility;

public interface IContextCommandHandler
{
    void Execute(ContextCommandExecution execution);
}

public class ContextCommandExecution
{
    public ContextCommandExecution(string commandName)
    {
        CommandName = commandName;
    }

    public string CommandName { get; }

    public KeyEventArgs? KeyEventArgs { get; set; }
}

[AttributeUsage(AttributeTargets.Method)]
public class ContextCommandAttribute : Attribute
{
    public string? Name { get; set; }
}

public class ContextCommandDefinition(
    string name,
    string? displayName = null,
    string? description = null,
    ContextCommandKeyGesture[]? keyGestures = null)
{
    public string Name { get; init; } = name;

    public string? DisplayName { get; init; } = displayName;

    public string? Description { get; init; } = description;

    public ContextCommandKeyGesture[]? KeyGestures { get; init; } = keyGestures;
}

public class ContextCommandKeyGesture(string? keyGesture, OSPlatform? platform = null)
{
    public string? KeyGesture { get; init; } = keyGesture;

    public OSPlatform? Platform { get; init; } = platform;
}
