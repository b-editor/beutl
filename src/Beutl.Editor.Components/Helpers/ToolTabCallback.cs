using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.Helpers;

internal static class ToolTabCallback
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(ToolTabCallback));
    private static readonly ToolContextHostToken s_unpublishedTools = new();

    public static async Task RunAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "A tool-tab UI operation failed.");
            NotificationService.ShowError(Strings.Error, MessageStrings.OperationFailed);
        }
    }

    public static Task OpenAsync(IEditorContext editor, IToolContext tool)
        => RunAsync(async () => await editor.OpenToolTabAsync(tool));

    public static Task CloseAsync(IEditorContext editor, IToolContext tool)
        => RunAsync(async () => await editor.CloseToolTabAsync(tool));

    public static Task OpenFromExtensionAsync(IEditorContext editor, ToolTabExtension extension)
        => RunAsync(async () =>
        {
            IToolContext? tool = null;
            bool handedOff = false;
            try
            {
                if (extension.TryCreateContext(editor, out tool) && tool is not null)
                {
                    handedOff = true;
                    await editor.OpenToolTabAsync(tool);
                }
            }
            finally
            {
                if (!handedOff && tool is not null && s_unpublishedTools.TryAcquireContext(tool, out var lease))
                {
                    try { await tool.DisposeAsync(); }
                    finally { lease.Dispose(); }
                }
            }
        });
}
