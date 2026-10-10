using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace Beutl.AgentToolkit.Live;

// ToolsVersion changes whenever the editor's tool set changes (a package added or removed), so
// the installed server, which only watches this registry, can announce the new tool list.
public sealed record AgentHostInstanceRegistration(
    string InstanceId, int ProcessId, long ProcessStartTime, Uri EndpointUri, long ToolsVersion = 0);

// Discovery is scoped to the Beutl profile. Entries contain no credentials; hosts and the live MCP
// broker in the same profile authenticate forwarding with the live MCP token of that profile.
public sealed class AgentHostInstanceRegistry(string directory)
{
    public const string DirectoryName = "agent-hosts";

    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    public static string GetDefaultDirectory(string profileDirectory)
        => Path.Combine(profileDirectory, DirectoryName);

    public string Location => directory;

    public AgentHostInstanceLease Register(string instanceId, Uri endpointUri, long toolsVersion = 0)
    {
        using Process process = Process.GetCurrentProcess();
        var registration = new AgentHostInstanceRegistration(
            instanceId, process.Id, GetProcessStartTime(process), endpointUri, toolsVersion);
        Write(registration);
        return new AgentHostInstanceLease(this, registration);
    }

    internal void Write(AgentHostInstanceRegistration registration)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, registration.InstanceId + ".json");
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(registration, s_jsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
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
}

// Removes the registration on dispose unless another process replaced it meanwhile.
public sealed class AgentHostInstanceLease : IDisposable
{
    private readonly AgentHostInstanceRegistry _registry;
    private AgentHostInstanceRegistration _registration;

    internal AgentHostInstanceLease(AgentHostInstanceRegistry registry, AgentHostInstanceRegistration registration)
    {
        _registry = registry;
        _registration = registration;
    }

    // Republishes the registration with a new tools version. The installed server watches the
    // registry, so this is how a changed tool set of a running editor reaches its clients.
    public void UpdateTools(long toolsVersion)
    {
        AgentHostInstanceRegistration updated = _registration with { ToolsVersion = toolsVersion };
        _registry.Write(updated);
        _registration = updated;
    }

    public void Dispose() => _registry.RemoveIfUnchanged(_registration);
}
