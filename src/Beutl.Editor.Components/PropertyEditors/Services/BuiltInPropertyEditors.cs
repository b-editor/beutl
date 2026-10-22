using System.Collections;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Styling;
using Beutl.Animation.Easings;
using Beutl.Audio.Effects;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D.Models;
using Beutl.Graphics3D.Textures;
using Beutl.IO;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;
using AColor = Avalonia.Media.Color;

namespace Beutl.Editor.Components.PropertyEditors.Services;

// The built-in editors, which PropertyEditorExtension.Instance holds.
internal static class BuiltInPropertyEditors
{
    public static void Register(PropertyEditorRegistry registry)
    {
        Number<byte>(registry);
        Number<decimal>(registry);
        Number<double>(registry);
        Number<float>(registry);
        Number<short>(registry);
        Number<int>(registry);
        Number<long>(registry);
        Number<sbyte>(registry);
        Number<ushort>(registry);
        Number<uint>(registry);
        Number<ulong>(registry);
        registry.RegisterValue<Rational, Rational>(p => new RationalEditorViewModel(p), () => new RationalEditor());

        registry.RegisterValue<bool, bool>(p => new BooleanEditorViewModel(p), () => new BooleanEditor());
        registry.RegisterValue<string?, string>(p => new StringEditorViewModel(p), () => new StringEditor());

        registry.RegisterValue<AlignmentX, AlignmentX>(p => new AlignmentXEditorViewModel(p), () => new AlignmentXEditor());
        registry.RegisterValue<AlignmentY, AlignmentY>(p => new AlignmentYEditorViewModel(p), () => new AlignmentYEditor());
        registry.RegisterValue<TextAlignment, TextAlignment>(p => new TextAlignmentEditorViewModel(p), () => new TextAlignmentEditor());
        Add(registry, typeof(Enum), p => Create(typeof(EnumEditorViewModel<>), p.PropertyType, p), _ => new EnumEditor());
        registry.RegisterValue<FontFamily?, FontFamily?>(p => new FontFamilyEditorViewModel(p), () => new FontFamilyEditor());
        registry.RegisterValue<FileInfo, FileInfo?>(p => new StorageFileEditorViewModel(p), () => new StorageFileEditor());
        Add(registry, typeof(IFileSource), CreateFileSourceEditor, _ => new StorageFileEditor());

        registry.RegisterValue<Color, AColor>(p => new ColorEditorViewModel(p), () => new ColorEditor());
        registry.RegisterValue<GradingColor, GradingColor>(p => new GradingColorEditorViewModel(p), () => new GradingColorEditor());

        registry.RegisterValue<Point, (float, float)>(VectorEditors.Point, () => new Vector2Editor<float>());
        registry.RegisterValue<Size, (float, float)>(VectorEditors.Size, () => new Vector2Editor<float>());
        registry.RegisterValue<Vector2, (float, float)>(VectorEditors.Vector2, () => new Vector2Editor<float>());
        registry.RegisterValue<Graphics.Vector, (float, float)>(VectorEditors.Vector, () => new Vector2Editor<float>());
        registry.RegisterValue<PixelPoint, (int, int)>(VectorEditors.PixelPoint, () => new Vector2Editor<int>());
        registry.RegisterValue<PixelSize, (int, int)>(VectorEditors.PixelSize, () => new Vector2Editor<int>());
        registry.RegisterValue<RelativePoint, RelativePoint>(p => new RelativePointEditorViewModel(p), () => new RelativePointEditor());
        registry.RegisterValue<RelativeRect, RelativeRect>(p => new RelativeRectEditorViewModel(p), () => new RelativeRectEditor());
        registry.RegisterValue<Vector3, (float, float, float)>(VectorEditors.Vector3, () => new Vector3Editor<float>());
        registry.RegisterValue<Vector4, (float, float, float, float)>(VectorEditors.Vector4, () => new Vector4Editor<float>());
        registry.RegisterValue<Thickness, (float, float, float, float)>(
            VectorEditors.Thickness, () => new Vector4Editor<float> { Theme = FindTheme("ThicknessEditorStyle") });
        registry.RegisterValue<Rect, (float, float, float, float)>(VectorEditors.Rect, () => new Vector4Editor<float>());
        registry.RegisterValue<PixelRect, (int, int, int, int)>(VectorEditors.PixelRect, () => new Vector4Editor<int>());
        registry.RegisterValue<CornerRadius, (float, float, float, float)>(
            VectorEditors.CornerRadius, () => new Vector4Editor<float> { Theme = FindTheme("CornerRadiusEditorStyle") });

        registry.RegisterValue<MediaTime, MediaTime>(p => new MediaTimeEditorViewModel(p), () => new MediaTimeEditor());
        // The node time ports stay TimeSpan until #2885 step 4; remove this when they move to MediaTime.
        registry.RegisterValue<TimeSpan, MediaTime>(p => new TimeSpanValueEditorViewModel(p), () => new MediaTimeEditor());
        Add(registry, typeof(Easing), p => Create(typeof(EasingEditorViewModel<>), p.PropertyType, p), _ => new EasingEditor());

        Add<ImageSource?>(registry, p => new ImageSourceEditorViewModel(p), () => new ImageSourceEditor());
        Add<VideoSource?>(registry, p => new VideoSourceEditorViewModel(p), () => new VideoSourceEditor());
        Add<SoundSource?>(registry, p => new SoundSourceEditorViewModel(p), () => new SoundSourceEditor());
        Add<TextureSource?>(registry, p => new TextureSourceEditorViewModel(p), () => new TextureSourceEditor());
        Add<ModelSource?>(registry, p => new ModelSourceEditorViewModel(p), () => new ModelSourceEditor());
        Add<CubeSource?>(registry, p => new CubeSourceEditorViewModel(p), () => new CubeSourceEditor());

        Add<Brush?>(registry, p => new BrushEditorViewModel(p), () => new BrushEditor());
        Add<Pen?>(registry, p => new PenEditorViewModel(p), () => new PenEditor());
        Add<Scene?>(registry, p => new SceneEditorViewModel(p), () => new SceneEditor());
        Add<FilterEffect?>(registry, p => new FilterEffectEditorViewModel(p), () => new FilterEffectEditor());
        Add<Geometry?>(registry, p => new GeometryEditorViewModel(p), () => new GeometryEditor());
        Add<AudioEffect?>(registry, p => new AudioEffectEditorViewModel(p), () => new AudioEffectEditor());
        Add<Transform?>(registry, p => new TransformEditorViewModel(p), () => new TransformEditor());
        Add<CurveMap>(registry, p => new CurveMapEditorViewModel(p), () => new CurveMapEditor());
        Add<ICoreList<GradientStop>>(registry, p => new GradientStopsEditorViewModel(p), () => new GradientStopsEditor());
        Add<DisplacementMapTransform?>(registry, p => new DisplacementMapTransformEditorViewModel(p), () => new DisplacementMapTransformEditor());
        Add(registry, typeof(IList), CreateListEditor, _ => new ListEditor());
        Add<GraphModel?>(registry, p => new GraphModelEditorViewModel(p), () => new GraphModelEditor());
        Add(registry, typeof(CoreObject), p => Create(typeof(CoreObjectEditorViewModel<>), p.PropertyType, p), _ => new CoreObjectEditor());

        registry.Add(new PropertyEditorRegistration
        {
            Tier = PropertyEditorTier.Choices,
            CreateContext = properties => properties[0].GetAttributes().OfType<ChoicesProviderAttribute>().FirstOrDefault() is { } choices
                ? Create(typeof(ProvidedChoiceEditorViewModel<,>), [properties[0].PropertyType, choices.ProviderType], properties[0])
                : null,
            CreateControl = (_, _) => new EnumEditor(),
            Connect = Connect,
            IsStandard = true,
            Owner = registry.Owner,
        });

        // The editors of list items, which take the place of the ones above in a list.
        AddListItem<FilterEffect?>(registry, p => new FilterEffectEditorViewModel(p), () => new FilterEffectListItemEditor());
        AddListItem<PathSegment?>(registry, p => new PathSegmentEditorViewModel(p), () => new PathOperationListItemEditor());
        AddListItem<PathFigure>(registry, p => new PathFigureEditorViewModel(p), () => new PathFigureListItemEditor());
        AddListItem<AudioEffect?>(registry, p => new AudioEffectEditorViewModel(p), () => new AudioEffectListItemEditor());
        AddListItem<Transform?>(registry, p => new TransformEditorViewModel(p), () => new TransformListItemEditor());
        Add(registry, typeof(IFileSource), CreateFileSourceEditor, _ => new StorageFileEditor(), listItemOnly: true);
        Add(registry, typeof(CoreObject), p => Create(typeof(CoreObjectEditorViewModel<>), p.PropertyType, p), _ => new CoreObjectListItemEditor(), listItemOnly: true);
    }

