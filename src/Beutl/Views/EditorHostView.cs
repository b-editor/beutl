using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.ViewModels;

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
            if (viewModel.Extension.TryCreateEditor(viewModel.EdittingFile, out control))
            {
                var cm = App.GetContextCommandManager();
                cm?.Attach(control, viewModel.Extension);

                control.DataContext = viewModel;
            }

            control ??= new TextBlock()
            {
                Text = $"""
                           Error:
                               {string.Format(Message.CouldNotOpenFollowingFileWithExtension, viewModel.Extension.DisplayName, Path.GetFileName(viewModel.EdittingFile))}

                           Message:
                               {Message.EditorContextHasAlreadyBeenCreated}
                        """
            };
        }

        Content = control;
    }
}
