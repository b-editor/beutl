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
            var context = new JsonSerializationContext(
                settings.GetType(), NullSerializationErrorNotifier.Instance, json: obj);
            using (ThreadLocalSerializationContext.Enter(context))
            {
                settings.Deserialize(context);
            }
        }
    }

    public void Save(Extension extension, ExtensionSettings settings)
    {
        Type extensionType = extension.GetType();
        var context = new JsonSerializationContext(settings.GetType(), NullSerializationErrorNotifier.Instance);
        using (ThreadLocalSerializationContext.Enter(context))
        {
            settings.Serialize(context);

            _json[extensionType.FullName!] = context.GetJsonObject();
        }

        SaveAll();
    }
}
