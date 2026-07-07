using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia.Input;
using Beutl.Collections;
using Beutl.Extensibility;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Api.Services;

public record ContextCommandEntry(
    Type ExtensionType,
    ContextCommandDefinition Definition,
    CoreList<ContextCommandParsedKeyGesture> KeyGestures);

public record ContextCommandParsedKeyGesture(KeyGesture? KeyGesture, OSPlatform Platform);

public record ContextCommandHandler(MethodInfo MethodInfo, ParameterInfo[] Parameters)
{
    public void Invoke(object context, KeyEventArgs args, ILogger logger)
    {
        switch (Parameters.Length)
        {
            case 0:
                args.Handled = true;
                MethodInfo.Invoke(context, []);
                break;
            case 1 when Parameters[0].ParameterType == typeof(KeyEventArgs):
                MethodInfo.Invoke(context, [args]);
                break;
            default:
                logger.LogWarning("Invalid parameter count: {ParameterCount}", Parameters.Length);
                break;
        }
    }
}

public class ContextCommandHandlerRegistry : IBeutlApiResource
{
    private readonly ILogger _logger = Log.CreateLogger<ContextCommandHandlerRegistry>();
    private readonly Dictionary<Type, Dictionary<string, ContextCommandHandler>> _handlers = new();

    private static string GetFullName(Type extensionType, string name)
    {
        return extensionType.Namespace == null
            ? $"{extensionType.Name}.{name}"
            : $"{extensionType.Namespace}.{extensionType.Name}.{name}";
    }

    public void Register(Type contextType, Type extensionType)
    {
        var dict = _handlers.GetValueOrDefault(contextType);
        if (dict == null)
        {
            dict = new Dictionary<string, ContextCommandHandler>();
            _handlers[contextType] = dict;
        }

        foreach (MethodInfo method in contextType.GetMethods())
        {
            if (method.GetCustomAttribute<ContextCommandAttribute>() is not { } attr)
                continue;

            string key = GetFullName(extensionType, attr.Name ?? method.Name);
            if (!dict.TryAdd(key, new ContextCommandHandler(method, method.GetParameters())))
            {
                _logger.LogWarning("Duplicate context command: {Key}", key);
            }
        }
    }

    public ContextCommandHandler? GetHandler(ContextCommandEntry entry)
    {
        var key = GetFullName(entry.ExtensionType, entry.Definition.Name);
        foreach (Dictionary<string, ContextCommandHandler> d in _handlers.Values)
        {
            var i = d.GetValueOrDefault(key);
            if (i != null)
            {
                return i;
            }
        }

        return null;
    }

    public bool IsRegistered(Type contextType)
    {
        return _handlers.ContainsKey(contextType);
    }
}

