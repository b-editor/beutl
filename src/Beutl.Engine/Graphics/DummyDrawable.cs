using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.Graphics;

public sealed class DummyDrawable : Drawable, IDummy
{
    internal JsonObject? Json { get; set; }

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        Json = json;
    }

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        if (Json != null)
        {
            JsonDeepClone.CopyTo(Json, json);
        }
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

    protected override Size MeasureCore(Size availableSize)
    {
        return Size.Empty;
    }

    protected override void OnDraw(ICanvas canvas)
    {
    }

    public bool TryGetTypeName([NotNullWhen(true)] out string? result)
    {
        result = null;
        return Json?.TryGetDiscriminator(out result) == true;
    }
}
