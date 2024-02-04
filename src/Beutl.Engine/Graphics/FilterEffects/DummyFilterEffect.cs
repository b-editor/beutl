using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

public sealed class DummyFilterEffect : FilterEffect, IDummy
{
    internal JsonObject? Json { get; set; }

    public override void ApplyTo(FilterEffectContext context)
    {
    }

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
