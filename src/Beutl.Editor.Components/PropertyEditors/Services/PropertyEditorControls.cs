using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.PropertyEditors;
using Beutl.PropertyAdapters;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.Services;

// Prepares the control of a property editor: binds its header and state, lets the context set it up, and connects its value.
internal static class PropertyEditorControls
{
    // For a control the host did not make for the context, such as one a view declares.
    public static void Attach(IPropertyEditorContext context, Control control)
    {
        if (control is PropertyEditor editor && context is PropertyEditorViewModel viewModel)
            BindState(editor, viewModel);

        if (control is IPropertyEditorContextVisitor visitor)
            context.Accept(visitor);

        (context as PropertyEditorViewModel)?.TryConnect(control);
    }

    private static void BindState(PropertyEditor editor, PropertyEditorViewModel viewModel)
    {
        editor[!PropertyEditor.IsReadOnlyProperty] = viewModel.CanEdit.Inverse().ToBinding();
        editor.Header = viewModel.Header;
        editor[!PropertyEditor.DescriptionProperty] = viewModel.Description.ToBinding();
        editor[!PropertyEditor.HoverInfoProperty] = viewModel.HoverInfo.ToBinding();
        if (viewModel.PropertyAdapter is IAnimatablePropertyAdapter)
        {
            editor[!PropertyEditor.KeyFrameCountProperty] = viewModel.KeyFrameCount.ToBinding();
            editor[!PropertyEditor.KeyFrameIndexProperty] = viewModel.KeyFrameIndex.ToPropertyBinding(BindingMode.TwoWay);
        }
    }
}
