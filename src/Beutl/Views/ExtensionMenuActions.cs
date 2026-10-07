using Beutl.Services;
using Beutl.ViewModels;

namespace Beutl.Views;

// The actions behind the extension entries of the main menu, shared by the in-window menu and the native menu.
internal static class ExtensionMenuActions
{
    public static async Task SwitchEditorAsync(
        MainViewModel viewModel, EditorTabItem selectedTab, EditorExtension editorExtension)
    {
        using var write = await viewModel.EditorService.BeginProjectFileWriteAsync(CancellationToken.None);
        if (!viewModel.EditorService.TabItems.Contains(selectedTab)) return;
        IEditorContext original = selectedTab.Context.Value;
        using var suspension = viewModel.EditorService.SuspendEditor(original);
        if (original is ISavableEditorContext editor
            && !await editor.SaveAsync())
        {
            NotificationService.ShowError(MessageStrings.UnableToSaveFile, selectedTab.FileName.Value);
            return;
        }

        if (!ReferenceEquals(selectedTab.Context.Value, original)) return;

        if (editorExtension.TryCreateContext(
                selectedTab.Context.Value.Object,
                new EditorContextServices(viewModel.EditorService, viewModel.ExtensionProvider),
                out IEditorContext? context))
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
