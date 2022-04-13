using System.Numerics;

using Avalonia.Controls;

using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.ProjectSystem;
using BeUtl.ViewModels.AnimationEditors;
using BeUtl.ViewModels.Editors;
using BeUtl.Views.AnimationEditors;
using BeUtl.Views.Editors;

namespace BeUtl.Services;

public static class PropertyEditorService
{
    private record struct Editor(Func<IPropertyInstance, Control?> CreateEditor, Func<IPropertyInstance, BaseEditorViewModel?> CreateViewModel);

    private record struct AnimationEditor(Func<object?, Control?> CreateEditor, Func<IAnimation, EditorViewModelDescription, object?> CreateViewModel);

    private static readonly Dictionary<Type, Editor> s_editors = new()
    {
        { typeof(bool), new(_ => new BooleanEditor(), s => new BooleanEditorViewModel((PropertyInstance<bool>)s)) },
        { typeof(byte), new(_ => new NumberEditor<byte>(), s => new ByteEditorViewModel((PropertyInstance<byte>)s)) },
        { typeof(Color), new(_ => new ColorEditor(), s => new ColorEditorViewModel((PropertyInstance<Color>)s)) },
        { typeof(CornerRadius), new(_ => new CornerRadiusEditor(), s => new CornerRadiusEditorViewModel((PropertyInstance<CornerRadius>)s)) },
        { typeof(decimal), new(_ => new NumberEditor<decimal>(), s => new DecimalEditorViewModel((PropertyInstance<decimal>)s)) },
        { typeof(double), new(_ => new NumberEditor<double>(), s => new DoubleEditorViewModel((PropertyInstance<double>)s)) },
        { typeof(Enum), new(CreateEnumEditor, CreateEnumViewModel) },
        { typeof(FileInfo), new(_ => new FileInfoEditor(), s => new FileInfoEditorViewModel((PropertyInstance<FileInfo>)s)) },
        { typeof(float), new(_ => new NumberEditor<float>(), s => new FloatEditorViewModel((PropertyInstance<float>)s)) },
        { typeof(FontFamily), new(_ => new FontFamilyEditor(), s => new FontFamilyEditorViewModel((PropertyInstance<FontFamily>)s)) },
        { typeof(short), new(_ => new NumberEditor<short>(), s => new Int16EditorViewModel((PropertyInstance<short>)s)) },
        { typeof(int), new(_ => new NumberEditor<int>(), s => new Int32EditorViewModel((PropertyInstance<int>)s)) },
        { typeof(long), new(_ => new NumberEditor<long>(), s => new Int64EditorViewModel((PropertyInstance<long>)s)) },
        { typeof(PixelPoint), new(_ => new PixelPointEditor(), s => new PixelPointEditorViewModel((PropertyInstance<PixelPoint>)s)) },
        { typeof(PixelRect), new(_ => new PixelRectEditor(), s => new PixelRectEditorViewModel((PropertyInstance<PixelRect>)s)) },
        { typeof(PixelSize), new(_ => new PixelSizeEditor(), s => new PixelSizeEditorViewModel((PropertyInstance<PixelSize>)s)) },
        { typeof(Point), new(_ => new PointEditor(), s => new PointEditorViewModel((PropertyInstance<Point>)s)) },
        { typeof(Rect), new(_ => new RectEditor(), s => new RectEditorViewModel((PropertyInstance<Rect>)s)) },
        { typeof(sbyte), new(_ => new NumberEditor<sbyte>(), s => new SByteEditorViewModel((PropertyInstance<sbyte>)s)) },
        { typeof(string), new(_ => new StringEditor(), s => new StringEditorViewModel((PropertyInstance<string>)s)) },
        { typeof(Thickness), new(_ => new ThicknessEditor(), s => new ThicknessEditorViewModel((PropertyInstance<Thickness>)s)) },
        { typeof(Size), new(_ => new SizeEditor(), s => new SizeEditorViewModel((PropertyInstance<Size>)s)) },
        { typeof(ushort), new(_ => new NumberEditor<ushort>(), s => new UInt16EditorViewModel((PropertyInstance<ushort>)s)) },
        { typeof(uint), new(_ => new NumberEditor<uint>(), s => new UInt32EditorViewModel((PropertyInstance<uint>)s)) },
        { typeof(ulong), new(_ => new NumberEditor<ulong>(), s => new UInt64EditorViewModel((PropertyInstance<ulong>)s)) },
        { typeof(Vector2), new(_ => new Vector2Editor(), s => new Vector2EditorViewModel((PropertyInstance<Vector2>)s)) },
        { typeof(Vector3), new(_ => new Vector3Editor(), s => new Vector3EditorViewModel((PropertyInstance<Vector3>)s)) },
        { typeof(Vector4), new(_ => new Vector4Editor(), s => new Vector4EditorViewModel((PropertyInstance<Vector4>)s)) },
        { typeof(Graphics.Vector), new(_ => new VectorEditor(), s => new VectorEditorViewModel((PropertyInstance<Graphics.Vector>)s)) },
    };

