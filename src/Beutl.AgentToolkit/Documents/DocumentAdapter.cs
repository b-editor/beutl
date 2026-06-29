using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Common;
using Beutl.Serialization;

namespace Beutl.AgentToolkit.Documents;

public sealed class DocumentAdapter
{
    private readonly DeclarativeDocumentApplier _applier = new();

    public JsonObject Read(ICoreSerializable root)
    {
        ArgumentNullException.ThrowIfNull(root);

        JsonObject document = CoreSerializer.SerializeToJsonObject(root, CreateOptions(root, CoreSerializationMode.EmbedReferencedObjects));
        SchemaVersion.Stamp(document);
        return document;
    }

    public void Write(ICoreSerializable root, JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(document);

        SchemaVersion.EnsureKnown(document);
        JsonObject payload = (JsonObject)document.DeepClone();
        payload.Remove(SchemaVersion.PropertyName);
        _applier.Apply((CoreObject)root, payload);
    }

    private static CoreSerializerOptions CreateOptions(ICoreSerializable root, CoreSerializationMode mode)
    {
        return new CoreSerializerOptions
        {
            BaseUri = root is CoreObject coreObject ? coreObject.Uri : null,
            Mode = mode
        };
    }
}
