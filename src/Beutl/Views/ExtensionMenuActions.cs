using Beutl.Api.Services;
using Beutl.Services;
using Beutl.ViewModels;

namespace Beutl.Views;

// The actions behind the extension entries of the main menu, shared by the in-window menu and the native menu.
internal static class ExtensionMenuActions
{
    public static void OpenToolTab(MainViewModel viewModel, ToolTabExtension extension)
    {
        if (viewModel.EditorService.SelectedTabItem.Value?.Context.Value is IEditorContext editorContext
            && ExtensionGuard.Call(extension, () => extension.TryCreateContext(editorContext, out IToolContext? created) ? created : null)
                is { } toolContext
            && !editorContext.OpenToolTab(toolContext))
        {
            toolContext.Dispose();
        }
    }

    public static async Task SwitchEditorAsync(
        MainViewModel viewModel, EditorTabItem selectedTab, EditorExtension editorExtension)
    {
        using var write = await viewModel.EditorService.BeginProjectFileWriteAsync(CancellationToken.None);
        if (!viewModel.EditorService.TabItems.Contains(selectedTab)) return;
        IEditorContext original = selectedTab.Context.Value;
        using var suspension = viewModel.EditorService.SuspendEditor(original);
        if (original is EditViewModel sceneEditor)
            await sceneEditor.FlushMediaFingerprintsAsync();
        if (original is ISavableEditorContext editor
            && !await editor.SaveAsync())
        {
            NotificationService.ShowError(MessageStrings.UnableToSaveFile, selectedTab.FileName.Value);
            return;
        }

        if (!ReferenceEquals(selectedTab.Context.Value, original)) return;

        if (ExtensionGuard.Call(editorExtension, () => editorExtension.TryCreateContext(
                selectedTab.Context.Value.Object,
                new EditorContextServices(viewModel.EditorService, viewModel.ExtensionProvider),
                out IEditorContext? created) ? created : null)
            is { } context)
        {
            selectedTab.Context.Value.Dispose();
            selectedTab.Context.Value = context;
        }
        else
        {
            NotificationService.ShowInformation(
                title: MessageStrings.ContextNotCreated,
                message: string.Format(
                    format: MessageStrings.FailedToOpenFileWithExtension,
                    arg0: editorExtension.DisplayName,
                    arg1: selectedTab.FileName.Value));
        }
    }
}
