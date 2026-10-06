using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using NuGet.Versioning;

namespace Beutl.Services;

internal sealed record PackageInstallRequest(string PackageName, string? Version)
{
    internal const int MaxUriLength = 2048;

    public static bool TryParse(string? value, [NotNullWhen(true)] out PackageInstallRequest? request)
    {
        request = null;
        if (value is null || value.Length > MaxUriLength
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || !IsInstallUri(uri))
        {
            return false;
        }

        var query = QueryHelpers.ParseQuery(uri.Query);
        if (query.Keys.Any(key => key is not ("package" or "version"))
            || !query.TryGetValue("package", out var names) || names.Count != 1
            || names[0] is not { } name
            || !IsValidPackageName(name))
        {
            return false;
        }

        if (!TryReadVersion(query, out string? version))
        {
            return false;
        }

        request = new PackageInstallRequest(name, version);
        return true;
    }

    // beutl://install with nothing but a query: no path, port, user info or fragment.
    private static bool IsInstallUri(Uri uri)
        => uri.IsWellFormedOriginalString()
            && uri.Scheme.Equals("beutl", StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals("install", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath is ("" or "/")
            && uri.Port == -1 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

    // Up to 100 ASCII letters, digits, '.', '-' and '_', with at least one letter or digit.
    private static bool IsValidPackageName(string name)
        => name is { Length: > 0 and <= 100 }
            && name.Any(char.IsAsciiLetterOrDigit)
            && !name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '-' or '_'));

    // The version is optional, but when it is given it must be a single NuGet version.
    private static bool TryReadVersion(Dictionary<string, StringValues> query, out string? version)
    {
        version = null;
        if (query.TryGetValue("version", out var versions))
        {
            if (versions.Count != 1 || versions[0] is not { Length: > 0 and <= 100 } candidate
                || candidate.Any(char.IsWhiteSpace) || !NuGetVersion.TryParse(candidate, out _))
            {
                return false;
            }

            version = candidate;
        }

        return true;
    }
}
