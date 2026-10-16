using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Api.Services;

// Saves the packages that passed the API compatibility check, so that an unchanged package is not checked on every
// load. Only passes are saved, so a failure is checked again.
internal sealed class PackageApiCheckCache(string path)
{
    private readonly ILogger _logger = Log.CreateLogger<PackageApiCheckCache>();
    private readonly Lock _gate = new();
    private Dictionary<string, string>? _passes;

    public PackageApiCheckCache()
        : this(Path.Combine(Helper.AppRoot, "packageApiChecks.json"))
    {
    }

    public bool HasPassed(string packageId, string key)
    {
        lock (_gate)
        {
            return Passes.TryGetValue(packageId, out string? saved) && saved == key;
        }
    }

    public void SavePass(string packageId, string key)
    {
        lock (_gate)
        {
            Passes[packageId] = key;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = new JsonObject();
                foreach ((string id, string saved) in Passes)
                {
                    json[id] = saved;
                }

                json.JsonSave(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Unable to save the package API check results to {Path}.", path);
            }
        }
    }

    // Hashes what decides a pass: the versions, the checker's build (local builds share one version), and the assemblies.
    public static string CreateKey(
        string packageVersion,
        IEnumerable<string> packageAssemblies,
        IReadOnlyDictionary<string, string?> hostAssemblies,
        string hostVersion)
    {
        var text = new StringBuilder($"{packageVersion}\n{hostVersion}\n{typeof(PackageApiCheckCache).Module.ModuleVersionId}\n");
        var referenced = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in packageAssemblies.Order(StringComparer.Ordinal))
        {
            using var peReader = new PEReader(File.OpenRead(file));
            if (peReader.HasMetadata)
            {
                MetadataReader reader = peReader.GetMetadataReader();
                text.Append($"{Path.GetFileName(file)} {reader.GetGuid(reader.GetModuleDefinition().Mvid)}\n");
                foreach (AssemblyReferenceHandle handle in reader.AssemblyReferences)
                {
                    referenced.Add(reader.GetString(reader.GetAssemblyReference(handle).Name));
                }
            }
        }

        var host = new Dictionary<string, string?>(hostAssemblies, StringComparer.OrdinalIgnoreCase);
        foreach (string name in referenced)
        {
            if (host.TryGetValue(name, out string? file))
            {
                text.Append($"{name} {(file != null && File.Exists(file) ? ReadMvid(file) : "-")}\n");
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private Dictionary<string, string> Passes => _passes ??= Read();

    private static string ReadMvid(string file)
    {
        using var peReader = new PEReader(File.OpenRead(file));
        MetadataReader reader = peReader.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString();
    }

    // An unreadable or malformed store reads as empty, so every package is checked again.
    private Dictionary<string, string> Read()
    {
        var passes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (JsonHelper.JsonRestore(path) is JsonObject json)
            {
                foreach ((string id, JsonNode? value) in json)
                {
                    if (value is JsonValue saved && saved.TryGetValue(out string? key))
                    {
                        passes[id] = key;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to read the package API check results from {Path}.", path);
            passes.Clear();
        }

        return passes;
    }
}
