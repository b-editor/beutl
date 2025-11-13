using System.Text.Json;

namespace Beutl.Protocol.Queries;

public class QueryResult
{
    public QueryResult(object? data, Dictionary<string, object?>? metadata = null)
    {
        Data = data;
        Metadata = metadata ?? new Dictionary<string, object?>();
    }

    public object? Data { get; }

    public Dictionary<string, object?> Metadata { get; }

    public string ToJson()
    {
        return JsonSerializer.Serialize(new { data = Data, metadata = Metadata });
    }

    public T? GetData<T>()
    {
        if (Data == null) return default;

        if (Data is T typed)
        {
            return typed;
        }

        // Try to deserialize from JSON if possible
        var json = JsonSerializer.Serialize(Data);
        return JsonSerializer.Deserialize<T>(json);
    }
}
