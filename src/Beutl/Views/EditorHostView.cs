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
                            {Message.NullWasSpecifiedForEditorContext}
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
                               {string.Format(Message.CouldNotOpenFollowingFileWithExtension, viewModel.Extension.DisplayName, viewModel.Object.Uri)}

                           Message:
                               {Message.EditorContextHasAlreadyBeenCreated}
                        """
            };
        }

        Content = control;
    }
}
