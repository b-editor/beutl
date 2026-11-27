using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.PropertyEditors;

namespace Beutl.Editor.Components.PropertyEditors.Views;

// Lies over a property row while a link target is picked: blocks the row's own editing, highlights it when its
// property can be the target and chooses it on a click. The right inset leaves the row's menu usable.
internal sealed class PropertyLinkSelectionOverlay : Border
{
    private const double MenuInset = 28;

    private readonly PropertyEditorViewModel _editor;
    private readonly IPropertyLinkSelection _selection;
    private readonly Border _highlight;
    private IDisposable? _subscription;

    public PropertyLinkSelectionOverlay(PropertyEditorViewModel editor, IPropertyLinkSelection selection)
    {
        _editor = editor;
        _selection = selection;
        Background = Brushes.Transparent;
        Margin = new Thickness(0, 0, MenuInset, 0);
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;

        _highlight = new Border
        {
            IsHitTestVisible = false,
            IsVisible = false,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(2, 1),
            [!Border.BackgroundProperty] = new DynamicResourceExtension("SubtleFillColorSecondaryBrush")
        };
        Child = _highlight;

        UpdateState();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _subscription ??= _selection.IsSelectingLinkTarget.Subscribe(_ => UpdateState());
        UpdateState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _subscription?.Dispose();
        _subscription = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && !IsVisible)
        {
            _highlight.IsVisible = false;
            Cursor = Cursors.Arrow;
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        bool candidate = _selection.CanLinkTo(_editor);
        Cursor = candidate ? Cursors.Hand : Cursors.Arrow;
        _highlight.IsVisible = candidate;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        Cursor = Cursors.Arrow;
        _highlight.IsVisible = false;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // The overlay takes every press, so the row below it cannot start an edit.
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Handled = true;
        if (e.InitialPressMouseButton != MouseButton.Left)
            return;

        // A click on a row that cannot be the target changes nothing, and one that can ends the selection.
        if (_selection.CanLinkTo(_editor))
            _selection.TryChooseLinkTarget(_editor);
    }

    private void UpdateState()
    {
        IsVisible = _selection.IsSelectingLinkTarget.Value;
        if (!IsVisible)
        {
            _highlight.IsVisible = false;
            Cursor = Cursors.Arrow;
        }
    }

    internal bool IsCandidate => _selection.CanLinkTo(_editor);

    internal bool IsHighlighted => _highlight.IsVisible;
}

// The row of a property editor with the overlay that picks it as a link target.
internal sealed class PropertyLinkSelectionRow : Panel
{
    public PropertyLinkSelectionRow(
        PropertyEditor editor, PropertyEditorViewModel viewModel, IPropertyLinkSelection selection)
    {
        Editor = editor;
        Children.Add(editor);
        Children.Add(new PropertyLinkSelectionOverlay(viewModel, selection));
    }

    public PropertyEditor Editor { get; }
}