public class ContextCommandManager(
    ContextCommandSettingsStore settingsStore,
    ContextCommandHandlerRegistry handlerRegistry)
    : IBeutlApiResource
{
    private readonly ILogger _logger = Log.CreateLogger<ContextCommandManager>();
    private readonly Dictionary<Type, ContextCommandEntry[]> _entries = new();

    public void Register(ViewExtension extension)
    {
        var definitions = extension.ContextCommands.Select(def
                => new ContextCommandEntry(
                    extension.GetType(),
                    def,
                    new(def.KeyGestures?.Select(g =>
                    {
                        if (!g.Platform.HasValue)
                        {
                            _logger.LogWarning("Key gesture platform is not specified: {KeyGesture}", g.KeyGesture);
                            return null!;
                        }

                        try
                        {
                            return new ContextCommandParsedKeyGesture(
                                g.KeyGesture == null ? null : KeyGesture.Parse(g.KeyGesture), g.Platform.Value);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to parse key gesture: {KeyGesture}", g.KeyGesture);
                            return new ContextCommandParsedKeyGesture(null, g.Platform.Value);
                        }
                    }).Where(i => i != null!) ?? [])))
            .ToArray();

        Restore(definitions);

        _entries[extension.GetType()] = definitions;
    }

    public void Unregister(ViewExtension extension)
    {
        _entries.Remove(extension.GetType());
    }

    public IEnumerable<ContextCommandEntry> GetDefinitions(Type type)
    {
        return _entries.GetValueOrDefault(type) ?? [];
    }

    public IEnumerable<ContextCommandEntry> GetDefinitions<T>()
    {
        return GetDefinitions(typeof(T));
    }

    public IEnumerable<ContextCommandEntry> GetDefinitions()
    {
        return _entries.Values.SelectMany(i => i);
    }

    /// <summary>
    /// Rebind one gesture slot of a command. <paramref name="gestureIndex"/> addresses the
    /// N-th gesture of <paramref name="platform"/> (a command may bind several, e.g. V and
    /// Escape); only that slot changes, so remapping one binding cannot destroy the others.
    /// </summary>
    public void ChangeKeyGesture(ContextCommandEntry entry, KeyGesture? keyGesture, OSPlatform platform, int gestureIndex = 0)
    {
        int seen = 0;
        bool changed = false;
        for (int i = 0; i < entry.KeyGestures.Count; i++)
        {
            var gesture = entry.KeyGestures[i];
            if (gesture.Platform != platform) continue;

            if (seen == gestureIndex)
            {
                entry.KeyGestures[i] = gesture with { KeyGesture = keyGesture };
                changed = true;
                break;
            }

            seen++;
        }

        // When no slot matched, appending is only valid at the next free slot
        // (gestureIndex == existing count). A larger index would silently create a slot the
        // UI never presented — and persist it through Save — so treat it as a caller bug.
        if (!changed)
        {
            if (gestureIndex != seen)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gestureIndex), gestureIndex,
                    $"The platform '{platform}' has {seen} gesture slot(s); only an existing slot or the next free slot can be assigned.");
            }

            entry.KeyGestures.Add(new ContextCommandParsedKeyGesture(keyGesture, platform));
        }

        // Persist the whole per-platform list: with multiple gestures per platform a single
        // (name, platform) → gesture record cannot round-trip the untouched slots.
        settingsStore.Save(
            GetFullName(entry),
            entry.KeyGestures.Where(g => g.Platform == platform).Select(g => g.KeyGesture).ToArray(),
            platform);
    }

    private static string GetFullName(ContextCommandEntry entry)
    {
        return entry.ExtensionType.Namespace == null
            ? $"{entry.ExtensionType.Name}.{entry.Definition.Name}"
            : $"{entry.ExtensionType.Namespace}.{entry.ExtensionType.Name}.{entry.Definition.Name}";
    }

    private void Restore(ContextCommandEntry[] entries)
    {
        foreach (ContextCommandEntry entry in entries)
        {
            foreach ((OSPlatform platform, IReadOnlyList<KeyGesture?> gestures) in
                     settingsStore.Restore(GetFullName(entry)))
            {
                // Overwrite the default gestures slot-by-slot with the persisted list; slots
                // beyond a shorter persisted list keep their defaults.
                int seen = 0;
                for (int index = 0; index < entry.KeyGestures.Count && seen < gestures.Count; index++)
                {
                    if (entry.KeyGestures[index].Platform != platform) continue;

                    entry.KeyGestures[index] = new ContextCommandParsedKeyGesture(gestures[seen], platform);
                    seen++;
                }

                // Slots the defaults do not have are appended
                for (; seen < gestures.Count; seen++)
                {
                    entry.KeyGestures.Add(new ContextCommandParsedKeyGesture(gestures[seen], platform));
                }
            }
        }
    }

    public void Attach(InputElement element, ViewExtension extension)
    {
        if (_entries.TryGetValue(extension.GetType(), out var entries))
        {
            element.Focusable = true;
            var logger = _logger;
            element.KeyDown += (sender, args) =>
            {
                if (sender is not InputElement { DataContext: { } context })
                    return;

                OSPlatform pid = OperatingSystem.IsWindows() ? OSPlatform.Windows :
                    OperatingSystem.IsMacOS() ? OSPlatform.OSX :
                    OperatingSystem.IsLinux() ? OSPlatform.Linux :
                    throw new PlatformNotSupportedException();
                if (context is IContextCommandHandler compiledHandler)
                {
                    foreach (ContextCommandEntry entry in entries)
                    {
                        // Platformが一致するものがない場合はスキップ
                        // Gestureが一致するものがない場合はスキップ
                        if (entry.KeyGestures.Where(gesture => gesture.Platform == pid)
                            .All(gesture => gesture.KeyGesture?.Matches(args) != true))
                        {
                            continue;
                        }

                        var execution = new ContextCommandExecution(entry.Definition.Name)
                        {
                            KeyEventArgs = args
                        };

                        // ハンドラが CanExecute で false を返した場合は実行をスキップして
                        // 同じジェスチャの後続バインディング（フォールバック）に処理を委ねる。
                        if (!compiledHandler.CanExecute(execution))
                        {
                            continue;
                        }

                        compiledHandler.Execute(execution);
                        return;
                    }
                }

                Type contextType = context.GetType();
                if (!handlerRegistry.IsRegistered(contextType))
                {
                    handlerRegistry.Register(contextType, extension.GetType());
                }

                foreach (ContextCommandEntry entry in entries)
                {
                    // Platformが一致するものがない場合はスキップ
                    // Gestureが一致するものがない場合はスキップ
                    if (entry.KeyGestures.Where(gesture => gesture.Platform == pid)
                        .All(gesture => gesture.KeyGesture?.Matches(args) != true))
                    {
                        continue;
                    }

                    if (handlerRegistry.GetHandler(entry) is { } handler)
                    {
                        handler.Invoke(context, args, logger);
                        return;
                    }
                }
            };
        }
    }
}