    private static void Number<T>(PropertyEditorRegistry registry)
        where T : struct, INumber<T>
        => registry.RegisterValue<T, T>(p => new NumberEditorViewModel<T>(p), () => new NumberEditor<T>());

    private static void Add<T>(PropertyEditorRegistry registry, Func<IPropertyAdapter<T>, IPropertyEditorContext> createContext, Func<Control> createControl)
        => Add(registry, typeof(T), p => p is IPropertyAdapter<T> typed ? createContext(typed) : null, _ => createControl());

    private static void AddListItem<T>(PropertyEditorRegistry registry, Func<IPropertyAdapter<T>, IPropertyEditorContext> createContext, Func<Control> createControl)
        => Add(registry, typeof(T), p => p is IPropertyAdapter<T> typed ? createContext(typed) : null, _ => createControl(), listItemOnly: true);

    private static void Add(
        PropertyEditorRegistry registry,
        Type valueType,
        Func<IPropertyAdapter, IPropertyEditorContext?> createContext,
        Func<IPropertyEditorContext, Control?> createControl,
        bool listItemOnly = false)
    {
        registry.Add(new PropertyEditorRegistration
        {
            ValueType = valueType,
            CreateContext = properties => createContext(properties[0]),
            CreateControl = (context, _) => createControl(context),
            Connect = Connect,
            IsStandard = true,
            HasListItemControl = listItemOnly,
            IsListItemOnly = listItemOnly,
            Owner = registry.Owner,
        });
    }

