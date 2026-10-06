using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Beutl.Editor;

internal static partial class VersionControlSerializationGraph
{
    private sealed partial class SerializationGraphVisitor
    {
        // An object whose extension is not loaded keeps its saved JSON but has no contract to
        // inspect. Record the files that JSON demonstrably points at and skip strings that do not
        // resolve: failing the whole graph would take version control away from anyone who opens
        // a shared project without every extension installed.
        private void CaptureFallbackFileUris(JsonNode? node, Uri? baseUri)
        {
            switch (node)
            {
                case JsonValue value when value.TryGetValue(out string? text):
                    CaptureFallbackFileUri(text, baseUri);
                    break;
                case JsonArray array:
                    foreach (JsonNode? item in array)
                    {
                        CaptureFallbackFileUris(item, baseUri);
                    }

                    break;
                case JsonObject jsonObject:
                    foreach ((string name, JsonNode? item) in jsonObject)
                    {
                        CaptureFallbackFileUri(name, baseUri);
                        CaptureFallbackFileUris(item, baseUri);
                    }

                    break;
            }
        }

        private void CaptureFallbackFileUri(string? value, Uri? baseUri)
        {
            try
            {
                if (TryResolveOpaqueFileUri(
                        value,
                        baseUri,
                        allowExtensionlessRelative: false,
                        requireFilePath: false,
                        out Uri? uri)
                    && File.Exists(uri.LocalPath))
                {
                    _unaddressableFileSources.Add(uri);
                }
            }
            catch (Exception ex) when (ex is ArgumentException
                                       or IOException
                                       or NotSupportedException
                                       or UriFormatException)
            {
                // Unresolvable fallback data cannot name a file the snapshot would omit.
            }
        }

        private void CaptureOpaqueFileUris(JsonNode? node, Uri? baseUri)
        {
            switch (node)
            {
                case JsonValue value when value.TryGetValue(out string? text):
                    CaptureOpaqueFileUri(text, baseUri, allowExtensionlessRelative: true);
                    break;
                case JsonArray array:
                    foreach (JsonNode? item in array)
                    {
                        CaptureOpaqueFileUris(item, baseUri);
                    }

                    break;
                case JsonObject jsonObject:
                    foreach ((string name, JsonNode? item) in jsonObject)
                    {
                        CaptureOpaqueFileUri(name, baseUri, allowExtensionlessRelative: false);
                        CaptureOpaqueFileUris(item, baseUri);
                    }

                    break;
            }
        }

        private void CaptureOpaqueFileUri(
            string? value,
            Uri? baseUri,
            bool allowExtensionlessRelative)
        {
            // Text that resolves to no path, such as a font face name, names nothing an ignore rule or
            // a layout check could protect, so it is skipped instead of failing the whole project.
            if (TryResolveOpaqueFileUri(
                    value,
                    baseUri,
                    allowExtensionlessRelative,
                    requireFilePath: false,
                    out Uri? uri))
            {
                _unaddressableFileSources.Add(uri);
            }
        }

        private static bool TryResolveOpaqueFileUri(
            string? value,
            Uri? baseUri,
            bool allowExtensionlessRelative,
            bool requireFilePath,
            [NotNullWhen(true)] out Uri? uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (LooksLikeWindowsPath(value))
            {
                string normalized = value.Replace('\\', '/')
                    .Replace("#", "%23", StringComparison.Ordinal)
                    .Replace("?", "%3F", StringComparison.Ordinal);
                return Uri.TryCreate($"file:///{normalized}", UriKind.Absolute, out uri);
            }

            if (Path.IsPathFullyQualified(value))
            {
                string fullPath = Path.GetFullPath(value);
                if (!File.Exists(fullPath)
                    && !Directory.Exists(fullPath)
                    && !LooksLikeFilePath(value)
                    && !requireFilePath)
                {
                    return false;
                }

                uri = CreateFileUri(fullPath);
                return true;
            }

            if (Uri.TryCreate(value, UriKind.Absolute, out Uri? absoluteUri))
            {
                if (!absoluteUri.IsFile)
                {
                    return false;
                }

                uri = CanonicalizeFileUri(absoluteUri);
                return true;
            }

            if (baseUri is { IsFile: true })
            {
                try
                {
                    string? directory = Path.GetDirectoryName(baseUri.LocalPath);
                    if (directory is not null)
                    {
                        string rawPath = Path.GetFullPath(Path.Combine(directory, value));
                        if (File.Exists(rawPath) || Directory.Exists(rawPath))
                        {
                            uri = CreateFileUri(rawPath);
                            return true;
                        }
                    }
                }
                catch (Exception ex) when (ex is ArgumentException
                                           or IOException
                                           or NotSupportedException)
                {
                    // Fall through to the URI-based check and fail closed for path-like data.
                }
            }

            bool looksLikeFilePath = LooksLikeFilePath(value);
            string relativeReference = value
                .Replace("#", "%23", StringComparison.Ordinal)
                .Replace("?", "%3F", StringComparison.Ordinal);
            if ((!allowExtensionlessRelative && !looksLikeFilePath)
                || baseUri is null
                || !Uri.TryCreate(baseUri, relativeReference, out Uri? resolved)
                || !resolved.IsFile)
            {
                return false;
            }

            if (!looksLikeFilePath
                && !File.Exists(resolved.LocalPath)
                && !Directory.Exists(resolved.LocalPath)
                && !requireFilePath)
            {
                return false;
            }

            uri = CanonicalizeFileUri(resolved);
            return true;
        }

        private static Uri CanonicalizeFileUri(Uri uri)
        {
            return CreateFileUri(uri.LocalPath);
        }

        private static Uri CreateFileUri(string path)
        {
            return new UriBuilder
            {
                Scheme = Uri.UriSchemeFile,
                Host = string.Empty,
                Path = Path.GetFullPath(path),
            }.Uri;
        }

        private static bool LooksLikeFilePath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || LooksLikeNumericSerialization(value))
            {
                return false;
            }

            string extension = Path.GetExtension(value);
            return LooksLikeWindowsPath(value)
                   || value.StartsWith("./", StringComparison.Ordinal)
                   || value.StartsWith("../", StringComparison.Ordinal)
                   || value.StartsWith(".\\", StringComparison.Ordinal)
                   || value.StartsWith("..\\", StringComparison.Ordinal)
                   || extension.Length > 1 && extension.Skip(1).Any(char.IsLetter);
        }

        private static bool LooksLikeNumericSerialization(string value)
        {
            string candidate = value.Trim().Trim('<', '>', '(', ')', '[', ']');
            if (double.TryParse(
                    candidate,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                return true;
            }

            string[] parts = candidate.Split(
                [',', '/', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length > 1
                   && parts.All(part => double.TryParse(
                       part,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out _));
        }

        private static bool LooksLikeWindowsPath(string value)
        {
            return value.Length >= 3
                   && char.IsAsciiLetter(value[0])
                   && value[1] == ':'
                   && value[2] is '/' or '\\';
        }
    }
}
