using Avalonia.Controls;
using Beutl.Api.Services;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Views;

public sealed class EditorHostView : ContentControl
{
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not IEditorContext viewModel)
        {
            Content = null;
            return;
        }

        EditorExtension extension = viewModel.Extension;
        Control? control = ExtensionGuard.Call(extension,
            () => extension.TryCreateEditor(viewModel.Object, out Control? editor) ? editor : null);
        if (control is not null)
        {
            var cm = AppHelper.GetContextCommandManager?.Invoke();
            cm?.Attach(control, viewModel.Extension);

            control.DataContext = viewModel;
        }

        control ??= new TextBlock()
        {
            Text = $"""
                       Error:
                           {string.Format(MessageStrings.FailedToOpenFileWithExtension, viewModel.Extension.DisplayName, viewModel.Object.Uri)}

                       Message:
                           {MessageStrings.EditorContextAlreadyCreated}
                    """
        };

        Content = control;
    }
}