    private static void Connect(IPropertyEditorContext context, Control control)
        => (context as PropertyEditorViewModel)?.TryConnect(control);

    private static IPropertyEditorContext? Create(Type viewModelType, Type typeArgument, IPropertyAdapter property)
        => Create(viewModelType, [typeArgument], property);

    private static IPropertyEditorContext? Create(Type viewModelType, Type[] typeArguments, IPropertyAdapter property)
        => Activator.CreateInstance(viewModelType.MakeGenericType(typeArguments), property) as IPropertyEditorContext;

    private static bool CanCreateFileSource(Type type)
    {
        return type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters
            && type.IsAssignableTo(typeof(IFileSource)) && type.GetConstructor(Type.EmptyTypes) != null;
    }

    private static IPropertyEditorContext? CreateFileSourceEditor(IPropertyAdapter property)
        => CanCreateFileSource(property.PropertyType) ? Create(typeof(FileSourceEditorViewModel<>), property.PropertyType, property) : null;

    private static IPropertyEditorContext? CreateListEditor(IPropertyAdapter property)
    {
        Type? interfaceType = Array.Find(
            property.PropertyType.GetInterfaces(), x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IList<>));
        return interfaceType?.GenericTypeArguments.FirstOrDefault() is { } itemType
            ? Create(typeof(ListEditorViewModel<>), itemType, property)
            : null;
    }

    private static ControlTheme FindTheme(string key) => (ControlTheme)Avalonia.Application.Current!.FindResource(key)!;
}
