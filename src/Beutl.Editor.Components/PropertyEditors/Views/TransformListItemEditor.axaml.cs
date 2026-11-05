using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Threading;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public partial class TransformListItemEditor : PresenterListItemEditor
{
    public TransformListItemEditor()
    {
        Resources["TransformTypeToIconConverter"] = TransformTypeToIconConverter.Instance;
        InitializeComponent();
        // Opening and closing swap the first row and the row with the name, so the content shows at once: a fade
        // would leave the rows under the row with the name while they fade out.
        FallbackObjectViewHelper.Attach(this, view => content.Children.Add(view));
        AttachHandle(
            inlineHandle.GetObservable(IsVisibleProperty).Select(inline => inline ? (Control)inlineHandle : reorderHandle),
            presenterEditor);
        HandOverFocus(inlineHandle, reorderHandle);
        HandOverFocus(reorderHandle, inlineHandle);
        EditorMenuHelper.AttachCopyPasteAndTemplateMenus(this, (FAMenuFlyout)ContextFlyout!);
    }

    // Opening or closing hides the focused handle, which leaves the focus nowhere, and shows the other one. That one takes
    // the focus the same way, so a key keeps toggling the transform.
    private static void HandOverFocus(ToggleButton from, ToggleButton to)
    {
        NavigationMethod method = NavigationMethod.Unspecified;
        from.GotFocus += (_, e) => method = e.NavigationMethod;
        from.LostFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!from.IsEffectivelyVisible && to.IsEffectivelyVisible
                && TopLevel.GetTopLevel(to)?.FocusManager?.GetFocusedElement() == null)
            {
                to.Focus(method);
            }
        });
    }

    private sealed class TransformTypeToIconConverter : IValueConverter
    {
        public static readonly TransformTypeToIconConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string? key = value is KnownTransformType type ? TransformEditorViewModel.Find(type)?.IconKey : null;
            return Application.Current!.FindResource(key ?? "TransformListItemEditor_ReOrderIcon");
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
