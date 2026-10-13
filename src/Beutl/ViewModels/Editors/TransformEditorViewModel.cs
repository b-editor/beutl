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

public sealed class TransformEditorViewModel : GroupableEditorViewModel<Transform, TransformGroup, FallbackTransform>
{
    private static KnownTransformType GetTransformType(Transform? obj)
    {
        return obj switch
        {
            TransformGroup => KnownTransformType.Group,
            TranslateTransform => KnownTransformType.Translate,
            RotationTransform => KnownTransformType.Rotation,
            ScaleTransform => KnownTransformType.Scale,
            SkewTransform => KnownTransformType.Skew,
            Rotation3DTransform => KnownTransformType.Rotation3D,
            IPresenter<Transform> => KnownTransformType.Presenter,
            _ => KnownTransformType.Unknown
        };
    }

    private static Transform? CreateTransform(KnownTransformType type)
    {
        return type switch
        {
            KnownTransformType.Group => new TransformGroup(),
            KnownTransformType.Translate => new TranslateTransform(),
            KnownTransformType.Rotation => new RotationTransform(),
            KnownTransformType.Scale => new ScaleTransform(),
            KnownTransformType.Skew => new SkewTransform(),
            KnownTransformType.Rotation3D => new Rotation3DTransform(),
            KnownTransformType.Presenter => new TransformPresenter(),
            _ => null
        };
    }

    private static string ToDisplayName(KnownTransformType type)
    {
        return type switch
        {
            KnownTransformType.Group => GraphicsStrings.Group,
            KnownTransformType.Translate => GraphicsStrings.TranslateTransform,
            KnownTransformType.Rotation => GraphicsStrings.Rotation,
            KnownTransformType.Scale => GraphicsStrings.Scale,
            KnownTransformType.Skew => GraphicsStrings.SkewTransform,
            KnownTransformType.Rotation3D => GraphicsStrings.Rotation3DTransform,
            KnownTransformType.Presenter => GraphicsStrings.Presenter,
            _ => "Null"
        };
    }

    public TransformEditorViewModel(IPropertyAdapter<Transform?> property)
        : base(property)
    {
        TransformType = Value.Select(GetTransformType)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        TransformName = TransformType.Select(ToDisplayName)
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
        if (CreateTransform(type) is { } obj)
            ChangeTransform(obj);
    }

    public void AddItem(KnownTransformType type)
    {
        if (CreateTransform(type) is { } obj)
            AddItem(obj);
    }

    protected override IListProperty<Transform> ChildrenOf(TransformGroup group) => group.Children;
}
