using System.Collections;
using System.Numerics;

using Avalonia.Controls;

using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.ProjectSystem;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.ViewModels;
using BeUtl.ViewModels.AnimationEditors;
using BeUtl.ViewModels.Editors;
using BeUtl.Views.AnimationEditors;
using BeUtl.Views.Editors;

namespace BeUtl.Services;

public static class PropertyEditorService
{
    private record struct Editor(Func<IWrappedProperty, Control?> CreateEditor, Func<IWrappedProperty, BaseEditorViewModel?> CreateViewModel);

    private record struct AnimationEditor(Func<object?, Control?> CreateEditor, Func<IAnimation, EditorViewModelDescription, ITimelineOptionsProvider, object?> CreateViewModel);

    private static readonly Dictionary<Type, Editor> s_editors = new()
    {
        { typeof(bool), new(_ => new BooleanEditor(), s => new BooleanEditorViewModel(s.ToTyped<bool>())) },
        { typeof(byte), new(_ => new NumberEditor<byte>(), s => new NumberEditorViewModel<byte>(s.ToTyped<byte>())) },
        { typeof(Color), new(_ => new ColorEditor(), s => new ColorEditorViewModel(s.ToTyped<Color>())) },
        { typeof(CornerRadius), new(_ => new CornerRadiusEditor(), s => new CornerRadiusEditorViewModel(s.ToTyped<CornerRadius>())) },
        { typeof(decimal), new(_ => new NumberEditor<decimal>(), s => new NumberEditorViewModel<decimal>(s.ToTyped<decimal>())) },
        { typeof(double), new(_ => new NumberEditor<double>(), s => new NumberEditorViewModel<double>(s.ToTyped<double>())) },
        { typeof(Enum), new(CreateEnumEditor, CreateEnumViewModel) },
        { typeof(FileInfo), new(_ => new FileInfoEditor(), s => new FileInfoEditorViewModel(s.ToTyped<FileInfo>())) },
        { typeof(float), new(_ => new NumberEditor<float>(), s => new NumberEditorViewModel<float>(s.ToTyped<float>())) },
        { typeof(FontFamily), new(_ => new FontFamilyEditor(), s => new FontFamilyEditorViewModel(s.ToTyped<FontFamily>())) },
        { typeof(short), new(_ => new NumberEditor<short>(), s => new NumberEditorViewModel<short>(s.ToTyped<short>())) },
        { typeof(int), new(_ => new NumberEditor<int>(), s => new NumberEditorViewModel<int>(s.ToTyped<int>())) },
        { typeof(long), new(_ => new NumberEditor<long>(), s => new NumberEditorViewModel<long>(s.ToTyped<long>())) },
        { typeof(PixelPoint), new(_ => new PixelPointEditor(), s => new PixelPointEditorViewModel(s.ToTyped<PixelPoint>())) },
        { typeof(PixelRect), new(_ => new PixelRectEditor(), s => new PixelRectEditorViewModel(s.ToTyped<PixelRect>())) },
        { typeof(PixelSize), new(_ => new PixelSizeEditor(), s => new PixelSizeEditorViewModel(s.ToTyped<PixelSize>())) },
        { typeof(Point), new(_ => new PointEditor(), s => new PointEditorViewModel(s.ToTyped<Point>())) },
        { typeof(Rect), new(_ => new RectEditor(), s => new RectEditorViewModel(s.ToTyped<Rect>())) },
        { typeof(sbyte), new(_ => new NumberEditor<sbyte>(), s => new NumberEditorViewModel<sbyte>(s.ToTyped<sbyte>())) },
        { typeof(string), new(_ => new StringEditor(), s => new StringEditorViewModel(s.ToTyped<string>())) },
        { typeof(Thickness), new(_ => new ThicknessEditor(), s => new ThicknessEditorViewModel(s.ToTyped<Thickness>())) },
        { typeof(Size), new(_ => new SizeEditor(), s => new SizeEditorViewModel(s.ToTyped<Size>())) },
        { typeof(ushort), new(_ => new NumberEditor<ushort>(), s => new NumberEditorViewModel<ushort>(s.ToTyped<ushort>())) },
        { typeof(uint), new(_ => new NumberEditor<uint>(), s => new NumberEditorViewModel<uint>(s.ToTyped<uint>())) },
        { typeof(ulong), new(_ => new NumberEditor<ulong>(), s => new NumberEditorViewModel<ulong>(s.ToTyped<ulong>())) },
        { typeof(Vector2), new(_ => new Vector2Editor(), s => new Vector2EditorViewModel(s.ToTyped<Vector2>())) },
        { typeof(Vector3), new(_ => new Vector3Editor(), s => new Vector3EditorViewModel(s.ToTyped<Vector3>())) },
        { typeof(Vector4), new(_ => new Vector4Editor(), s => new Vector4EditorViewModel(s.ToTyped<Vector4>())) },
        { typeof(Graphics.Vector), new(_ => new VectorEditor(), s => new VectorEditorViewModel(s.ToTyped<Graphics.Vector>())) },
        { typeof(RelativePoint), new(_ => new RelativePointEditor(), s => new RelativePointEditorViewModel(s.ToTyped<RelativePoint>())) },
        { typeof(IBrush), new(_ => new BrushEditor(), s => new BrushEditorViewModel(s)) },
        { typeof(GradientStops), new(_ => new GradientStopsEditor(), s => new GradientStopsEditorViewModel(s.ToTyped<GradientStops>())) },
        { typeof(IList<LayerOperation>), new(_ => new LayerOpsEditor(), s => new LayerOpsEditorViewModel(s)) },
        { typeof(IList), new(_ => new ListEditor(), s => new ListEditorViewModel(s)) },
        { typeof(ICoreObject), new(CreateNavigationButton, CreateNavigationButtonViewModel) },
    };

