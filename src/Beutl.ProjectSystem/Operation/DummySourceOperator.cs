using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.Operation;

public sealed class DummySourceOperator : SourceOperator, IDummy
{
    internal JsonObject? Json { get; set; }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        if (Json != null)
        {
            (context as IJsonSerializationContext)?.SetJsonObject(Json);
        }
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        Json = (context as IJsonSerializationContext)?.GetJsonObject();
    }

    public bool TryGetTypeName([NotNullWhen(true)] out string? result)
    {
        result = null;
        return Json?.TryGetDiscriminator(out result) == true;
    }
}
