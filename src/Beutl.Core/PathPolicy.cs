using System.Globalization;
using System.Text;

namespace Beutl;

// Lexical operations preserve case and do not inspect the filesystem.
// Use FilePathComparison for on-disk identity or containment, passing paths before lexical normalization.
public static class PathPolicy
{
    private const string UnescapedPathCharacters = "-._~!$&'()*+,;=:@[]";

    public static string NormalizeLexicalPath(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool AreSameLexicalPath(string left, string right)
        => string.Equals(NormalizeLexicalPath(left), NormalizeLexicalPath(right), StringComparison.Ordinal);

    // A lexical boundary is not a filesystem boundary: an existing component may be a link.
    public static bool IsSameOrDescendantLexicalPath(string root, string path)
    {
        string normalizedRoot = NormalizeLexicalPath(root);
        string normalizedPath = NormalizeLexicalPath(path);
        if (string.Equals(normalizedRoot, normalizedPath, StringComparison.Ordinal))
            return true;

        string prefix = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(prefix, StringComparison.Ordinal);
    }

    // Resolve relative paths lexically; keep a trailing separator so directory URIs remain usable as bases.
    public static Uri ToFileUri(string path)
        => ToFileUri(Path.GetFullPath(path), OperatingSystem.IsWindows());

    // windows: the path has a drive, which stays, or a UNC server, which becomes the host. Other forms go to new Uri(path).
    internal static Uri ToFileUri(string path, bool windows)
    {
        var builder = new StringBuilder("file://");
        int start = 0;
        if (windows && path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && IsSeparator(path[2], windows))
        {
            builder.Append('/');
        }
        else if (windows && path.Length >= 3 && IsSeparator(path[0], windows) && IsSeparator(path[1], windows)
                 && !IsSeparator(path[2], windows) && path[2] is not ('?' or '.'))
        {
            int end = path.AsSpan(2).IndexOfAny('\\', '/');
            start = end < 0 ? path.Length : end + 2;
            builder.Append(path, 2, start - 2);
        }
        else if (windows || !path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal))
        {
            return new Uri(path);
        }

        Span<byte> utf8 = stackalloc byte[4];
        foreach (Rune rune in path.AsSpan(start).EnumerateRunes())
        {
            if (rune.IsAscii && IsSeparator((char)rune.Value, windows))
            {
                builder.Append('/');
            }
            else if (rune.IsAscii
                     && (char.IsAsciiLetterOrDigit((char)rune.Value) || UnescapedPathCharacters.Contains((char)rune.Value)))
            {
                builder.Append((char)rune.Value);
            }
            else
            {
                foreach (byte b in utf8[..rune.EncodeToUtf8(utf8)])
                {
                    builder.Append(CultureInfo.InvariantCulture, $"%{b:X2}");
                }
            }
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }

    private static bool IsSeparator(char c, bool windows) => c == '/' || (windows && c == '\\');
}
