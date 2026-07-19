using System.Text.Json.Nodes;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beutl.AgentToolkit.Reconciliation;

internal static class ValidationValueNode
{
    private static ILogger? s_logger;

    // Log.LoggerFactory is assign-once and hands out a NullLoggerFactory until the host configures it,
    // so caching eagerly would let type-load order decide whether the warning below ever reaches a log.
    // Log.IsLoggerFactoryConfigured is internal to Beutl.Core and not visible here; the public getter
    // returns exactly NullLoggerFactory.Instance while unconfigured, which is the same signal.
    private static ILogger Logger
    {
        get
        {
            if (s_logger is not null) return s_logger;
            if (ReferenceEquals(Log.LoggerFactory, NullLoggerFactory.Instance)) return NullLogger.Instance;

            try
            {
                return s_logger = Log.CreateLogger(typeof(ValidationValueNode));
            }
            catch (Exception)
            {
                return NullLogger.Instance;
            }
        }
    }

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
            return CoreSerializer.SerializeToJsonNode(value, options);
        }
        catch (Exception ex)
        {
            // Throwing would restore the false failure this type exists to prevent, so degrade instead.
            Logger.LogWarning(ex, "Failed to serialize a validation payload of type {Type}.", value.GetType());
            return JsonValue.Create(value.ToString()) ?? JsonValue.Create(value.GetType().FullName);
        }
    }
}
