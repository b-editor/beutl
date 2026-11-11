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
            return ReadPageScriptResult(await pageData.WaitAsync(PageDataTimeout, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    // WebView2 returns a script's value as JSON, so a string arrives quoted, while WebKit returns the string itself.
    // A value that is itself quoted cannot tell the two apart, so the extension's expression goes into an envelope
    // that always starts with a brace, and its string comes back as it was on every engine. The expression is the
    // argument of the function that builds the envelope, so it runs in the page's scope and sees the page's globals;
    // its own parentheses keep a comma expression one argument.
    internal static string WrapPageScript(string expression) =>
        "((value) => JSON.stringify({ value: typeof value === 'string' ? value : null }))((\n"
        + expression.TrimEnd().TrimEnd(';') + "\n))";

    internal static string? ReadPageScriptResult(string? result)
    {
        if (string.IsNullOrEmpty(result)) return null;
        try
        {
            string envelope = result[0] == '"' ? JsonSerializer.Deserialize<string>(result) ?? "" : result;
            using var document = JsonDocument.Parse(envelope);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("value", out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
