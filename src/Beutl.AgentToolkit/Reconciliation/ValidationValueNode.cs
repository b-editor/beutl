using System.Text.Json.Nodes;
using Beutl.JsonConverters;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.AgentToolkit.Reconciliation;

internal static class ValidationValueNode
{
    private static ILogger? s_logger;

    private static ILogger Logger => Log.GetLoggerOnceConfigured(ref s_logger, typeof(ValidationValueNode));

    // System.Text.Json cannot write a live engine object: IProperty.ValueType is a System.Type, and
    // IFileSource needs a serialization context that is gone by the time the response is written.
    // options carries the document's BaseUri, without which a media reference is reported as an
    // absolute host path while the document itself reports it relative.
    public static JsonNode? From(object? value, CoreSerializerOptions? options)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonNode node:
                return node.DeepClone();
            case string text:
                return JsonValue.Create(text);
        }

        try
        {
            using (MediaTimeJsonConverter.WriteTimeSpanStrings())
            {
                return CoreSerializer.SerializeToJsonNode(value, options);
            }
        }
        catch (Exception ex)
        {
            // Throwing would restore the false failure this type exists to prevent, so degrade instead.
            Logger.LogWarning(ex, "Failed to serialize a validation payload of type {Type}.", value.GetType());
            return JsonValue.Create(value.ToString()) ?? JsonValue.Create(value.GetType().FullName);
        }
    }
}
