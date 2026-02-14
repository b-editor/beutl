using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Styling;
using Beutl.Api.Services;
using Beutl.Audio.Effects;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D.Models;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;

namespace Beutl.Services;

public static class PropertyEditorService
{
    public static IPropertyAdapter<T> ToTyped<T>(this IPropertyAdapter pi)
    {
        return (IPropertyAdapter<T>)pi;
    }

    public static (IPropertyAdapter[]? Properties, PropertyEditorExtension? Extension) MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
    {
        PropertyEditorExtension[] items = ExtensionProvider.Current.GetExtensions<PropertyEditorExtension>();
        for (int i = items.Length - 1; i >= 0; i--)
        {
            PropertyEditorExtension item = items[i];
            IPropertyAdapter[] array = item.MatchProperty(properties).ToArray();
            if (array.Length > 0)
            {
                return (array, item);
            }
        }

        return default;
    }

    private static Control? CreateEnumEditor(IPropertyAdapter s)
    {
        Type type = typeof(EnumEditor<>).MakeGenericType(s.PropertyType);
        return (Control?)Activator.CreateInstance(type);
    }

    private static BaseEditorViewModel? CreateEnumViewModel(IPropertyAdapter s)
    {
        Type type = typeof(EnumEditorViewModel<>).MakeGenericType(s.PropertyType);
        return Activator.CreateInstance(type, s) as BaseEditorViewModel;
    }

    private static Control? CreateCoreObjectEditor(IPropertyAdapter s)
    {
        Type controlType = typeof(CoreObjectEditor<>);
        controlType = controlType.MakeGenericType(s.PropertyType);
        return Activator.CreateInstance(controlType) as Control;
    }

    private static IListItemEditor? CreateCoreObjectListItemEditor(IPropertyAdapter s)
    {
        Type controlType = typeof(CoreObjectListItemEditor<>);
        controlType = controlType.MakeGenericType(s.PropertyType);
        return Activator.CreateInstance(controlType) as IListItemEditor;
    }

    private static BaseEditorViewModel? CreateCoreObjectEditorViewModel(IPropertyAdapter s)
    {
        Type viewModelType = typeof(CoreObjectEditorViewModel<>);
        viewModelType = viewModelType.MakeGenericType(s.PropertyType);
        return Activator.CreateInstance(viewModelType, s) as BaseEditorViewModel;
    }

    private static Control? CreateParsableEditor(IPropertyAdapter s)
    {
        Type controlType = typeof(ParsableEditor<>);
        controlType = controlType.MakeGenericType(s.PropertyType);
        return Activator.CreateInstance(controlType) as Control;
    }

    private static BaseEditorViewModel? CreateParsableEditorViewModel(IPropertyAdapter s)
    {
        Type viewModelType = typeof(ParsableEditorViewModel<>);
        viewModelType = viewModelType.MakeGenericType(s.PropertyType);
        return Activator.CreateInstance(viewModelType, s) as BaseEditorViewModel;
    }

    private static Control? CreateListEditor(IPropertyAdapter s)
    {
        Type? itemtype = GetItemTypeFromListType(s.PropertyType);
        if (itemtype != null)
        {
            Type controlType = typeof(ListEditor<>);
            controlType = controlType.MakeGenericType(itemtype);
            return Activator.CreateInstance(controlType) as Control;
        }
        else
        {
            return null;
        }
    }

    private static BaseEditorViewModel? CreateListEditorViewModel(IPropertyAdapter s)
    {
        Type? itemtype = GetItemTypeFromListType(s.PropertyType);
        if (itemtype != null)
        {
            Type viewModelType = typeof(ListEditorViewModel<>);
            viewModelType = viewModelType.MakeGenericType(itemtype);
            return Activator.CreateInstance(viewModelType, s) as BaseEditorViewModel;
        }
        else
        {
            return null;
        }
    }

