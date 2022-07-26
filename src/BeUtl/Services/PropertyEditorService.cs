using System.Collections;
using System.Numerics;

using Avalonia.Controls;

using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.Media;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.ViewModels;
using BeUtl.ViewModels.AnimationEditors;
using BeUtl.ViewModels.Editors;
using BeUtl.Views.Editors;

namespace BeUtl.Services;

public static class PropertyEditorService
{
    private record struct Editor(Func<IWrappedProperty, Control?> CreateEditor, Func<IWrappedProperty, BaseEditorViewModel?> CreateViewModel);

    private record struct AnimationEditor(Func<object?, Control?> CreateEditor, Func<IAnimationSpan, EditorViewModelDescription, ITimelineOptionsProvider, object?> CreateViewModel);

    private static readonly Dictionary<int, Editor> s_editorsOverride = new()
    {
        { Brush.OpacityProperty.Id, new(_ => new OpacityEditor(), s => new OpacityEditorViewModel(s.ToTyped<float>())) }
    };

    // IList<StreamOperator>
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
        { typeof(IList), new(_ => new ListEditor(), s => new ListEditorViewModel(s)) },
        { typeof(ICoreObject), new(CreateNavigationButton, CreateNavigationButtonViewModel) },
    };

    public static IWrappedProperty<T> ToTyped<T>(this IWrappedProperty pi)
    {
        return (IWrappedProperty<T>)pi;
    }

    public static AnimationSpan<T> ToTyped<T>(this IAnimationSpan animation)
    {
        return (AnimationSpan<T>)animation;
    }

    public static Control? CreateEditor(IWrappedProperty property)
    {
        if (s_editorsOverride.TryGetValue(property.AssociatedProperty.Id, out Editor editorOverrided))
        {
            return editorOverrided.CreateEditor(property);
        }

        if (s_editors.TryGetValue(property.AssociatedProperty.PropertyType, out Editor editor))
        {
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
        if (s_editorsOverride.TryGetValue(property.AssociatedProperty.Id, out Editor editorOverrided))
        {
            return editorOverrided.CreateViewModel(property);
        }

        if (s_editors.TryGetValue(property.AssociatedProperty.PropertyType, out Editor editor))
        {
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

    public static AnimationEditorViewModel CreateAnimationEditorViewModel(
        EditorViewModelDescription desc,
        IAnimationSpan animation,
        ITimelineOptionsProvider optionsProvider)
    {
        return new AnimationEditorViewModel(animation, desc, optionsProvider);
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
