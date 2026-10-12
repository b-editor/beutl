using System.Text.Json.Nodes;
using Beutl.Extensibility;
using Beutl.JsonDiscriminator;
using Beutl.Serialization;

namespace Beutl.Api.Services;

public sealed class ExtensionSettingsStore
{
    private const string FileName = "extensionsSettings.json";
    private readonly bool _persist = true;
    private JsonObject _json = [];

    public ExtensionSettingsStore()
    {
        RestoreAll();
    }

    // Test seam: start from the given JSON and optionally skip writing extensionsSettings.json.
    internal ExtensionSettingsStore(JsonObject json, bool persist)
    {
        _json = json;
        _persist = persist;
    }

    private void SaveAll()
    {
        if (!_persist) return;

        string fileName = Path.Combine(Helper.AppRoot, FileName);
        _json.JsonSave(fileName);
    }

    private void RestoreAll()
    {
        string fileName = Path.Combine(Helper.AppRoot, FileName);
        if (JsonHelper.JsonRestore(fileName) is JsonObject obj)
        {
            _json = obj;
        }
    }

    public void Restore(Extension extension, ExtensionSettings settings)
    {
        if (FindSaved(extension.GetType()) is JsonObject obj)
        {
            CoreSerializer.PopulateFromJsonObject(settings, obj);
        }
    }

    public void Save(Extension extension, ExtensionSettings settings)
    {
        Type extensionType = extension.GetType();
        _json[extensionType.FullName!] = CoreSerializer.SerializeToJsonObject(settings);

        SaveAll();
    }

    // The settings saved under the extension type's full name or, when there are none, under one of its former
    // names (FormerPersistedNameAttribute), so a renamed or moved extension keeps its settings. Saving writes the
    // current name.
    private JsonObject? FindSaved(Type extensionType)
    {
        if (_json.TryGetPropertyValue(extensionType.FullName!, out JsonNode? current))
        {
            return current as JsonObject;
        }

        foreach (FormerTypeName former in TypeFormat.GetFormerNames(extensionType))
        {
            if (_json[former.FullName] is JsonObject saved)
            {
                return saved;
            }
        }

        return null;
    }
}
