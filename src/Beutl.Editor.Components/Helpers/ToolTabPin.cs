using System.Text.Json.Nodes;
using Reactive.Bindings;

namespace Beutl.Editor.Components.Helpers;

// The pin a tool tab offers on its target. Losing the target releases it, so a pinned tab is never left
// empty, and it is saved with the tab.
internal sealed class ToolTabPin : IDisposable
{
    private const string JsonKey = "isPinned";
    private readonly IDisposable _release;

    public ToolTabPin(IObservable<bool> hasTarget)
    {
        HasTarget = hasTarget.ToReadOnlyReactivePropertySlim();
        _release = HasTarget.Where(v => !v).Subscribe(_ => IsPinned.Value = false);
    }

    public ReactivePropertySlim<bool> IsPinned { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> HasTarget { get; }

    // Whether the saved tab was pinned; read before restoring the target when the pin decides which target to restore.
    public static bool WasPinned(JsonObject json)
    {
        return json.TryGetPropertyValueAsJsonValue(JsonKey, out bool pinned) && pinned;
    }

    // Call after the tab restored its target: a tab whose target is gone comes back unpinned.
    public void ReadFromJson(JsonObject json)
    {
        IsPinned.Value = HasTarget.Value && WasPinned(json);
    }

    public void WriteToJson(JsonObject json)
    {
        if (IsPinned.Value)
            json[JsonKey] = true;
        else
            json.Remove(JsonKey);
    }

    public void Dispose()
    {
        _release.Dispose();
        HasTarget.Dispose();
        IsPinned.Dispose();
    }
}
