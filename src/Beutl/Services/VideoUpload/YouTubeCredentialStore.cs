using System.Text.Json.Nodes;
using Google.Apis.Json;
using Google.Apis.Util.Store;

namespace Beutl.Services.VideoUpload;

// One YouTube account per Beutl profile. Writes are staged until sign-in succeeds;
// an old credential refreshing in the background cannot undo a sign-out.
internal sealed class YouTubeCredentialStore(string path, string clientId) : IDataStore
{
    private readonly object _gate = new();
    private JsonObject? _pending;
    private bool _committed;
    private bool _disabled;

    public Task StoreAsync<T>(string key, T value)
    {
        lock (_gate)
        {
            if (_disabled) return Task.CompletedTask;
            _pending = new JsonObject
            {
                ["clientId"] = clientId,
                ["key"] = typeof(T).FullName + ":" + key,
                ["value"] = JsonNode.Parse(NewtonsoftJsonSerializer.Instance.Serialize(value))
            };
            if (_committed) Save();
        }
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        lock (_gate)
        {
            if (_disabled) return Task.FromResult(default(T)!);
            JsonObject? document = _pending;
            if (document == null && File.Exists(path))
            {
                try { document = JsonNode.Parse(File.ReadAllText(path)) as JsonObject; }
                catch (System.Text.Json.JsonException) { }
            }
            if (document?["clientId"]?.GetValue<string>() == clientId
                && document["key"]?.GetValue<string>() == typeof(T).FullName + ":" + key
                && document["value"] is { } value)
                return Task.FromResult(NewtonsoftJsonSerializer.Instance.Deserialize<T>(value.ToJsonString()));
            return Task.FromResult(default(T)!);
        }
    }

    public Task DeleteAsync<T>(string key) => ClearAsync();

    public Task ClearAsync()
    {
        lock (_gate)
        {
            if (_disabled) return Task.CompletedTask;
            _pending = null;
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    public void Commit()
    {
        lock (_gate)
        {
            if (_disabled) return;
            _committed = true;
            if (_pending != null) Save();
        }
    }

    public void Disable(bool clear)
    {
        lock (_gate)
        {
            _disabled = true;
            _pending = null;
            if (clear) File.Delete(path);
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _pending!.JsonSave(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}