    // pixelrect, rect, thickness, vector3, vector4
    private static readonly Dictionary<Type, AnimationEditor> s_animationEditors = new()
    {
        { typeof(bool), new(_ => new BooleanAnimationEditor(), (a, desc) => new AnimationEditorViewModel<bool>(a, desc)) },
        { typeof(byte), new(_ => new NumberAnimationEditor<byte>(), (a, desc) => new AnimationEditorViewModel<byte>(a, desc)) },
        { typeof(Color), new(_ => new ColorAnimationEditor(), (a, desc) => new ColorAnimationEditorViewModel((Animation<Color>)a, desc)) },
        { typeof(CornerRadius), new(_ => new CornerRadiusAnimationEditor(), (a, desc) => new CornerRadiusAnimationEditorViewModel((Animation<CornerRadius>)a, desc)) },
        { typeof(decimal), new(_ => new NumberAnimationEditor<decimal>(), (a, desc) => new AnimationEditorViewModel<decimal>(a, desc)) },
        { typeof(double), new(_ => new NumberAnimationEditor<double>(), (a, desc) => new AnimationEditorViewModel<double>(a, desc)) },
        { typeof(float), new(_ => new NumberAnimationEditor<float>(), (a, desc) => new AnimationEditorViewModel<float>(a, desc)) },
        { typeof(short), new(_ => new NumberAnimationEditor<short>(), (a, desc) => new AnimationEditorViewModel<short>(a, desc)) },
        { typeof(int), new(_ => new NumberAnimationEditor<int>(), (a, desc) => new AnimationEditorViewModel<int>(a, desc)) },
        { typeof(long), new(_ => new NumberAnimationEditor<long>(), (a, desc) => new AnimationEditorViewModel<long>(a, desc)) },
        { typeof(PixelPoint), new(_ => new PixelPointAnimationEditor(), (a, desc) => new PixelPointAnimationEditorViewModel((Animation<PixelPoint>)a, desc)) },
        { typeof(PixelSize), new(_ => new PixelSizeAnimationEditor(), (a, desc) => new PixelSizeAnimationEditorViewModel((Animation<PixelSize>)a, desc)) },
        { typeof(Point), new(_ => new PointAnimationEditor(), (a, desc) => new PointAnimationEditorViewModel((Animation<Point>)a, desc)) },
        { typeof(sbyte), new(_ => new NumberAnimationEditor<sbyte>(), (a, desc) => new AnimationEditorViewModel<sbyte>(a, desc)) },
        { typeof(Size), new(_ => new SizeAnimationEditor(), (a, desc) => new SizeAnimationEditorViewModel((Animation<Size>)a, desc)) },
        { typeof(ushort), new(_ => new NumberAnimationEditor<ushort>(), (a, desc) => new AnimationEditorViewModel<ushort>(a, desc)) },
        { typeof(uint), new(_ => new NumberAnimationEditor<uint>(), (a, desc) => new AnimationEditorViewModel<uint>(a, desc)) },
        { typeof(ulong), new(_ => new NumberAnimationEditor<ulong>(), (a, desc) => new AnimationEditorViewModel<ulong>(a, desc)) },
        { typeof(Vector2), new(_ => new Vector2AnimationEditor(), (a, desc) => new Vector2AnimationEditorViewModel((Animation<Vector2>)a, desc)) },
        { typeof(Graphics.Vector), new(_ => new VectorAnimationEditor(), (a, desc) => new VectorAnimationEditorViewModel((Animation<Graphics.Vector>)a, desc)) },
    };

    public static Control? CreateEditor(IPropertyInstance property)
    {
        if (s_editors.ContainsKey(property.Property.PropertyType))
        {
            Editor editor = s_editors[property.Property.PropertyType];
            return editor.CreateEditor(property);
        }

        foreach (KeyValuePair<Type, Editor> item in s_editors)
        {
            if (property.Property.PropertyType.IsAssignableTo(item.Key))
            {
                return item.Value.CreateEditor(property);
            }
        }

        return null;
    }

    public static BaseEditorViewModel? CreateEditorViewModel(IPropertyInstance property)
    {
        if (s_editors.ContainsKey(property.Property.PropertyType))
        {
            Editor editor = s_editors[property.Property.PropertyType];
            return editor.CreateViewModel(property);
        }

        foreach (KeyValuePair<Type, Editor> item in s_editors)
        {
            if (property.Property.PropertyType.IsAssignableTo(item.Key))
            {
                return item.Value.CreateViewModel(property);
            }
        }

        return null;
    }

    public static Control? CreateAnimationEditor(IAnimatablePropertyInstance setter)
    {
        if (s_animationEditors.ContainsKey(setter.Property.PropertyType))
        {
            AnimationEditor editor = s_animationEditors[setter.Property.PropertyType];
            return editor.CreateEditor(null);
        }

        return null;
    }

    public static object? CreateAnimationEditorViewModel(EditorViewModelDescription desc, IAnimation animation)
    {
        if (s_animationEditors.ContainsKey(desc.PropertyInstance.Property.PropertyType))
        {
            AnimationEditor editor = s_animationEditors[desc.PropertyInstance.Property.PropertyType];
            return editor.CreateViewModel(animation, desc);
        }

        return null;
    }

    private static Control? CreateEnumEditor(IPropertyInstance s)
    {
        Type type = typeof(EnumEditor<>).MakeGenericType(s.Property.PropertyType);
        return (Control?)Activator.CreateInstance(type);
    }

    private static BaseEditorViewModel? CreateEnumViewModel(IPropertyInstance s)
    {
        Type type = typeof(EnumEditorViewModel<>).MakeGenericType(s.Property.PropertyType);
        return Activator.CreateInstance(type, s) as BaseEditorViewModel;
    }
}
