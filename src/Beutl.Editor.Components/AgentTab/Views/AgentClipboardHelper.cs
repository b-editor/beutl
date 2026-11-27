using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Beutl.Utilities;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.AgentTab.Views;

internal static class AgentClipboardHelper
{
    public static Task<bool> TryCopyTextAsync(Visual source, string? text, ILogger logger, string failureMessage)
        => TryCopyTextAsync(TopLevel.GetTopLevel(source), text, logger, failureMessage);

    public static Task<bool> TryCopyTextAsync(TopLevel? topLevel, string? text, ILogger logger, string failureMessage)
        => TryCopyAsync(topLevel, clipboard => clipboard.SetTextAsync(text), logger, failureMessage);

    public static Task<bool> TryCopyAsync(Visual source, Func<IClipboard, Task> copy, ILogger logger, string failureMessage)
        => TryCopyAsync(TopLevel.GetTopLevel(source), copy, logger, failureMessage);

    public static async Task<bool> TryCopyAsync(TopLevel? topLevel, Func<IClipboard, Task> copy, ILogger logger, string failureMessage)
    {
        if (topLevel?.Clipboard is not { } clipboard)
            return false;
        try
        {
            await copy(clipboard);
            return true;
        }
        catch (Exception ex) when (!ExceptionHelpers.ContainsFatalFailure(ex))
        {
            logger.LogWarning(ex, "{Message}", failureMessage);
            return false;
        }
    }
}
