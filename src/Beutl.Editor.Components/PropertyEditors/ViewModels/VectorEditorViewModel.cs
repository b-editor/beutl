using System.Numerics;
using System.Runtime.CompilerServices;
using Avalonia;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.PropertyEditors;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// The editor of a value with two to four TElement components, such as a point or a rectangle, which a
// Vector2Editor, Vector3Editor or Vector4Editor edits; headers name the components, or the editor's theme does.
public sealed class VectorEditorViewModel<TValue, TElement, TEdit>(
    IPropertyAdapter<TValue> property,
    Func<TValue, TEdit> toEdit,
    Func<TEdit, TValue> fromEdit,
    params string[] headers)
    : PropertyEditorViewModel<TValue, TEdit>(property), IConfigureUniformEditor
    where TElement : INumber<TElement>
{
    public ReactivePropertySlim<bool> IsUniformEditorEnabled { get; } = new();

    protected override TEdit ToEdit(TValue value) => toEdit(value);

    protected override TValue FromEdit(TEdit edit) => fromEdit(edit);

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (Disposables.IsDisposed) return;

        if (visitor is VectorEditorBase vector)
            vector.HapticReferenceValues = GetDefaultValue() is TValue value && ToEdit(value) is ITuple tuple ? tuple : null;

        switch (visitor)
        {
            case Vector2Editor<TElement> editor:
                VectorEditorBindingHelper.ApplyNumberAttributes(editor, PropertyAdapter);
                if (headers.Length == 2)
                    (editor.FirstHeader, editor.SecondHeader) = (headers[0], headers[1]);
                BindUniform(editor, Vector2Editor.IsUniformProperty);
                break;

            case Vector3Editor<TElement> editor:
                VectorEditorBindingHelper.ApplyNumberAttributes(editor, PropertyAdapter);
                if (headers.Length == 3)
                    (editor.FirstHeader, editor.SecondHeader, editor.ThirdHeader) = (headers[0], headers[1], headers[2]);
                BindUniform(editor, Vector3Editor.IsUniformProperty);
                break;

            case Vector4Editor<TElement> editor:
                VectorEditorBindingHelper.ApplyNumberAttributes(editor, PropertyAdapter);
                if (headers.Length == 4)
                {
                    (editor.FirstHeader, editor.SecondHeader, editor.ThirdHeader, editor.FourthHeader) =
                        (headers[0], headers[1], headers[2], headers[3]);
                }

                BindUniform(editor, Vector4Editor.IsUniformProperty);
                break;
        }
    }

    private void BindUniform(VectorEditorBase editor, AvaloniaProperty<bool> property)
    {
        editor.Bind(property, IsUniformEditorEnabled.ToBinding())
            .DisposeWith(Disposables);
    }
}

// The vector editors of the built-in value types.
internal static class VectorEditors
{
    public static VectorEditorViewModel<Media.PixelPoint, int, (int, int)> PixelPoint(IPropertyAdapter<Media.PixelPoint> p)
        => new(p, v => (v.X, v.Y), e => new(e.Item1, e.Item2), "X", "Y");

    public static VectorEditorViewModel<Media.PixelSize, int, (int, int)> PixelSize(IPropertyAdapter<Media.PixelSize> p)
        => new(p, v => (v.Width, v.Height), e => new(e.Item1, e.Item2), Strings.Width, Strings.Height);

    public static VectorEditorViewModel<Graphics.Point, float, (float, float)> Point(IPropertyAdapter<Graphics.Point> p)
        => new(p, v => (v.X, v.Y), e => new(e.Item1, e.Item2), "X", "Y");

    public static VectorEditorViewModel<Graphics.Size, float, (float, float)> Size(IPropertyAdapter<Graphics.Size> p)
        => new(p, v => (v.Width, v.Height), e => new(e.Item1, e.Item2), Strings.Width, Strings.Height);

    public static VectorEditorViewModel<Graphics.Vector, float, (float, float)> Vector(IPropertyAdapter<Graphics.Vector> p)
        => new(p, v => (v.X, v.Y), e => new(e.Item1, e.Item2), "X", "Y");

    public static VectorEditorViewModel<Vector2, float, (float, float)> Vector2(IPropertyAdapter<Vector2> p)
        => new(p, v => (v.X, v.Y), e => new(e.Item1, e.Item2), "X", "Y");

    public static VectorEditorViewModel<Vector3, float, (float, float, float)> Vector3(IPropertyAdapter<Vector3> p)
        => new(p, v => (v.X, v.Y, v.Z), e => new(e.Item1, e.Item2, e.Item3), "X", "Y", "Z");

    public static VectorEditorViewModel<Media.PixelRect, int, (int, int, int, int)> PixelRect(IPropertyAdapter<Media.PixelRect> p)
        => new(p, v => (v.X, v.Y, v.Width, v.Height), e => new(e.Item1, e.Item2, e.Item3, e.Item4),
            "X", "Y", Strings.Width, Strings.Height);

    public static VectorEditorViewModel<Graphics.Rect, float, (float, float, float, float)> Rect(IPropertyAdapter<Graphics.Rect> p)
        => new(p, v => (v.X, v.Y, v.Width, v.Height), e => new(e.Item1, e.Item2, e.Item3, e.Item4),
            "X", "Y", Strings.Width, Strings.Height);

    public static VectorEditorViewModel<Vector4, float, (float, float, float, float)> Vector4(IPropertyAdapter<Vector4> p)
        => new(p, v => (v.X, v.Y, v.Z, v.W), e => new(e.Item1, e.Item2, e.Item3, e.Item4), "X", "Y", "Z", "W");

    public static VectorEditorViewModel<Graphics.Thickness, float, (float, float, float, float)> Thickness(IPropertyAdapter<Graphics.Thickness> p)
        => new(p, v => (v.Left, v.Top, v.Right, v.Bottom), e => new(e.Item1, e.Item2, e.Item3, e.Item4),
            Strings.Left, Strings.Top, Strings.Right, Strings.Bottom);

    // Its theme names the corners.
    public static VectorEditorViewModel<Media.CornerRadius, float, (float, float, float, float)> CornerRadius(IPropertyAdapter<Media.CornerRadius> p)
        => new(p, v => (v.TopLeft, v.TopRight, v.BottomRight, v.BottomLeft), e => new(e.Item1, e.Item2, e.Item3, e.Item4));
}