    private static Type? GetItemTypeFromListType(Type listType)
    {
        Type? interfaceType = Array.Find(listType.GetInterfaces(), x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IList<>));
        return interfaceType?.GenericTypeArguments?.FirstOrDefault();
    }

    private static BaseEditorViewModel? CreateChoiceViewModel(IPropertyAdapter s, Type providerType)
    {
        Type type = typeof(ProvidedChoiceEditorViewModel<,>).MakeGenericType(s.PropertyType, providerType);
        return Activator.CreateInstance(type, s) as BaseEditorViewModel;
    }

    internal sealed class PropertyEditorExtensionImpl : IPropertyEditorExtensionImpl
    {
        private record struct Editor(Func<IPropertyAdapter, Control?> CreateEditor, Func<IPropertyAdapter, BaseEditorViewModel?> CreateViewModel);

        private record struct ListItemEditor(Func<IPropertyAdapter, IListItemEditor?> CreateEditor, Func<IPropertyAdapter, BaseEditorViewModel?> CreateViewModel);

        private static readonly Dictionary<Type, ListItemEditor> s_listItemEditorsOverride = new()
        {
            { typeof(FilterEffect), new(_ => new FilterEffectListItemEditor(), s => new FilterEffectEditorViewModel(s.ToTyped<FilterEffect?>())) },
            { typeof(PathSegment), new(_ => new PathOperationListItemEditor(), s => new PathOperationEditorViewModel(s.ToTyped<PathSegment?>())) },
            { typeof(PathFigure), new(_ => new PathFigureListItemEditor(), s => new PathFigureEditorViewModel(s.ToTyped<PathFigure>())) },
            { typeof(AudioEffect), new(_ => new AudioEffectListItemEditor(), s => new AudioEffectEditorViewModel(s.ToTyped<AudioEffect?>())) },
            { typeof(Transform), new(_ => new TransformListItemEditor(), s => new TransformEditorViewModel(s.ToTyped<Transform?>())) },
            { typeof(CoreObject), new(CreateCoreObjectListItemEditor, CreateCoreObjectEditorViewModel) }
        };

        private static readonly Dictionary<int, Editor> s_editorsOverride =
        [
            // プロパティのIdから、プロパティエディタを作成
        ];

        // IList<StreamOperator>
        private static readonly FrozenDictionary<Type, Editor> s_editors = new KeyValuePair<Type, Editor>[]
        {
            // Number
            new(typeof(byte), new(_ => new NumberEditor<byte>(), s => new NumberEditorViewModel<byte>(s.ToTyped<byte>()))),
            new(typeof(decimal), new(_ => new NumberEditor<decimal>(), s => new NumberEditorViewModel<decimal>(s.ToTyped<decimal>()))),
            new(typeof(double), new(_ => new NumberEditor<double>(), s => new NumberEditorViewModel<double>(s.ToTyped<double>()))),
            new(typeof(float), new(_ => new NumberEditor<float>(), s => new NumberEditorViewModel<float>(s.ToTyped<float>()))),
            new(typeof(short), new(_ => new NumberEditor<short>(), s => new NumberEditorViewModel<short>(s.ToTyped<short>()))),
            new(typeof(int), new(_ => new NumberEditor<int>(), s => new NumberEditorViewModel<int>(s.ToTyped<int>()))),
            new(typeof(long), new(_ => new NumberEditor<long>(), s => new NumberEditorViewModel<long>(s.ToTyped<long>()))),
            new(typeof(sbyte), new(_ => new NumberEditor<sbyte>(), s => new NumberEditorViewModel<sbyte>(s.ToTyped<sbyte>()))),
            new(typeof(ushort), new(_ => new NumberEditor<ushort>(), s => new NumberEditorViewModel<ushort>(s.ToTyped<ushort>()))),
            new(typeof(uint), new(_ => new NumberEditor<uint>(), s => new NumberEditorViewModel<uint>(s.ToTyped<uint>()))),
            new(typeof(ulong), new(_ => new NumberEditor<ulong>(), s => new NumberEditorViewModel<ulong>(s.ToTyped<ulong>()))),
            new(typeof(Rational), new(_ => new RationalEditor(), s => new RationalEditorViewModel(s.ToTyped<Rational>()))),

            new(typeof(bool), new(_ => new BooleanEditor(), s => new BooleanEditorViewModel(s.ToTyped<bool>()))),
            new(typeof(string), new(_ => new StringEditor(), s => new StringEditorViewModel(s.ToTyped<string?>()))),

            new(typeof(AlignmentX), new(_ => new AlignmentXEditor(), s => new AlignmentXEditorViewModel(s.ToTyped<AlignmentX>()))),
            new(typeof(AlignmentY), new(_ => new AlignmentYEditor(), s => new AlignmentYEditorViewModel(s.ToTyped<AlignmentY>()))),
            new(typeof(Enum), new(CreateEnumEditor, CreateEnumViewModel)),
            new(typeof(FontFamily), new(_ => new FontFamilyEditor(), s => new FontFamilyEditorViewModel(s.ToTyped<FontFamily?>()))),
            new(typeof(FileInfo), new(_ => new StorageFileEditor(), s => new StorageFileEditorViewModel(s.ToTyped<FileInfo>()))),

            new(typeof(Color), new(_ => new ColorEditor(), s => new ColorEditorViewModel(s.ToTyped<Color>()))),
            new(typeof(GradingColor), new(_ => new GradingColorEditor(), s => new GradingColorEditorViewModel(s.ToTyped<GradingColor>()))),

            new(typeof(Point), new(_ => new Vector2Editor<float>(), s => new PointEditorViewModel(s.ToTyped<Point>()))),
            new(typeof(Size), new(_ => new Vector2Editor<float>(), s => new SizeEditorViewModel(s.ToTyped<Size>()))),
            new(typeof(Vector2), new(_ => new Vector2Editor<float>(), s => new Vector2EditorViewModel(s.ToTyped<Vector2>()))),
            new(typeof(Graphics.Vector), new(_ => new Vector2Editor<float>(), s => new VectorEditorViewModel(s.ToTyped<Graphics.Vector>()))),
            new(typeof(PixelPoint), new(_ => new Vector2Editor<int>(), s => new PixelPointEditorViewModel(s.ToTyped<PixelPoint>()))),
            new(typeof(PixelSize), new(_ => new Vector2Editor<int>(), s => new PixelSizeEditorViewModel(s.ToTyped<PixelSize>()))),
            new(typeof(RelativePoint), new(_ => new RelativePointEditor(), s => new RelativePointEditorViewModel(s.ToTyped<RelativePoint>()))),
            new(typeof(RelativeRect), new(_ => new RelativeRectEditor(), s => new RelativeRectEditorViewModel(s.ToTyped<RelativeRect>()))),
            new(typeof(Vector3), new(_ => new Vector3Editor<float>(), s => new Vector3EditorViewModel(s.ToTyped<Vector3>()))),
            new(typeof(Vector4), new(_ => new Vector4Editor<float>(), s => new Vector4EditorViewModel(s.ToTyped<Vector4>()))),
            new(typeof(Thickness), new(_ => new Vector4Editor<float>() { Theme = (ControlTheme)Avalonia.Application.Current!.FindResource("ThicknessEditorStyle")! }, s => new ThicknessEditorViewModel(s.ToTyped<Thickness>()))),
            new(typeof(Rect), new(_ => new Vector4Editor<float>(), s => new RectEditorViewModel(s.ToTyped<Rect>()))),
            new(typeof(PixelRect), new(_ => new Vector4Editor<int>(), s => new PixelRectEditorViewModel(s.ToTyped<PixelRect>()))),
            new(typeof(CornerRadius), new(_ => new Vector4Editor<float>() { Theme = (ControlTheme)Avalonia.Application.Current!.FindResource("CornerRadiusEditorStyle")! }, s => new CornerRadiusEditorViewModel(s.ToTyped<CornerRadius>()))),

            new(typeof(TimeSpan), new(_ => new TimeSpanEditor(), s => new TimeSpanEditorViewModel(s.ToTyped<TimeSpan>()))),

            new(typeof(ImageSource), new(_ => new ImageSourceEditor(), s => new ImageSourceEditorViewModel(s.ToTyped<ImageSource?>()))),
            new(typeof(VideoSource), new(_ => new VideoSourceEditor(), s => new VideoSourceEditorViewModel(s.ToTyped<VideoSource?>()))),
            new(typeof(SoundSource), new(_ => new SoundSourceEditor(), s => new SoundSourceEditorViewModel(s.ToTyped<SoundSource?>()))),
            new(typeof(TextureSource), new(_ => new TextureSourceEditor(), s => new TextureSourceEditorViewModel(s.ToTyped<TextureSource?>()))),
            new(typeof(ModelSource), new(_ => new ModelSourceEditor(), s => new ModelSourceEditorViewModel(s.ToTyped<ModelSource?>()))),

            new(typeof(Brush), new(_ => new BrushEditor(), s => new BrushEditorViewModel(s.ToTyped<Brush?>()))),
            new(typeof(Pen), new(_ => new PenEditor(), s => new PenEditorViewModel(s.ToTyped<Pen?>()))),
            new(typeof(FilterEffect), new(_ => new FilterEffectEditor(), s => new FilterEffectEditorViewModel(s.ToTyped<FilterEffect?>()))),
            new(typeof(Geometry), new(_ => new GeometryEditor(), s => new GeometryEditorViewModel(s.ToTyped<Geometry?>()))),
            new(typeof(AudioEffect), new(_ => new AudioEffectEditor(), s => new AudioEffectEditorViewModel(s.ToTyped<AudioEffect?>()))),
            new(typeof(Transform), new(_ => new TransformEditor(), s => new TransformEditorViewModel(s.ToTyped<Transform?>()))),
            new(typeof(CurveMap), new(_ => new CurveMapEditor(), s => new CurveMapEditorViewModel(s.ToTyped<CurveMap>()))),
            new(typeof(ICoreList<GradientStop>), new(_ => new GradientStopsEditor(), s => new GradientStopsEditorViewModel(s.ToTyped<ICoreList<GradientStop>>()))),
            new(typeof(DisplacementMapTransform), new(_ => new DisplacementMapTransformEditor(), s => new DisplacementMapTransformEditorViewModel(s.ToTyped<DisplacementMapTransform?>()))),
            new(typeof(IList), new(CreateListEditor, CreateListEditorViewModel)),
            new(typeof(CoreObject), new(CreateCoreObjectEditor, CreateCoreObjectEditorViewModel)),
            new(typeof(IParsable<>), new(CreateParsableEditor, CreateParsableEditorViewModel)),
        }.ToFrozenDictionary();

        public IEnumerable<IPropertyAdapter> MatchProperty(IReadOnlyList<IPropertyAdapter> properties)
        {
            for (int i = 0; i < properties.Count; i++)
            {
                IPropertyAdapter item = properties[i];
                var attrs = item.GetAttributes();
                // 特殊処理
                if (attrs.OfType<ChoicesProviderAttribute>().Any())
                {
                    yield return item;
                    yield break;
                }
                if (item.GetCoreProperty() is { Id: var id })
                {
                    if (s_editorsOverride.ContainsKey(id))
                    {
                        yield return item;
                        yield break;
                    }
                }

                if (s_editors.ContainsKey(item.PropertyType))
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

        public bool TryCreateContext(PropertyEditorExtension extension, IReadOnlyList<IPropertyAdapter> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
        {
            return TryCreateContextCore(extension, properties, out context);
        }

        public bool TryCreateContextForNode(PropertyEditorExtension extension, IReadOnlyList<IPropertyAdapter> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
        {
            return TryCreateContextCore(extension, properties, out context);
        }

        public bool TryCreateContextForListItem(PropertyEditorExtension extension, IPropertyAdapter property, [NotNullWhen(true)] out IPropertyEditorContext? context)
        {
            BaseEditorViewModel? viewModel = null;
            bool result = false;

            if (s_listItemEditorsOverride.TryGetValue(property.PropertyType, out ListItemEditor editor))
            {
                viewModel = editor.CreateViewModel(property);
                if (viewModel != null)
                {
                    viewModel.Extension = extension;
                    result = true;
                    goto Return;
                }
            }

            foreach (KeyValuePair<Type, ListItemEditor> item in s_listItemEditorsOverride)
            {
                if (property.PropertyType.IsAssignableTo(item.Key))
                {
                    viewModel = item.Value.CreateViewModel(property);
                    if (viewModel != null)
                    {
                        viewModel.Extension = extension;
                        result = true;
                        goto Return;
                    }
                }
            }

            if (!result && TryCreateContextCore(extension, [property], out IPropertyEditorContext? tmp1))
            {
                context = tmp1;
                return true;
            }

        Return:
            context = viewModel;
            return result;
        }

        public bool TryCreateContextForSettings(PropertyEditorExtension extension, IReadOnlyList<IPropertyAdapter> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
        {
            return TryCreateContextCore(extension, properties, out context);
        }

        private static bool TryCreateContextCore(PropertyEditorExtension extension, IReadOnlyList<IPropertyAdapter> properties, [NotNullWhen(true)] out IPropertyEditorContext? context)
        {
            BaseEditorViewModel? viewModel = null;
            bool result = false;

            if (properties.Count > 0 && properties[0] is { } property)
            {
                var attrs = property.GetAttributes();
                if (attrs.OfType<ChoicesProviderAttribute>().FirstOrDefault() is { } choiceAtt)
                {
                    viewModel = CreateChoiceViewModel(property, choiceAtt.ProviderType);
                    if (viewModel != null)
                    {
                        viewModel.Extension = extension;
                        result = true;
                        goto Return;
                    }
                }
                if (property.GetCoreProperty() is { Id: var propId })
                {
                    if (s_editorsOverride.TryGetValue(propId, out Editor editorOverrided))
                    {
                        viewModel = editorOverrided.CreateViewModel(property);
                        if (viewModel != null)
                        {
                            viewModel.Extension = extension;
                            result = true;
                            goto Return;
                        }
                    }
                }

                if (s_editors.TryGetValue(property.PropertyType, out Editor editor))
                {
                    viewModel = editor.CreateViewModel(property);
                    if (viewModel != null)
                    {
                        viewModel.Extension = extension;
                        result = true;
                        goto Return;
                    }
                }

                foreach (KeyValuePair<Type, Editor> item in s_editors)
                {
                    if (property.PropertyType.IsAssignableTo(item.Key))
                    {
                        viewModel = item.Value.CreateViewModel(property);
                        if (viewModel != null)
                        {
                            viewModel.Extension = extension;
                            result = true;
                            goto Return;
                        }
                    }
                }
            }

        Return:
            context = viewModel;
            return result;
        }

        public bool TryCreateControl(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
        {
            if (TryCreateControlCore(context, out Control? control1))
            {
                if (control1 is PropertyEditor editor)
                {
                    editor.MenuContent = new PropertyEditorMenu();
                }

                control = control1;
                return true;
            }
            else if (context is PropertyEditorGroupContext)
            {
                control = new PropertyEditorGroup();
                return true;
            }
            else
            {
                control = null;
                return false;
            }
        }

        public bool TryCreateControlForNode(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
        {
            return TryCreateControlCore(context, out control);
        }

        public bool TryCreateControlForListItem(IPropertyEditorContext context, [NotNullWhen(true)] out IListItemEditor? control)
        {
            control = null;
            try
            {
                if (context is BaseEditorViewModel { PropertyAdapter: { } property })
                {
                    if (s_listItemEditorsOverride.TryGetValue(property.PropertyType, out ListItemEditor editor))
                    {
                        control = editor.CreateEditor(property);
                        if (control != null)
                        {
                            return true;
                        }
                    }

                    foreach (KeyValuePair<Type, ListItemEditor> item in s_listItemEditorsOverride)
                    {
                        if (property.PropertyType.IsAssignableTo(item.Key))
                        {
                            control = item.Value.CreateEditor(property);
                            if (control != null)
                            {
                                return true;
                            }
                        }
                    }
                }

                if (TryCreateControlCore(context, out Control? control1))
                {
                    if (control1 is IListItemEditor control2)
                    {
                        control = control2;
                    }
                    else
                    {
                        var control3 = new ListItemEditorHost();
                        control3.SetChild(control1);
                        control = control3;
                    }

                    return true;
                }
                else
                {
                    return false;
                }
            }
            finally
            {
                if (control is IPropertyEditorContextVisitor visitor)
                {
                    context.Accept(visitor);
                }

                if (control is PropertyEditor pe)
                {
                    pe.EditorStyle = PropertyEditorStyle.ListItem;
                }
            }
        }

        public bool TryCreateControlForSettings(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
        {
            control = null;
            try
            {
                if (TryCreateControlCore(context, out control))
                {
                    return true;
                }
                else if (context is PropertyEditorGroupContext)
                {
                    control = new Pages.SettingsPages.PropertyEditorGroup();
                    return true;
                }
                else
                {
                    control = null;
                    return false;
                }
            }
            finally
            {
                if (control is PropertyEditor pe)
                {
                    pe.EditorStyle = PropertyEditorStyle.Settings;
                }
            }
        }

        private static bool TryCreateControlCore(IPropertyEditorContext context, [NotNullWhen(true)] out Control? control)
        {
            control = null;
            try
            {
                if (context is BaseEditorViewModel { PropertyAdapter: { } property })
                {
                    var attrs = property.GetAttributes();
                    // 特殊処理
                    if (attrs.OfType<ChoicesProviderAttribute>().Any())
                    {
                        control = new EnumEditor();
                        return true;
                    }
                    if (property.GetCoreProperty() is { Id: var propId })
                    {
                        if (s_editorsOverride.TryGetValue(propId, out Editor editorOverrided))
                        {
                            control = editorOverrided.CreateEditor(property);
                            if (control != null)
                            {
                                return true;
                            }
                        }
                    }

                    if (s_editors.TryGetValue(property.PropertyType, out Editor editor))
                    {
                        control = editor.CreateEditor(property);
                        if (control != null)
                        {
                            return true;
                        }
                    }

                    foreach (KeyValuePair<Type, Editor> item in s_editors)
                    {
                        if (property.PropertyType.IsAssignableTo(item.Key))
                        {
                            control = item.Value.CreateEditor(property);
                            if (control != null)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            finally
            {
                if (control is IPropertyEditorContextVisitor visitor)
                {
                    context.Accept(visitor);
                }
            }
        }
    }
}
