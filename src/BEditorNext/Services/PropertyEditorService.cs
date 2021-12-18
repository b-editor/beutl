using System.Numerics;

using Avalonia.Controls;

using BEditorNext.Animation;
using BEditorNext.Graphics;
using BEditorNext.Media;
using BEditorNext.ProjectSystem;
using BEditorNext.ViewModels.AnimationEditors;
using BEditorNext.ViewModels.Editors;
using BEditorNext.Views.AnimationEditors;
using BEditorNext.Views.Editors;

namespace BEditorNext.Services;

public static class PropertyEditorService
{
    private record struct Editor(Func<ISetter, Control?> CreateEditor, Func<ISetter, object?> CreateViewModel);

    private record struct AnimationEditor(Func<object?, Control?> CreateEditor, Func<IAnimation, BaseEditorViewModel, object?> CreateViewModel);

    // pixelrect, rect, thickness, vector3, vector4
    private static readonly Dictionary<Type, Editor> s_editors = new()
    {
        { typeof(bool), new(_ => new BooleanEditor(), s => new BooleanEditorViewModel((Setter<bool>)s)) },
        { typeof(byte), new(_ => new NumberEditor<byte>(), s => new ByteEditorViewModel((Setter<byte>)s)) },
        { typeof(Color), new(_ => new ColorEditor(), s => new ColorEditorViewModel((Setter<Color>)s)) },
        { typeof(decimal), new(_ => new NumberEditor<decimal>(), s => new DecimalEditorViewModel((Setter<decimal>)s)) },
        { typeof(double), new(_ => new NumberEditor<double>(), s => new DoubleEditorViewModel((Setter<double>)s)) },
        { typeof(float), new(_ => new NumberEditor<float>(), s => new FloatEditorViewModel((Setter<float>)s)) },
        { typeof(short), new(_ => new NumberEditor<short>(), s => new Int16EditorViewModel((Setter<short>)s)) },
        { typeof(int), new(_ => new NumberEditor<int>(), s => new Int32EditorViewModel((Setter<int>)s)) },
        { typeof(long), new(_ => new NumberEditor<long>(), s => new Int64EditorViewModel((Setter<long>)s)) },
        { typeof(PixelPoint), new(_ => new PixelPointEditor(), s => new PixelPointEditorViewModel((Setter<PixelPoint>)s)) },
        { typeof(PixelSize), new(_ => new PixelSizeEditor(), s => new PixelSizeEditorViewModel((Setter<PixelSize>)s)) },
        { typeof(Point), new(_ => new PointEditor(), s => new PointEditorViewModel((Setter<Point>)s)) },
        { typeof(sbyte), new(_ => new NumberEditor<sbyte>(), s => new SByteEditorViewModel((Setter<sbyte>)s)) },
        { typeof(Size), new(_ => new SizeEditor(), s => new SizeEditorViewModel((Setter<Size>)s)) },
        { typeof(ushort), new(_ => new NumberEditor<ushort>(), s => new UInt16EditorViewModel((Setter<ushort>)s)) },
        { typeof(uint), new(_ => new NumberEditor<uint>(), s => new UInt32EditorViewModel((Setter<uint>)s)) },
        { typeof(ulong), new(_ => new NumberEditor<ulong>(), s => new UInt64EditorViewModel((Setter<ulong>)s)) },
        { typeof(Vector2), new(_ => new Vector2Editor(), s => new Vector2EditorViewModel((Setter<Vector2>)s)) },
    };

    // pixelpoint, pixelrect, pixelsize, point, rect, size, thickness, vector2, vector3, vector4
    private static readonly Dictionary<Type, AnimationEditor> s_animationEditors = new()
    {
        { typeof(bool), new(_ => new BooleanAnimationEditor(), (a, vm) => new AnimationEditorViewModel<bool>(a, vm)) },
        { typeof(byte), new(_ => new NumberAnimationEditor<byte>(), (a, vm) => new AnimationEditorViewModel<byte>(a, vm)) },
        { typeof(Color), new(_ => new ColorAnimationEditor(), (a, vm) => new ColorAnimationEditorViewModel((Animation<Color>)a, (BaseEditorViewModel<Color>)vm)) },
        { typeof(decimal), new(_ => new NumberAnimationEditor<decimal>(), (a, vm) => new AnimationEditorViewModel<decimal>(a, vm)) },
        { typeof(double), new(_ => new NumberAnimationEditor<double>(), (a, vm) => new AnimationEditorViewModel<double>(a, vm)) },
        { typeof(float), new(_ => new NumberAnimationEditor<float>(), (a, vm) => new AnimationEditorViewModel<float>(a, vm)) },
        { typeof(short), new(_ => new NumberAnimationEditor<short>(), (a, vm) => new AnimationEditorViewModel<short>(a, vm)) },
        { typeof(int), new(_ => new NumberAnimationEditor<int>(), (a, vm) => new AnimationEditorViewModel<int>(a, vm)) },
        { typeof(long), new(_ => new NumberAnimationEditor<long>(), (a, vm) => new AnimationEditorViewModel<long>(a, vm)) },
        { typeof(sbyte), new(_ => new NumberAnimationEditor<sbyte>(), (a, vm) => new AnimationEditorViewModel<sbyte>(a, vm)) },
        { typeof(ushort), new(_ => new NumberAnimationEditor<ushort>(), (a, vm) => new AnimationEditorViewModel<ushort>(a, vm)) },
        { typeof(uint), new(_ => new NumberAnimationEditor<uint>(), (a, vm) => new AnimationEditorViewModel<uint>(a, vm)) },
        { typeof(ulong), new(_ => new NumberAnimationEditor<ulong>(), (a, vm) => new AnimationEditorViewModel<ulong>(a, vm)) },
    };

    public static Control? CreateEditor(ISetter setter)
    {
        if (s_editors.ContainsKey(setter.Property.PropertyType))
        {
            Editor editor = s_editors[setter.Property.PropertyType];
            Control? control = editor.CreateEditor(setter);

            if (control != null)
            {
                control.DataContext = editor.CreateViewModel(setter);
            }

            return control;
        }

        return null;
    }

    public static Control? CreateAnimationEditor(IAnimatableSetter setter)
    {
        if (s_animationEditors.ContainsKey(setter.Property.PropertyType))
        {
            AnimationEditor editor = s_animationEditors[setter.Property.PropertyType];
            return editor.CreateEditor(null);
        }

        return null;
    }

    public static object? CreateAnimationEditorViewModel(BaseEditorViewModel viewModel, IAnimation animation)
    {
        if (s_animationEditors.ContainsKey(viewModel.Setter.Property.PropertyType))
        {
            AnimationEditor editor = s_animationEditors[viewModel.Setter.Property.PropertyType];
            return editor.CreateViewModel(animation, viewModel);
        }

        return null;
    }
}
