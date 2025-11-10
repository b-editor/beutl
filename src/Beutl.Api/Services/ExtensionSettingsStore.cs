using System.Text.Json.Nodes;
using Beutl.Extensibility;
using Beutl.Serialization;

namespace Beutl.Api.Services;

public sealed class ExtensionSettingsStore
{
    private const string FileName = "extensionsSettings.json";
    private JsonObject _json = [];

    public ExtensionSettingsStore()
    {
        RestoreAll();
    }

    private void SaveAll()
    {
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
        Type extensionType = extension.GetType();
        if (_json[extensionType.FullName!] is JsonObject obj)
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
}
