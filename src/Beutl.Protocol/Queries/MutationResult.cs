using System.Text.Json;

namespace Beutl.Protocol.Queries;

public class MutationResult
{
    public MutationResult(bool success, object? data = null, string? error = null, Dictionary<string, object?>? metadata = null)
    {
        Success = success;
        Data = data;
        Error = error;
        Metadata = metadata ?? new Dictionary<string, object?>();
    }

    public bool Success { get; }

    public object? Data { get; }

    public string? Error { get; }

    public Dictionary<string, object?> Metadata { get; }

    public string ToJson()
    {
        return JsonSerializer.Serialize(new
        {
            success = Success,
            data = Data,
            error = Error,
            metadata = Metadata
        });
    }

    public static MutationResult CreateSuccess(object? data = null, Dictionary<string, object?>? metadata = null)
    {
        return new MutationResult(true, data, null, metadata);
    }

    public static MutationResult CreateError(string error, Dictionary<string, object?>? metadata = null)
    {
        return new MutationResult(false, null, error, metadata);
    }
}
