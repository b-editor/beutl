using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Beutl.Api.Services;

/// <summary>
/// Strict parser for the optional static Marketplace analytics manifest. This parser
/// deliberately has no extension point: malformed or future schemas are rejected
/// instead of falling back to package/type names.
/// </summary>
internal sealed class AnalyticsFeatureManifest
{
    internal const string PackagePath = "beutl/analytics-features.v1.json";
    internal const int MaxBytes = 64 * 1024;
    internal const int MaxFeatures = 128;
    internal const int MaxTypesPerFeature = 8;

    private static readonly Regex s_kindPattern = new("^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex s_keyPattern = new("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private AnalyticsFeatureManifest(IReadOnlyList<AnalyticsFeatureDefinition> features, string sha256)
    {
        Features = features;
        Sha256 = sha256;
    }

    internal IReadOnlyList<AnalyticsFeatureDefinition> Features { get; }

    internal string Sha256 { get; }

    internal static AnalyticsFeatureManifest? TryLoadFromPackageFile(string packageFile, string? approvedSha256)
    {
        if (!IsSha256(approvedSha256)) return null;

        try
        {
            using var archive = ZipFile.OpenRead(packageFile);
            return TryLoadFromArchive(archive, approvedSha256);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static AnalyticsFeatureManifest? TryLoadFromArchive(
        ZipArchive archive,
        string? approvedSha256)
    {
        if (!IsSha256(approvedSha256)) return null;

        ZipArchiveEntry[] entries = archive.Entries
            .Where(entry => string.Equals(entry.FullName, PackagePath, StringComparison.Ordinal))
            .ToArray();
        if (entries.Length != 1 || entries[0].Length > MaxBytes) return null;

        using Stream stream = entries[0].Open();
        return ReadAtMost(stream) is { } bytes
            ? TryParseApproved(bytes, approvedSha256!)
            : null;
    }

    internal static AnalyticsFeatureManifest? TryLoadFromInstalledDirectory(string installedDirectory, string? approvedSha256)
    {
        if (!IsSha256(approvedSha256)) return null;

        try
        {
            string manifestPath = Path.Combine(installedDirectory, "beutl", "analytics-features.v1.json");
            var info = new FileInfo(manifestPath);
            if (!info.Exists || info.Length > MaxBytes) return null;
            using FileStream stream = File.OpenRead(manifestPath);
            return ReadAtMost(stream) is { } bytes
                ? TryParseApproved(bytes, approvedSha256!)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal AnalyticsFeatureDefinition? Find(string assemblyName, string typeName)
    {
        return Features.FirstOrDefault(feature => feature.Types.Any(type =>
            string.Equals(type.Assembly, assemblyName, StringComparison.Ordinal)
            && string.Equals(type.Type, typeName, StringComparison.Ordinal)));
    }

    private static AnalyticsFeatureManifest? TryParseApproved(byte[] bytes, string approvedSha256)
    {
        if (bytes.Length > MaxBytes) return null;
        string sha256 = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(sha256, approvedSha256, StringComparison.OrdinalIgnoreCase)) return null;
        return TryParse(bytes, sha256, out AnalyticsFeatureManifest? manifest) ? manifest : null;
    }

    private static byte[]? ReadAtMost(Stream stream)
    {
        using var result = new MemoryStream();
        byte[] buffer = new byte[8192];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            if (result.Length + read > MaxBytes)
            {
                return null;
            }

            result.Write(buffer, 0, read);
        }

        return result.ToArray();
    }

    internal static bool TryParse(ReadOnlySpan<byte> bytes, string sha256, out AnalyticsFeatureManifest? manifest)
    {
        manifest = null;
        if (bytes.Length > MaxBytes || !IsSha256(sha256)) return false;

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.ToArray());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyProperties(root, "schemaVersion", "features")) return false;
            if (!root.TryGetProperty("schemaVersion", out JsonElement schema)
                || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out int schemaVersion)
                || schemaVersion != 1)
            {
                return false;
            }

            if (!root.TryGetProperty("features", out JsonElement featuresElement)
                || featuresElement.ValueKind != JsonValueKind.Array
                || featuresElement.GetArrayLength() is 0 or > MaxFeatures)
            {
                return false;
            }

            var features = new List<AnalyticsFeatureDefinition>(featuresElement.GetArrayLength());
            var featureKeys = new HashSet<string>(StringComparer.Ordinal);
            var types = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement featureElement in featuresElement.EnumerateArray())
            {
                if (featureElement.ValueKind != JsonValueKind.Object
                    || !HasOnlyProperties(featureElement, "kind", "key", "types")
                    || !TryGetString(featureElement, "kind", out string? kind)
                    || !TryGetString(featureElement, "key", out string? key)
                    || !s_kindPattern.IsMatch(kind)
                    || !s_keyPattern.IsMatch(key)
                    || !featureElement.TryGetProperty("types", out JsonElement typeElements)
                    || typeElements.ValueKind != JsonValueKind.Array
                    || typeElements.GetArrayLength() is 0 or > MaxTypesPerFeature)
                {
                    return false;
                }

                if (!featureKeys.Add($"{kind}\u001f{key}")) return false;
                var featureTypes = new List<AnalyticsFeatureType>(typeElements.GetArrayLength());
                foreach (JsonElement typeElement in typeElements.EnumerateArray())
                {
                    if (typeElement.ValueKind != JsonValueKind.Object
                        || !HasOnlyProperties(typeElement, "assembly", "type")
                        || !TryGetString(typeElement, "assembly", out string? assembly)
                        || !TryGetString(typeElement, "type", out string? type)
                        || !IsAbsoluteClrName(assembly)
                        || !IsAbsoluteClrName(type)
                        || !types.Add($"{assembly}\u001f{type}"))
                    {
                        return false;
                    }

                    featureTypes.Add(new AnalyticsFeatureType(assembly, type));
                }

                features.Add(new AnalyticsFeatureDefinition(kind, key, featureTypes));
            }

            manifest = new AnalyticsFeatureManifest(features, sha256.ToUpperInvariant());
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasOnlyProperties(JsonElement element, params string[] names)
    {
        var allowed = new HashSet<string>(names, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().All(property =>
            allowed.Contains(property.Name) && seen.Add(property.Name));
    }

    private static bool TryGetString(JsonElement element, string property, [NotNullWhen(true)] out string? value)
    {
        value = null;
        return element.TryGetProperty(property, out JsonElement propertyValue)
            && propertyValue.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value = propertyValue.GetString());
    }

    private static bool IsAbsoluteClrName(string value)
    {
        return value.Length is >= 1 and <= 256
            && !value.StartsWith(".", StringComparison.Ordinal)
            && !value.Contains('/')
            && !value.Contains('\\')
            && !value.Contains("..", StringComparison.Ordinal);
    }

    private static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }
}

internal sealed record AnalyticsFeatureDefinition(string Kind, string Key, IReadOnlyList<AnalyticsFeatureType> Types);

internal sealed record AnalyticsFeatureType(string Assembly, string Type);
