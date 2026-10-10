using System.Diagnostics;
using System.Text.Json;

namespace Beutl.AgentHost;

internal sealed record AgentHostInstanceRegistration(
    string InstanceId, int ProcessId, long ProcessStartTime, Uri EndpointUri);

// Discovery is scoped to the Beutl profile. Entries contain no credentials; hosts in the same
// profile authenticate forwarding with the live MCP token already used by the agent connection.
internal sealed class AgentHostInstanceRegistry(string directory)
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public IDisposable Register(string instanceId, Uri endpointUri)
    {
        using Process process = Process.GetCurrentProcess();
        var registration = new AgentHostInstanceRegistration(
            instanceId, process.Id, process.StartTime.ToUniversalTime().Ticks, endpointUri);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, instanceId + ".json");
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(registration, s_jsonOptions));
            File.Move(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }

        return new RegistrationLease(path);
    }

    public IReadOnlyList<AgentHostInstanceRegistration> Read()
    {
        if (!Directory.Exists(directory))
            return [];

        var result = new List<AgentHostInstanceRegistration>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var registration = JsonSerializer.Deserialize<AgentHostInstanceRegistration>(
                    File.ReadAllText(path), s_jsonOptions);
                if (registration is null
                    || !Guid.TryParseExact(registration.InstanceId, "N", out _)
                    || Path.GetFileNameWithoutExtension(path) != registration.InstanceId
                    || !IsLocalEndpoint(registration.EndpointUri))
                    continue;

                using Process process = Process.GetProcessById(registration.ProcessId);
                if (process.StartTime.ToUniversalTime().Ticks == registration.ProcessStartTime)
                    result.Add(registration);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or JsonException or ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
            {
                // A host can exit during discovery. Ignore stale or incomplete registrations.
            }
        }

        return result;
    }

    private static bool IsLocalEndpoint(Uri? uri)
        => uri is { IsAbsoluteUri: true, Scheme: "http", Host: "127.0.0.1", AbsolutePath: "/mcp" }
           && uri.Port > 0 && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    private sealed class RegistrationLease(string path) : IDisposable
    {
        public void Dispose()
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
