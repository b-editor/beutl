using System.Reactive;
using System.Runtime.InteropServices;
using Avalonia.Input;

namespace Beutl.Extensibility;

public interface IContextCommandHandler
{
    void Execute(ContextCommandExecution execution);

    bool CanExecute(ContextCommandExecution execution) => true;
}

// CanExecute の判定が状態変化に応じて変わる場合に実装する optional インターフェース。
// CommandPalette はスナップショット中のハンドラーが提供する CanExecuteChanged を購読し、
// 通知を受け取った時点でフィルタ結果を再評価する。
public interface IContextCommandStateNotifier
{
    IObservable<Unit> CanExecuteChanged { get; }
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

        var fallbacks = new List<ContextCommandKeyGesture>();
        var windows = new List<ContextCommandKeyGesture>();
        var linux = new List<ContextCommandKeyGesture>();
        var osx = new List<ContextCommandKeyGesture>();

        foreach (ContextCommandKeyGesture gesture in keyGestures)
        {
            if (gesture.Platform == null) fallbacks.Add(gesture);
            else if (gesture.Platform == OSPlatform.Windows) windows.Add(gesture);
            else if (gesture.Platform == OSPlatform.Linux) linux.Add(gesture);
            else if (gesture.Platform == OSPlatform.OSX) osx.Add(gesture);
        }

        // A platform with no explicit gesture inherits every platform-less fallback, so a command
        // declaring several fallbacks (e.g. V and Escape) keeps all of them per platform rather
        // than only the last one. A platform with its own gesture(s) overrides the fallbacks.
        FillFromFallbacks(windows, fallbacks, OSPlatform.Windows);
        FillFromFallbacks(linux, fallbacks, OSPlatform.Linux);
        FillFromFallbacks(osx, fallbacks, OSPlatform.OSX);

        return windows.Concat(linux).Concat(osx).ToArray();
    }

    private static void FillFromFallbacks(
        List<ContextCommandKeyGesture> target, List<ContextCommandKeyGesture> fallbacks, OSPlatform platform)
    {
        if (target.Count != 0) return;

        foreach (ContextCommandKeyGesture fallback in fallbacks)
            target.Add(new ContextCommandKeyGesture(fallback.KeyGesture, platform));
    }
}

public class ContextCommandKeyGesture(string? keyGesture, OSPlatform? platform = null)
{
    public string? KeyGesture { get; init; } = keyGesture;

    public OSPlatform? Platform { get; init; } = platform;
}
