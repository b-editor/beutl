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

    public void ChangeKeyGesture(ContextCommandEntry entry, KeyGesture? keyGesture, OSPlatform platform)
    {
        bool changed = false;
        for (int i = 0; i < entry.KeyGestures.Count; i++)
        {
            var gesture = entry.KeyGestures[i];
            if (gesture.Platform == platform)
            {
                entry.KeyGestures[i] = gesture with { KeyGesture = keyGesture };
                changed = true;
            }
        }

        // platformId が見つからなかった場合、追加する
        if (!changed)
        {
            entry.KeyGestures.Add(new ContextCommandParsedKeyGesture(keyGesture, platform));
        }

        settingsStore.Save(GetFullName(entry), keyGesture, platform);
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
            var keyGestures = settingsStore.Restore(GetFullName(entry));
            foreach (ContextCommandParsedKeyGesture i in keyGestures)
            {
                // デフォルトのGestureがある場合、それを上書きする
                for (int index = 0; index < entry.KeyGestures.Count; index++)
                {
                    ContextCommandParsedKeyGesture j = entry.KeyGestures[index];
                    if (j.Platform == i.Platform)
                    {
                        entry.KeyGestures[index] = i;
                        goto NextItem;
                    }
                }

                // プラットフォームが指定されていない場合、先頭に追加する
                entry.KeyGestures.Add(i);

                NextItem: ;
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

                        compiledHandler.Execute(new ContextCommandExecution(entry.Definition.Name)
                        {
                            KeyEventArgs = args
                        });
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
    private JsonObject _json = [];

    public ContextCommandSettingsStore()
    {
        RestoreAll();
    }

    public void Save(string name, KeyGesture? keyGesture, OSPlatform os)
    {
        string platform = os.ToString();
        if (!_json.TryGetPropertyValue(platform, out var node)
            || node is not JsonObject obj)
        {
            obj = new JsonObject();
            _json[platform] = obj;
        }

        obj[name] = keyGesture?.ToString();

        SaveAll();
    }

    private static bool ValidateKey(string key)
    {
        return key.Equals("Windows", StringComparison.OrdinalIgnoreCase)
               || key.Equals("Linux", StringComparison.OrdinalIgnoreCase)
               || key.Equals("OSX", StringComparison.OrdinalIgnoreCase);
    }

    public IEnumerable<ContextCommandParsedKeyGesture> Restore(string name)
    {
        foreach (var (key, node) in _json)
        {
            if (!ValidateKey(key)) continue;
            if (node is JsonObject obj && obj.TryGetPropertyValueAsJsonValue(name, out string? value))
            {
                ContextCommandParsedKeyGesture? gesture = null;
                if (string.IsNullOrEmpty(value))
                {
                    gesture = new ContextCommandParsedKeyGesture(null, OSPlatform.Create(key));
                }
                else
                {
                    try
                    {
                        gesture = new ContextCommandParsedKeyGesture(KeyGesture.Parse(value), OSPlatform.Create(key));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse key gesture: {KeyGesture}", value);
                    }
                }

                if (gesture != null)
                {
                    yield return gesture;
                }
            }
        }
    }

    private void SaveAll()
    {
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