    // pixelrect, rect, thickness, vector3, vector4
    private static readonly Dictionary<Type, AnimationEditor> s_animationEditors = new()
    {
        { typeof(bool), new(_ => new BooleanAnimationEditor(), (a, desc, ops) => new AnimationEditorViewModel<bool>(a, desc, ops)) },
        { typeof(byte), new(_ => new NumberAnimationEditor<byte>(), (a, desc, ops) => new NumberAnimationEditorViewModel<byte>(a.ToTyped<byte>(), desc, ops)) },
        { typeof(Color), new(_ => new ColorAnimationEditor(), (a, desc, ops) => new ColorAnimationEditorViewModel(a.ToTyped<Color>(), desc, ops)) },
        { typeof(CornerRadius), new(_ => new CornerRadiusAnimationEditor(), (a, desc, ops) => new CornerRadiusAnimationEditorViewModel(a.ToTyped<CornerRadius>(), desc, ops)) },
        { typeof(decimal), new(_ => new NumberAnimationEditor<decimal>(), (a, desc, ops) => new NumberAnimationEditorViewModel<decimal>(a.ToTyped<decimal>(), desc, ops)) },
        { typeof(double), new(_ => new NumberAnimationEditor<double>(), (a, desc, ops) => new NumberAnimationEditorViewModel<double>(a.ToTyped<double>(), desc, ops)) },
        { typeof(float), new(_ => new NumberAnimationEditor<float>(), (a, desc, ops) => new NumberAnimationEditorViewModel<float>(a.ToTyped<float>(), desc, ops)) },
        { typeof(short), new(_ => new NumberAnimationEditor<short>(), (a, desc, ops) => new NumberAnimationEditorViewModel<short>(a.ToTyped<short>(), desc, ops)) },
        { typeof(int), new(_ => new NumberAnimationEditor<int>(), (a, desc, ops) => new NumberAnimationEditorViewModel<int>(a.ToTyped<int>(), desc, ops)) },
        { typeof(long), new(_ => new NumberAnimationEditor<long>(), (a, desc, ops) => new NumberAnimationEditorViewModel<long>(a.ToTyped<long>(), desc, ops)) },
        { typeof(PixelPoint), new(_ => new PixelPointAnimationEditor(), (a, desc, ops) => new PixelPointAnimationEditorViewModel(a.ToTyped<PixelPoint>(), desc, ops)) },
        { typeof(PixelSize), new(_ => new PixelSizeAnimationEditor(), (a, desc, ops) => new PixelSizeAnimationEditorViewModel(a.ToTyped<PixelSize>(), desc, ops)) },
        { typeof(Point), new(_ => new PointAnimationEditor(), (a, desc, ops) => new PointAnimationEditorViewModel(a.ToTyped<Point>(), desc, ops)) },
        { typeof(sbyte), new(_ => new NumberAnimationEditor<sbyte>(), (a, desc, ops) => new NumberAnimationEditorViewModel<sbyte>(a.ToTyped<sbyte>(), desc, ops)) },
        { typeof(Size), new(_ => new SizeAnimationEditor(), (a, desc, ops) => new SizeAnimationEditorViewModel(a.ToTyped<Size>(), desc, ops)) },
        { typeof(ushort), new(_ => new NumberAnimationEditor<ushort>(), (a, desc, ops) => new NumberAnimationEditorViewModel<ushort>(a.ToTyped<ushort>(), desc, ops)) },
        { typeof(uint), new(_ => new NumberAnimationEditor<uint>(), (a, desc, ops) => new NumberAnimationEditorViewModel<uint>(a.ToTyped<uint>(), desc, ops)) },
        { typeof(ulong), new(_ => new NumberAnimationEditor<ulong>(), (a, desc, ops) => new NumberAnimationEditorViewModel<ulong>(a.ToTyped<ulong>(), desc, ops)) },
        { typeof(Vector2), new(_ => new Vector2AnimationEditor(), (a, desc, ops) => new Vector2AnimationEditorViewModel(a.ToTyped<Vector2>(), desc, ops)) },
        { typeof(Graphics.Vector), new(_ => new VectorAnimationEditor(), (a, desc, ops) => new VectorAnimationEditorViewModel(a.ToTyped<Graphics.Vector>(), desc, ops)) },
    };

