using System.Runtime.InteropServices;
using System.Text;

using Beutl.Logging;

using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.WebBrowserTab;

/// <summary>
/// The mark an operating system reads to know a file came from the internet. It is what SmartScreen and Office's
/// Protected View go by on Windows, for the contents of a ZIP too, and Gatekeeper on macOS. Linux has no such mark.
/// </summary>
internal static partial class BrowserDownloadProvenance
{
    private const string ZoneIdentifierStream = ":Zone.Identifier";
    private const string QuarantineAttribute = "com.apple.quarantine";
    private const int CreateOnly = 2; // XATTR_CREATE

    private static readonly ILogger s_logger = Log.CreateLogger(typeof(BrowserDownloadProvenance));

    /// <summary>
    /// Marks a finished file, before it is published so it never appears without the mark. A mark the browser
    /// engine already wrote stays. A volume that cannot hold one, such as FAT, is no reason to fail the download.
    /// </summary>
    internal static void Mark(string path, Uri? source, Uri? referrer = null)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                WriteZoneIdentifier(path, source, referrer);
            else if (OperatingSystem.IsMacOS())
                SetQuarantine(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            s_logger.LogDebug(ex, "A saved file was left without its download mark.");
        }
    }

    // Zone 3 is the Internet. "about:internet" is what browsers write for a file with no address of its own.
    internal static string FormatZoneIdentifier(Uri? source, Uri? referrer)
    {
        var builder = new StringBuilder("[ZoneTransfer]\r\nZoneId=3\r\n");
        if (ToHttpAddress(referrer) is { } referrerAddress)
            builder.Append("ReferrerUrl=").Append(referrerAddress).Append("\r\n");
        builder.Append("HostUrl=").Append(ToHttpAddress(source) ?? "about:internet").Append("\r\n");
        return builder.ToString();
    }

    // Flags, the time in hexadecimal, the program that saved the file, and an event id only LaunchServices assigns.
    internal static string FormatQuarantine(DateTimeOffset time) => $"0081;{time.ToUnixTimeSeconds():x8};Beutl;";

    // ASCII, without what only matters inside the page. An address that carries credentials is not an HTTP address here.
    private static string? ToHttpAddress(Uri? uri) =>
        uri != null && BrowserMediaDownload.IsHttpUri(uri)
            ? new UriBuilder(uri) { Host = uri.IdnHost, Fragment = "" }.Uri.AbsoluteUri
            : null;

    private static void WriteZoneIdentifier(string path, Uri? source, Uri? referrer)
    {
        using var stream = new FileStream(path + ZoneIdentifierStream, FileMode.CreateNew, FileAccess.Write);
        stream.Write(Encoding.ASCII.GetBytes(FormatZoneIdentifier(source, referrer)));
    }

    private static void SetQuarantine(string path)
    {
        byte[] value = Encoding.ASCII.GetBytes(FormatQuarantine(DateTimeOffset.UtcNow));
        if (SetExtendedAttribute(path, QuarantineAttribute, value, (nuint)value.Length, 0, CreateOnly) != 0)
            s_logger.LogDebug("A saved file was left without its download mark: errno {Error}.", Marshal.GetLastPInvokeError());
    }

    [LibraryImport("libSystem.B.dylib", EntryPoint = "setxattr", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int SetExtendedAttribute(string path, string name, byte[] value, nuint size, uint position, int options);
}
