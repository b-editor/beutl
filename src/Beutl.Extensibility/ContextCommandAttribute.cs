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

    public ContextCommandKeyGesture[]? KeyGestures { get; init; } = Normalize(keyGestures);

    private static ContextCommandKeyGesture[]? Normalize(ContextCommandKeyGesture[]? keyGestures)
    {
        if (keyGestures == null) return keyGestures;

        ContextCommandKeyGesture? fallbackGesture = null;
        ContextCommandKeyGesture? windows = null;
        ContextCommandKeyGesture? linux = null;
        ContextCommandKeyGesture? osx = null;

        foreach (ContextCommandKeyGesture gesture in keyGestures)
        {
            if (gesture.Platform == null) fallbackGesture = gesture;
            else if (gesture.Platform == OSPlatform.Windows) windows = gesture;
            else if (gesture.Platform == OSPlatform.Linux) linux = gesture;
            else if (gesture.Platform == OSPlatform.OSX) osx = gesture;
        }

        if (windows == null && fallbackGesture != null)
            windows = new ContextCommandKeyGesture(fallbackGesture.KeyGesture, OSPlatform.Windows);
        if (linux == null && fallbackGesture != null)
            linux = new ContextCommandKeyGesture(fallbackGesture.KeyGesture, OSPlatform.Linux);
        if (osx == null && fallbackGesture != null)
            osx = new ContextCommandKeyGesture(fallbackGesture.KeyGesture, OSPlatform.OSX);

        return new[] { windows, linux, osx }.Where(i => i != null).ToArray()!;
    }
}

public class ContextCommandKeyGesture(string? keyGesture, OSPlatform? platform = null)
{
    public string? KeyGesture { get; init; } = keyGesture;

    public OSPlatform? Platform { get; init; } = platform;
}
