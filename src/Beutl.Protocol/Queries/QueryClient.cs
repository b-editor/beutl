using System.Reactive.Subjects;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace Beutl.Protocol.Queries;

public class QueryClient : IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly Dictionary<string, Subject<QueryUpdate>> _subscriptionSubjects = new();
    private readonly Subject<string> _errors = new();

    public QueryClient(HubConnection connection)
    {
        _connection = connection;
        SetupClientHandlers();
    }

    public IObservable<string> Errors => _errors;

    public async Task<QueryResult?> ExecuteQueryAsync(QuerySchema schema, Guid? targetId = null)
    {
        try
        {
            string queryJson = SerializeQuerySchema(schema);
            string? targetIdStr = targetId?.ToString();

            string resultJson = await _connection.InvokeAsync<string>("ExecuteQueryAsync", queryJson, targetIdStr);

            return DeserializeQueryResult(resultJson);
        }
        catch (Exception ex)
        {
            _errors.OnNext($"Query execution failed: {ex.Message}");
            return null;
        }
    }

    public async Task<MutationResult?> ExecuteMutationAsync(Mutation mutation, QuerySchema? returnSchema = null)
    {
        try
        {
            string mutationJson = SerializeMutation(mutation);
            string? returnSchemaJson = returnSchema != null ? SerializeQuerySchema(returnSchema) : null;

            string resultJson = await _connection.InvokeAsync<string>("ExecuteMutationAsync", mutationJson, returnSchemaJson);

            return DeserializeMutationResult(resultJson);
        }
        catch (Exception ex)
        {
            _errors.OnNext($"Mutation execution failed: {ex.Message}");
            return null;
        }
    }

    public async Task<IObservable<QueryUpdate>?> SubscribeAsync(string subscriptionId, QuerySchema schema, Guid? targetId = null)
    {
        try
        {
            string queryJson = SerializeQuerySchema(schema);
            string? targetIdStr = targetId?.ToString();

            bool success = await _connection.InvokeAsync<bool>("SubscribeAsync", subscriptionId, queryJson, targetIdStr);

            if (!success)
            {
                _errors.OnNext($"Subscription '{subscriptionId}' failed.");
                return null;
            }

            var subject = new Subject<QueryUpdate>();
            _subscriptionSubjects[subscriptionId] = subject;

            return subject;
        }
        catch (Exception ex)
        {
            _errors.OnNext($"Subscription failed: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> UnsubscribeAsync(string subscriptionId)
    {
        try
        {
            bool success = await _connection.InvokeAsync<bool>("UnsubscribeAsync", subscriptionId);

            if (_subscriptionSubjects.Remove(subscriptionId, out var subject))
            {
                subject.OnCompleted();
            }

            return success;
        }
        catch (Exception ex)
        {
            _errors.OnNext($"Unsubscribe failed: {ex.Message}");
            return false;
        }
    }

    public async Task<QueryResult?> GetSubscriptionStateAsync(string subscriptionId)
    {
        try
        {
            string resultJson = await _connection.InvokeAsync<string>("GetSubscriptionStateAsync", subscriptionId);
            return DeserializeQueryResult(resultJson);
        }
        catch (Exception ex)
        {
            _errors.OnNext($"Get subscription state failed: {ex.Message}");
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var subject in _subscriptionSubjects.Values)
        {
            subject.OnCompleted();
        }
        _subscriptionSubjects.Clear();
        _errors.OnCompleted();
    }

    private void SetupClientHandlers()
    {
        _connection.On<string, string>("ReceiveSubscriptionUpdateAsync", (subscriptionId, updateJson) =>
        {
            if (_subscriptionSubjects.TryGetValue(subscriptionId, out var subject))
            {
                try
                {
                    QueryUpdate? update = DeserializeQueryUpdate(updateJson);
                    if (update != null)
                    {
                        subject.OnNext(update);
                    }
                }
                catch
                {
                    // Ignore deserialization errors
                }
            }
        });

        _connection.On<string>("ReceiveQueryErrorAsync", (errorJson) =>
        {
            _errors.OnNext(errorJson);
        });
    }

    private string SerializeQuerySchema(QuerySchema schema)
    {
        var fields = SerializeFields(schema.Fields);
        return JsonSerializer.Serialize(new { fields });
    }

    private object[] SerializeFields(QueryField[] fields)
    {
        var result = new List<object>();

        foreach (var field in fields)
        {
            if (field.HasSubFields)
            {
                result.Add(new Dictionary<string, object>
                {
                    [field.Name] = SerializeFields(field.SubFields)
                });
            }
            else
            {
                result.Add(field.Name);
            }
        }

        return result.ToArray();
    }

    private string SerializeMutation(Mutation mutation)
    {
        return mutation switch
        {
            UpdatePropertyMutation m => JsonSerializer.Serialize(new
            {
                type = "UpdateProperty",
                targetId = m.TargetId,
                propertyName = m.PropertyName,
                newValue = m.NewValue
            }),
            AddToCollectionMutation m => JsonSerializer.Serialize(new
            {
                type = "AddToCollection",
                targetId = m.TargetId,
                collectionPropertyName = m.CollectionPropertyName,
                item = m.Item,
                index = m.Index
            }),
            RemoveFromCollectionMutation m => JsonSerializer.Serialize(new
            {
                type = "RemoveFromCollection",
                targetId = m.TargetId,
                collectionPropertyName = m.CollectionPropertyName,
                index = m.Index
            }),
            CreateObjectMutation m => JsonSerializer.Serialize(new
            {
                type = "CreateObject",
                parentId = m.ParentId,
                propertyName = m.PropertyName,
                typeName = m.TypeName,
                initialValues = m.InitialValues
            }),
            DeleteObjectMutation m => JsonSerializer.Serialize(new
            {
                type = "DeleteObject",
                targetId = m.TargetId
            }),
            _ => throw new ArgumentException($"Unknown mutation type: {mutation.GetType().Name}")
        };
    }

    private QueryResult? DeserializeQueryResult(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            object? data = null;
            if (root.TryGetProperty("data", out var dataElement))
            {
                data = JsonSerializer.Deserialize<Dictionary<string, object?>>(dataElement.GetRawText());
            }

            Dictionary<string, object?>? metadata = null;
            if (root.TryGetProperty("metadata", out var metadataElement))
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, object?>>(metadataElement.GetRawText());
            }

            return new QueryResult(data, metadata);
        }
        catch
        {
            return null;
        }
    }

    private MutationResult? DeserializeMutationResult(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            bool success = root.TryGetProperty("success", out var successElement) && successElement.GetBoolean();

            object? data = null;
            if (root.TryGetProperty("data", out var dataElement))
            {
                data = JsonSerializer.Deserialize<Dictionary<string, object?>>(dataElement.GetRawText());
            }

            string? error = null;
            if (root.TryGetProperty("error", out var errorElement))
            {
                error = errorElement.GetString();
            }

            Dictionary<string, object?>? metadata = null;
            if (root.TryGetProperty("metadata", out var metadataElement))
            {
                metadata = JsonSerializer.Deserialize<Dictionary<string, object?>>(metadataElement.GetRawText());
            }

            return new MutationResult(success, data, error, metadata);
        }
        catch
        {
            return null;
        }
    }

    private QueryUpdate? DeserializeQueryUpdate(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            object? updatedData = null;
            if (root.TryGetProperty("updatedData", out var dataElement))
            {
                updatedData = JsonSerializer.Deserialize<Dictionary<string, object?>>(dataElement.GetRawText());
            }

            DateTime timestamp = DateTime.UtcNow;
            if (root.TryGetProperty("timestamp", out var timestampElement))
            {
                timestamp = timestampElement.GetDateTime();
            }

            return new QueryUpdate
            {
                UpdatedData = updatedData,
                Timestamp = timestamp
            };
        }
        catch
        {
            return null;
        }
    }
}
