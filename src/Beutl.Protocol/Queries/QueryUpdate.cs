using System.Text.Json;
using Beutl.Protocol.Operations;

namespace Beutl.Protocol.Queries;

public class QueryUpdate
{
    public SyncOperation? Operation { get; init; }

    public object? UpdatedData { get; init; }

    public DateTime Timestamp { get; init; }

    public string ToJson()
    {
        return JsonSerializer.Serialize(new
        {
            operation = Operation?.GetType().Name,
            updatedData = UpdatedData,
            timestamp = Timestamp
        });
    }
}
