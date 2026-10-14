using Avalonia.Input;
using Beutl.Engine;
using Beutl.Graphics.Transformation;
using Beutl.PropertyAdapters;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public enum KnownTransformType
{
    Unknown,
    Group,
    Translate,
    Rotation,
    Scale,
    Skew,
    Rotation3D,
    Presenter
}

// A transform the editor offers: its type, how to create one, its name and the resource key of its icon (null for
// none). The name is read on each use, so it follows the UI culture.
internal sealed record KnownTransform(
    KnownTransformType Kind, Type Type, Func<Transform> Create, Func<string> Name, string? IconKey);

public sealed class TransformEditorViewModel : GroupableEditorViewModel<Transform, TransformGroup, FallbackTransform>
{
    // In the order the menus list them.
    internal static IReadOnlyList<KnownTransform> KnownTransforms { get; } =
    [
        new(KnownTransformType.Group, typeof(TransformGroup), () => new TransformGroup(),
            () => GraphicsStrings.Group, null),
        new(KnownTransformType.Translate, typeof(TranslateTransform), () => new TranslateTransform(),
            () => GraphicsStrings.TranslateTransform, "TranslateTransformIconData"),
        new(KnownTransformType.Rotation, typeof(RotationTransform), () => new RotationTransform(),
            () => GraphicsStrings.Rotation, "RotationTransformIconData"),
        new(KnownTransformType.Scale, typeof(ScaleTransform), () => new ScaleTransform(),
            () => GraphicsStrings.Scale, "ScaleTransformIconData"),
        new(KnownTransformType.Skew, typeof(SkewTransform), () => new SkewTransform(),
            () => GraphicsStrings.SkewTransform, "SkewTransformIconData"),
        new(KnownTransformType.Rotation3D, typeof(Rotation3DTransform), () => new Rotation3DTransform(),
            () => GraphicsStrings.Rotation3DTransform, "Rotation3DTransformIconData"),
        new(KnownTransformType.Presenter, typeof(TransformPresenter), () => new TransformPresenter(),
            () => GraphicsStrings.Presenter, null),
    ];

    internal static KnownTransform? Find(KnownTransformType type)
        => KnownTransforms.FirstOrDefault(t => t.Kind == type);

    // A drop is taken only for exactly one of these types, and not for a presenter.
    internal static KnownTransformType FromDroppedType(Type type)
        => KnownTransforms.FirstOrDefault(t => t.Type == type && t.Kind != KnownTransformType.Presenter)?.Kind
           ?? KnownTransformType.Unknown;

    private static KnownTransformType GetTransformType(Transform? obj)
    {
        if (obj is IPresenter<Transform>)
            return KnownTransformType.Presenter;

        return KnownTransforms.FirstOrDefault(t => t.Type.IsInstanceOfType(obj))?.Kind ?? KnownTransformType.Unknown;
    }

    public TransformEditorViewModel(IPropertyAdapter<Transform?> property)
        : base(property)
    {
        TransformType = Value.Select(GetTransformType)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        TransformName = TransformType.Select(type => Find(type)?.Name() ?? "Null")
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        (IsPresenter, CurrentTargetName) = PresenterEditorHelper.ObservePresenter<Transform>(Value, Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> TransformName { get; }

    protected override DataFormat<string>? PasteFormat => BeutlDataFormats.Transform;

    public ReadOnlyReactivePropertySlim<KnownTransformType> TransformType { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }

    public ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    public void ChangeTransform(Transform instance) => ChangeValue(instance);

    public void ChangeType(KnownTransformType type)
    {
        if (Find(type)?.Create() is { } obj)
            ChangeTransform(obj);
    }

    public void AddItem(KnownTransformType type)
    {
        if (Find(type)?.Create() is { } obj)
            AddItem(obj);
    }

    protected override IListProperty<Transform> ChildrenOf(TransformGroup group) => group.Children;
}
