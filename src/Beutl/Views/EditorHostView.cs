using Avalonia.Controls;

namespace Beutl.Views;

public sealed class EditorHostView : ContentControl
{
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is null)
        {
            Content = new TextBlock
            {
                Text = $"""
                        Error:
                            {MessageStrings.EditorContextNull}
                        """
            };
            return;
        }

        Control? control = null;
        if (DataContext is IEditorContext viewModel)
        {
            if (viewModel.Extension.TryCreateEditor(viewModel.Object, out control))
            {
                var cm = App.GetContextCommandManager();
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
        }

        Content = control;
    }
}
