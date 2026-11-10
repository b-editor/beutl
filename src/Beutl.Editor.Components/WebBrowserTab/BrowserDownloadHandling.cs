using System.Text.Json;

namespace Beutl.Editor.Components.WebBrowserTab;

/// <summary>
/// An offer a <see cref="BrowserDownloadExtension"/> took over, with what its page script found in the page when the
/// page offered the file. The extension is named by its id, so the offer keeps no package alive while it waits.
/// </summary>
internal sealed class BrowserDownloadHandling(
    ExtensionId extension, BrowserDownloadOffer offer, string? description, Task<string?> pageData)
{
    // The script ran when the page offered the file, long before the user confirms it. A page that never answers
    // must not hold the import up.
    internal static readonly TimeSpan PageDataTimeout = TimeSpan.FromSeconds(5);

    internal ExtensionId Extension { get; } = extension;

    internal BrowserDownloadOffer Offer { get; } = offer;

    internal string? Description { get; } = description;

    internal async Task<string?> ReadPageDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            return NormalizeScriptResult(await pageData.WaitAsync(PageDataTimeout, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    // WebView2 returns a script's value as JSON, so a string arrives quoted; WebKit returns the string itself.
    internal static string? NormalizeScriptResult(string? result)
    {
        if (string.IsNullOrEmpty(result) || result is "null" or "undefined") return null;
        if (result.Length >= 2 && result[0] == '"' && result[^1] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(result);
            }
            catch (JsonException)
            {
                // A WebKit string can itself start and end with quotation marks.
            }
        }

        return result;
    }
}