    public static IWrappedProperty<T> ToTyped<T>(this IWrappedProperty pi)
    {
        return (IWrappedProperty<T>)pi;
    }

    public static Animation<T> ToTyped<T>(this IAnimation animation)
    {
        return (Animation<T>)animation;
    }

    public static Control? CreateEditor(IWrappedProperty property)
    {
        if (s_editors.ContainsKey(property.AssociatedProperty.PropertyType))
        {
            Editor editor = s_editors[property.AssociatedProperty.PropertyType];
            return editor.CreateEditor(property);
        }

        foreach (KeyValuePair<Type, Editor> item in s_editors)
        {
            if (property.AssociatedProperty.PropertyType.IsAssignableTo(item.Key))
            {
                return item.Value.CreateEditor(property);
            }
        }

        return null;
    }

    public static BaseEditorViewModel? CreateEditorViewModel(IWrappedProperty property)
    {
        if (s_editors.ContainsKey(property.AssociatedProperty.PropertyType))
        {
            Editor editor = s_editors[property.AssociatedProperty.PropertyType];
            return editor.CreateViewModel(property);
        }

        foreach (KeyValuePair<Type, Editor> item in s_editors)
        {
            if (property.AssociatedProperty.PropertyType.IsAssignableTo(item.Key))
            {
                return item.Value.CreateViewModel(property);
            }
        }

        return null;
    }

    public static Control? CreateAnimationEditor(IWrappedProperty.IAnimatable property)
    {
        if (s_animationEditors.ContainsKey(property.AssociatedProperty.PropertyType))
        {
            AnimationEditor editor = s_animationEditors[property.AssociatedProperty.PropertyType];
            return editor.CreateEditor(null);
        }

        return null;
    }

    public static object? CreateAnimationEditorViewModel(EditorViewModelDescription desc, IAnimation animation, ITimelineOptionsProvider optionsProvider)
    {
        if (s_animationEditors.ContainsKey(desc.WrappedProperty.AssociatedProperty.PropertyType))
        {
            AnimationEditor editor = s_animationEditors[desc.WrappedProperty.AssociatedProperty.PropertyType];
            return editor.CreateViewModel(animation, desc, optionsProvider);
        }

        return null;
    }

    private static Control? CreateEnumEditor(IWrappedProperty s)
    {
        Type type = typeof(EnumEditor<>).MakeGenericType(s.AssociatedProperty.PropertyType);
        return (Control?)Activator.CreateInstance(type);
    }

    private static BaseEditorViewModel? CreateEnumViewModel(IWrappedProperty s)
    {
        Type type = typeof(EnumEditorViewModel<>).MakeGenericType(s.AssociatedProperty.PropertyType);
        return Activator.CreateInstance(type, s) as BaseEditorViewModel;
    }

    private static Control? CreateNavigationButton(IWrappedProperty s)
    {
        Type controlType = typeof(NavigateButton<>);
        controlType = controlType.MakeGenericType(s.AssociatedProperty.PropertyType);
        return Activator.CreateInstance(controlType) as Control;
    }

    private static BaseEditorViewModel? CreateNavigationButtonViewModel(IWrappedProperty s)
    {
        Type viewModelType = typeof(NavigationButtonViewModel<>);
        viewModelType = viewModelType.MakeGenericType(s.AssociatedProperty.PropertyType);
        return Activator.CreateInstance(viewModelType, s) as BaseEditorViewModel;
    }
}
