using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.NodeGraphTab.ViewModels;
using Beutl.NodeGraph;

namespace Beutl.Editor.Components.NodeGraphTab.Views;

public partial class GraphNodeView
{
    private static readonly Cursor s_resizeCorner = new(StandardCursorType.BottomRightCorner);
    private DragSession? _resize;

    private void ApplySize(GraphNodeViewModel model, NodeEditorSize size)
    {
        Width = size.Width ?? model.EditorWidth;
        Classes.Set("custom-width", size.Width.HasValue);
        if (size.CodeEditorHeight is { } height && !Equals(Resources["NodeCodeEditorHeight"], height))
            Resources["NodeCodeEditorHeight"] = height;
        Classes.Set("custom-code-height", size.CodeEditorHeight.HasValue);
    }

    private IEnumerable<TextBox> VisibleCodeFields() => this.GetVisualDescendants().OfType<TextBox>()
        .Where(box => box is { IsEffectivelyVisible: true, Name: "PART_InnerTextBox", TemplatedParent: StringEditor editor }
                      && editor.Classes.Contains("code"));

    private (bool Width, bool Height) ResizeEdges(PointerEventArgs e)
    {
        // Port points straddle the edge and keep their own connect, rewire and disconnect gestures.
        if (e.Source is NodePortPoint || DataContext is not GraphNodeViewModel { CanEdit.Value: true } model)
            return default;

        Point point = e.GetPosition(this);
        const double grip = 7;
        return (point.X >= Bounds.Width - grip,
            point.Y >= Bounds.Height - grip && model.IsExpanded.Value && VisibleCodeFields().Any());
    }

    private void OnResizeHover(object? sender, PointerEventArgs e)
    {
        if (_resize != null || e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Cursor? cursor = ResizeEdges(e) switch
        {
            (true, true) => s_resizeCorner,
            (true, false) => Cursors.SizeWestEast,
            (false, true) => Cursors.SizeNorthSouth,
            _ => null
        };
        if (cursor == null) ClearValue(CursorProperty);
        else Cursor = cursor;
    }

    private void OnResizePressed(object? sender, PointerPressedEventArgs e)
    {
        if (_resize != null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || DataContext is not GraphNodeViewModel model || Parent is not Canvas canvas) return;
        var (horizontal, vertical) = ResizeEdges(e);
        if (!horizontal && !vertical) return;

        Point start = e.GetPosition(canvas);
        double width = Bounds.Width;
        // The pointer moves the bottom edge once even when several shader stages share its change in height.
        double[] fields = vertical ? [.. VisibleCodeFields().Select(box => box.Bounds.Height)] : [];
        double fieldHeight = vertical ? fields.Average() : 0;
        NodeEditorSize original = model.GraphNode.EditorSize.Value;
        NodeEditorSize preview = original;
        IPointer pointer = e.Pointer;
        handle.Focus();
        _resize = DragSession.Begin(model.EditorContext, this, CommandNames.ResizeNode,
            () => Finish(keep: true), () => Finish(keep: false));
        e.Handled = true;
        if (_resize == null) return;

        OnPressed();
        PointerMoved += Move;
        PointerReleased += Release;
        pointer.Capture(this);

        void Preview(PointerEventArgs args)
        {
            Vector delta = args.GetPosition(canvas) - start;
            preview = new NodeEditorSize(
                horizontal && delta.X != 0 ? width + delta.X : original.Width,
                vertical && delta.Y != 0 ? fieldHeight + delta.Y / fields.Length : original.CodeEditorHeight).Constrain();
            ApplySize(model, preview);
        }

        void Move(object? source, PointerEventArgs args)
        {
            if (args.Pointer != pointer) return;
            Preview(args);
            args.Handled = true;
        }

        void Release(object? source, PointerReleasedEventArgs args)
        {
            if (args.Pointer != pointer || args.InitialPressMouseButton != MouseButton.Left) return;
            Preview(args);
            _resize?.Confirm();
            args.Handled = true;
        }

        void Finish(bool keep)
        {
            PointerMoved -= Move;
            PointerReleased -= Release;
            if (keep && model.CanEdit.Value)
            {
                using (_resize!.Enter())
                    model.GraphNode.EditorSize.Value = preview;
            }
            ApplySize(model, model.GraphNode.EditorSize.Value);
            _resize = null;
            if (pointer.Captured == this) pointer.Capture(null);
            ClearValue(CursorProperty);
            OnReleased();
        }
    }
}
