using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

using Avalonia.Controls;

using BeUtl.Framework;
using BeUtl.Framework.Services;
using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.Media;
using BeUtl.ViewModels.Editors;
using BeUtl.Views.Editors;

namespace BeUtl.Services;

public static class PropertyEditorService
{
    public static IAbstractProperty<T> ToTyped<T>(this IAbstractProperty pi)
    {
        return (IAbstractProperty<T>)pi;
    }

    public static (CoreProperty[] Properties, PropertyEditorExtension Extension) MatchProperty(IReadOnlyList<CoreProperty> properties)
    {
        ExtensionProvider extp = PackageManager.Instance.ExtensionProvider;

        foreach (PropertyEditorExtension item in extp.GetExtensions<PropertyEditorExtension>().AsSpan())
        {
            CoreProperty[] array = item.MatchProperty(properties).ToArray();
            if (array.Length > 0)
            {
                return (array, item);
            }
        }

        return default;
    }

    private static Control? CreateEnumEditor(IAbstractProperty s)
    {
        Type type = typeof(EnumEditor<>).MakeGenericType(s.Property.PropertyType);
        return (Control?)Activator.CreateInstance(type);
    }

    private static BaseEditorViewModel? CreateEnumViewModel(IAbstractProperty s)
    {
        Type type = typeof(EnumEditorViewModel<>).MakeGenericType(s.Property.PropertyType);
        return Activator.CreateInstance(type, s) as BaseEditorViewModel;
    }

    private static Control? CreateNavigationButton(IAbstractProperty s)
    {
        Type controlType = typeof(NavigateButton<>);
        controlType = controlType.MakeGenericType(s.Property.PropertyType);
        return Activator.CreateInstance(controlType) as Control;
    }

    private static BaseEditorViewModel? CreateNavigationButtonViewModel(IAbstractProperty s)
    {
        Type viewModelType = typeof(NavigationButtonViewModel<>);
        viewModelType = viewModelType.MakeGenericType(s.Property.PropertyType);
        return Activator.CreateInstance(viewModelType, s) as BaseEditorViewModel;
    }

    internal sealed class PropertyEditorExtensionImpl : IPropertyEditorExtensionImpl
    {
        private record struct Editor(Func<IAbstractProperty, Control?> CreateEditor, Func<IAbstractProperty, BaseEditorViewModel?> CreateViewModel);

        private static readonly Dictionary<int, Editor> s_editorsOverride = new()
        {
            { Brush.OpacityProperty.Id, new(_ => new OpacityEditor(), s => new OpacityEditorViewModel(s.ToTyped<float>())) },
            { ScaleTransform.ScaleProperty.Id, new(_ => new PercentageEditor(), s => new PercentageEditorViewModel(s.ToTyped<float>())) },
            { ScaleTransform.ScaleXProperty.Id, new(_ => new PercentageEditor(), s => new PercentageEditorViewModel(s.ToTyped<float>())) },
            { ScaleTransform.ScaleYProperty.Id, new(_ => new PercentageEditor(), s => new PercentageEditorViewModel(s.ToTyped<float>())) },
        };

        // IList<StreamOperator>
        private static readonly Dictionary<Type, Editor> s_editors = new()
        {
            { typeof(AlignmentX), new(_ => new AlignmentXEditor(), s => new AlignmentXEditorViewModel(s.ToTyped<AlignmentX>())) },
            { typeof(AlignmentY), new(_ => new AlignmentYEditor(), s => new AlignmentYEditorViewModel(s.ToTyped<AlignmentY>())) },
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

        public IEnumerable<CoreProperty> MatchProperty(IReadOnlyList<CoreProperty> properties)
        {
            for (int i = 0; i < properties.Count; i++)
            {
                CoreProperty item = properties[i];
                if (s_editorsOverride.ContainsKey(item.Id) || s_editors.ContainsKey(item.PropertyType))
                {
                    yield return item;
                    yield break;
                }
                else
                {
                    foreach (KeyValuePair<Type, Editor> pair in s_editors)
                    {
                        if (item.PropertyType.IsAssignableTo(pair.Key))
                        {
                            yield return item;
                            yield break;
                        }
                    }
                }
            }
        }

        public bool TryCreateContext(PropertyEditorExtension extension, IReadOnlyList<IAbstractProperty> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
        {
            if (properties.Count > 0 && properties[0] is { } property)
            {
                if (s_editorsOverride.TryGetValue(property.Property.Id, out Editor editorOverrided))
                {
                    context = editorOverrided.CreateViewModel(property);
                    if (context != null)
                    {
                        context.Extension = extension;
                        return true;
                    }
                }

                if (s_editors.TryGetValue(property.Property.PropertyType, out Editor editor))
                {
                    context = editor.CreateViewModel(property);
                    if (context != null)
                    {
                        context.Extension = extension;
                        return true;
                    }
                }

                foreach (KeyValuePair<Type, Editor> item in s_editors)
                {
                    if (property.Property.PropertyType.IsAssignableTo(item.Key))
                    {
                        context = item.Value.CreateViewModel(property);
                        if (context != null)
                        {
                            context.Extension = extension;
                            return true;
                        }
                    }
                }
            }

            context = null;
            return false;
        }

        public bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out IControl? control)
        {
            if (context is BaseEditorViewModel { WrappedProperty: { } property })
            {
                if (s_editorsOverride.TryGetValue(property.Property.Id, out Editor editorOverrided))
                {
                    control = editorOverrided.CreateEditor(property);
                    if (control != null)
                    {
                        return true;
                    }
                }

                if (s_editors.TryGetValue(property.Property.PropertyType, out Editor editor))
                {
                    control = editor.CreateEditor(property);
                    if (control != null)
                    {
                        return true;
                    }
                }

                foreach (KeyValuePair<Type, Editor> item in s_editors)
                {
                    if (property.Property.PropertyType.IsAssignableTo(item.Key))
                    {
                        control = item.Value.CreateEditor(property);
                        if (control != null)
                        {
                            return true;
                        }
                    }
                }
            }

            control = null;
            return false;
        }
    }
}
