using System.Text.Json;

using Beutl.Logging;

using Microsoft.Extensions.Logging;

using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Licenses;
using NuGet.Versioning;

namespace Beutl.Api.Services;

public class AcceptedLicenseManager : IBeutlApiResource
{
    private readonly ILogger _logger = Log.CreateLogger<AcceptedLicenseManager>();
    private readonly Dictionary<PackageIdentity, LicenseMetadata> _accepted = [];
    private const string FileName = "accepted-licenses.json";

    public AcceptedLicenseManager()
    {
        Restore();
    }

    public IReadOnlyDictionary<PackageIdentity, LicenseMetadata> Accepted => _accepted;

    public void Accepts(IReadOnlyList<(PackageIdentity, LicenseMetadata)> list)
    {
        if (list.Count == 0)
            return;

        foreach ((PackageIdentity, LicenseMetadata) item in list)
        {
            _accepted.TryAdd(item.Item1, item.Item2);
        }

        Save();
    }

    private void Save()
    {
        string fileName = Path.Combine(Helper.AppRoot, FileName);
        using (FileStream stream = File.Create(fileName))
        {
            JsonSerializer.Serialize(stream, _accepted
                .Select(AccepedPackageInfo.Create)
                .ToArray());
        }
    }

    private void Restore()
    {
        string fileName = Path.Combine(Helper.AppRoot, FileName);
        if (File.Exists(fileName))
        {
            using (FileStream stream = File.OpenRead(fileName))
            {
                try
                {
                    if (JsonSerializer.Deserialize<AccepedPackageInfo[]>(stream) is AccepedPackageInfo[] packages)
                    {
                        _accepted.Clear();

                        _accepted.AddRange(packages.Select(x =>
                            new KeyValuePair<PackageIdentity, LicenseMetadata>(x.Package.ToIdentity(), x.License.ToMetadata())));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to restore file.");
                }
            }
        }
    }

    // Serializable objects
    private record AccepedPackageInfo(PackageInfo Package, LicenseInfo License)
    {
        public static AccepedPackageInfo Create(KeyValuePair<PackageIdentity, LicenseMetadata> item)
        {
            return new AccepedPackageInfo(
                new PackageInfo(item.Key.Id, item.Key.Version.ToString()),
                new LicenseInfo(item.Value.Type, item.Value.License, item.Value.Version.ToString()));
        }
    }

    private record PackageInfo(string Name, string Version)
    {
        public PackageIdentity ToIdentity()
        {
            return new PackageIdentity(Name, new NuGetVersion(Version));
        }
    }

    private record LicenseInfo(LicenseType Type, string License, string Version)
    {
        public LicenseMetadata ToMetadata()
        {
            return new LicenseMetadata(
                type: Type,
                license: License,
                expression: Type == LicenseType.Expression ? NuGetLicenseExpression.Parse(License) : null,
                warningsAndErrors: Array.Empty<string>(),
                version: new Version(Version));
        }
    }
}
