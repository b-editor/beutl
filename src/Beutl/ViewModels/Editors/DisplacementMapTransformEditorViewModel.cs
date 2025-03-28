using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Graphics.Effects;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public enum DispMapTransformType
{
    Null,
    Translate,
    Rotation,
    Scale
}

public sealed class DisplacementMapTransformEditorViewModel : ValueEditorViewModel<DisplacementMapTransform?>
{
    private static DispMapTransformType GetTransformType(DisplacementMapTransform? obj)
    {
        return obj switch
        {
            DisplacementMapTranslateTransform => DispMapTransformType.Translate,
            DisplacementMapRotationTransform => DispMapTransformType.Rotation,
            DisplacementMapScaleTransform => DispMapTransformType.Scale,
            _ => DispMapTransformType.Null
        };
    }

    private static DisplacementMapTransform? CreateTransform(DispMapTransformType type)
    {
        return type switch
        {
            DispMapTransformType.Translate => new DisplacementMapTranslateTransform(),
            DispMapTransformType.Rotation => new DisplacementMapRotationTransform(),
            DispMapTransformType.Scale => new DisplacementMapScaleTransform(),
            _ => null
        };
    }

    private static string ToDisplayName(DispMapTransformType type)
    {
        return type switch
        {
            DispMapTransformType.Translate => Strings.Translate,
            DispMapTransformType.Rotation => Strings.Rotation,
            DispMapTransformType.Scale => Strings.Scale,
            _ => "Null"
        };
    }

    public DisplacementMapTransformEditorViewModel(IPropertyAdapter<DisplacementMapTransform?> property)
        : base(property)
    {
        TransformType = Value.Select(GetTransformType)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        TransformName = TransformType.Select(ToDisplayName)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsTranslate = TransformType.Select(v => v == DispMapTransformType.Translate)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsRotation = TransformType.Select(v => v == DispMapTransformType.Rotation)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsScale = TransformType.Select(v => v == DispMapTransformType.Scale)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsNull = Value.Select(v => v == null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsExpanded.SkipWhile(v => !v)
            .Take(1)
            .Subscribe(_ =>
                Value.Subscribe(v =>
                    {
                        Properties.Value?.Dispose();
                        Properties.Value = null;

                        if (v is not null)
                        {
                            Properties.Value = new PropertiesEditorViewModel(v, (p, m) => m.Browsable);
                        }

                        AcceptChild();
                    })
                    .DisposeWith(Disposables))
            .DisposeWith(Disposables);

        Value.CombineWithPrevious()
            .Select(v => v.OldValue as IAnimatable)
            .Where(v => v != null)
            .Subscribe(v => this.GetService<ISupportCloseAnimation>()?.Close(v!))
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> TransformName { get; }

    public ReadOnlyReactivePropertySlim<DispMapTransformType> TransformType { get; }

    public ReadOnlyReactivePropertySlim<bool> IsTranslate { get; }

    public ReadOnlyReactivePropertySlim<bool> IsRotation { get; }

    public ReadOnlyReactivePropertySlim<bool> IsScale { get; }

    public ReadOnlyReactivePropertySlim<bool> IsNull { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new(false);

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptChild();
    }

    private void AcceptChild()
    {
        var visitor = new Visitor(this);
        if (Properties.Value != null)
        {
            foreach (IPropertyEditorContext item in Properties.Value.Properties)
            {
                item.Accept(visitor);
            }
        }
    }

    public void ChangeType(DispMapTransformType type)
    {
        DisplacementMapTransform? obj = CreateTransform(type);
        SetValue(Value.Value, obj);
    }

    public void SetNull()
    {
        SetValue(Value.Value, null);
    }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        try
        {
            if (json.TryGetPropertyValue(nameof(IsExpanded), out var isExpandedNode)
                && isExpandedNode is JsonValue isExpanded)
            {
                IsExpanded.Value = (bool)isExpanded;
            }

            Properties.Value?.ReadFromJson(json);
        }
        catch
        {
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        try
        {
            json[nameof(IsExpanded)] = IsExpanded.Value;
            Properties.Value?.WriteToJson(json);
        }
        catch
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Properties.Value?.Dispose();
    }

    private sealed record Visitor(DisplacementMapTransformEditorViewModel Obj)
        : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType)
        {
            return Obj.GetService(serviceType);
        }

        public void Visit(IPropertyEditorContext context)
        {
        }
    }
}
