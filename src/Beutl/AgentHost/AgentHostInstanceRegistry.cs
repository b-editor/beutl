using System.Diagnostics;
using System.Globalization;
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
            instanceId, process.Id, GetProcessStartTime(process), endpointUri);
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

        return new RegistrationLease(this, registration);
    }

    public IReadOnlyList<AgentHostInstanceRegistration> Read()
    {
        string[] paths;
        try { paths = Directory.GetFiles(directory, "*.json"); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }

        var result = new List<AgentHostInstanceRegistration>();
        foreach (string path in paths)
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

                try
                {
                    using Process process = Process.GetProcessById(registration.ProcessId);
                    if (!process.HasExited && GetProcessStartTime(process) == registration.ProcessStartTime)
                        result.Add(registration);
                    else
                        RemoveIfUnchanged(registration);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    RemoveIfUnchanged(registration);
                }
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

    private static long GetProcessStartTime(Process process)
    {
        if (!OperatingSystem.IsLinux())
            return process.StartTime.ToUniversalTime().Ticks;

        // .NET derives Linux StartTime from a per-process sampled boot wall clock, so its UTC
        // ticks differ across readers. The kernel's /proc starttime is an exact boot-relative ID.
        string stat = File.ReadAllText($"/proc/{process.Id}/stat");
        string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return long.Parse(fields[19], CultureInfo.InvariantCulture); // Field 22, after pid and (comm).
    }

    public void RemoveIfUnchanged(AgentHostInstanceRegistration expected)
    {
        string path = Path.Combine(directory, expected.InstanceId + ".json");
        string claimedPath = path + ".prune-" + Guid.NewGuid().ToString("N");
        bool restore = false;
        try
        {
            // Claim the actual file atomically, then compare ownership. Comparing and deleting
            // the original path would race a registration replaced between those two operations.
            File.Move(path, claimedPath);
            restore = true;
            var current = JsonSerializer.Deserialize<AgentHostInstanceRegistration>(
                File.ReadAllText(claimedPath), s_jsonOptions);
            if (current == expected)
            {
                File.Delete(claimedPath);
                restore = false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        finally
        {
            if (restore)
            {
                try { File.Move(claimedPath, path); }
                catch (IOException)
                {
                    // A newer registration already published at the original path wins.
                    if (File.Exists(path))
                    {
                        try { File.Delete(claimedPath); }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static bool IsLocalEndpoint(Uri? uri)
        => uri is { IsAbsoluteUri: true, Scheme: "http", Host: "127.0.0.1", AbsolutePath: "/mcp" }
           && uri.Port > 0 && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0;

    private sealed class RegistrationLease(AgentHostInstanceRegistry registry, AgentHostInstanceRegistration registration) : IDisposable
    {
        public void Dispose()
        {
            registry.RemoveIfUnchanged(registration);
        }
    }
}