public record ContextCommandSettingsStore : IBeutlApiResource
{
    private readonly ILogger _logger = Log.CreateLogger<ContextCommandSettingsStore>();
    private readonly bool _persist = true;
    private JsonObject _json = [];

    public ContextCommandSettingsStore()
    {
        RestoreAll();
    }

    // Test seam: start from the given JSON and optionally skip writing keymap.json to disk.
    internal ContextCommandSettingsStore(JsonObject json, bool persist)
    {
        _json = json;
        _persist = persist;
    }

    public virtual void Save(string name, IReadOnlyList<KeyGesture?> gestures, OSPlatform os)
    {
        string platform = os.ToString();
        if (!_json.TryGetPropertyValue(platform, out var node)
            || node is not JsonObject obj)
        {
            obj = new JsonObject();
            _json[platform] = obj;
        }

        var array = new JsonArray();
        foreach (KeyGesture? gesture in gestures)
        {
            array.Add((JsonNode?)gesture?.ToString());
        }

        obj[name] = array;

        SaveAll();
    }

    private static bool ValidateKey(string key)
    {
        return key.Equals("Windows", StringComparison.OrdinalIgnoreCase)
               || key.Equals("Linux", StringComparison.OrdinalIgnoreCase)
               || key.Equals("OSX", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The persisted per-platform gesture lists for a command, slot order preserved. A null
    /// element is an explicitly cleared binding (distinct from an absent command, which keeps
    /// its defaults). Entries written before multi-gesture support — a plain string or null
    /// instead of an array — are read as a single-element list.
    /// </summary>
    public virtual IEnumerable<(OSPlatform Platform, IReadOnlyList<KeyGesture?> Gestures)> Restore(string name)
    {
        foreach (var (key, node) in _json)
        {
            if (!ValidateKey(key)) continue;
            if (node is not JsonObject obj || !obj.TryGetPropertyValue(name, out JsonNode? value)) continue;

            List<KeyGesture?> gestures = value is JsonArray array
                ? array.Select(ParseGesture).ToList()
                : [ParseGesture(value)];

            yield return (OSPlatform.Create(key), gestures);
        }
    }

    // A gesture that no longer parses is treated as cleared rather than skipped, so the
    // slots after it keep their positions.
    private KeyGesture? ParseGesture(JsonNode? node)
    {
        if (node is not JsonValue value
            || !value.TryGetValue(out string? text)
            || string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            return KeyGesture.Parse(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse key gesture: {KeyGesture}", text);
            return null;
        }
    }

    private void SaveAll()
    {
        if (!_persist) return;

        string fileName = Path.Combine(Helper.AppRoot, "keymap.json");
        _json.JsonSave(fileName);
        _logger.LogInformation("Saved keymap to {FileName}", fileName);
    }

    private void RestoreAll()
    {
        string fileName = Path.Combine(Helper.AppRoot, "keymap.json");
        if (JsonHelper.JsonRestore(fileName) is JsonObject obj)
        {
            _json = obj;
        }
    }
}
