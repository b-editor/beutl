using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;

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

public sealed class TransformEditorViewModel : ValueEditorViewModel<Transform?>
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
            KnownTransformType.Group => Strings.Group,
            KnownTransformType.Translate => Strings.Translate,
            KnownTransformType.Rotation => Strings.Rotation,
            KnownTransformType.Scale => Strings.Scale,
            KnownTransformType.Skew => Strings.Skew,
            KnownTransformType.Rotation3D => Strings.Rotation3D,
            KnownTransformType.Presenter => Strings.Presenter,
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

        IsGroup = Value.Select(v => v is TransformGroup)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsGroupOrNull = Value.Select(v => v is TransformGroup || v == null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsExpanded.SkipWhile(v => !v)
            .Take(1)
            .Subscribe(_ =>
                Value.Subscribe(v =>
                {
                    Properties.Value?.Dispose();
                    Properties.Value = null;
                    Group.Value?.Dispose();
                    Group.Value = null;

                    if (v is TransformGroup group)
                    {
                        var prop = new EnginePropertyAdapter<ICoreList<Transform>>(group.Children, group);
                        Group.Value = new ListEditorViewModel<Transform?>(prop)
                        {
                            IsExpanded = { Value = true }
                        };
                    }
                    else if (v != null)
                    {
                        Properties.Value = new PropertiesEditorViewModel(v);
                    }

                    AcceptChild();
                })
                .DisposeWith(Disposables))
            .DisposeWith(Disposables);

        IsEnabled = Value.Select(x => (x as Transform)?.GetObservable(Transform.IsEnabledProperty) ?? Observable.ReturnThenNever(x?.IsEnabled ?? false))
            .Switch()
            .ToReactiveProperty()
            .DisposeWith(Disposables);

        IsEnabled.Skip(1)
            .Subscribe(v =>
            {
                if (Value.Value is Transform transform && transform.IsEnabled != v)
                {
                    transform.IsEnabled = v;
                    Commit();
                }
            })
            .DisposeWith(Disposables);

        Value.CombineWithPrevious()
            .Select(v => v.OldValue)
            .Where(v => v != null)
            .Subscribe(v => this.GetService<ISupportCloseAnimation>()?.Close(v!))
            .DisposeWith(Disposables);

        var expressionObservable = Value
            .Select(v => v switch
            {
                IPresenter<Transform> presenter => presenter.Target.SubscribeExpressionChange()
                    .Select(exp => (presenter, exp))!,
                _ => Observable.ReturnThenNever(
                    ((IPresenter<Transform>?)null, (IExpression<Transform?>?)null))
            })
            .Switch();
        IsPresenter = expressionObservable
            .Select(t => t is { Item1: not null, Item2: ReferenceExpression<Transform> or null })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        CurrentTargetName = expressionObservable
            .Select(t => t.Item2 is ReferenceExpression<Transform>
                ? t.Item1?.Target.GetValue(RenderContext.Default)
                : null)
            .Select(fe => fe != null ? CoreObjectHelper.GetDisplayName(fe) : Message.Property_is_unset)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> TransformName { get; }

    public ReadOnlyReactivePropertySlim<KnownTransformType> TransformType { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroup { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroupOrNull { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new(false);

    public ReactiveProperty<bool> IsEnabled { get; }

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public ReactivePropertySlim<ListEditorViewModel<Transform?>?> Group { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }

    public ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptChild();
    }

    private void AcceptChild()
    {
        var visitor = new Visitor(this);
        Group.Value?.Accept(visitor);

        if (Properties.Value != null)
        {
            foreach (IPropertyEditorContext item in Properties.Value.Properties)
            {
                item.Accept(visitor);
            }
        }
    }

    public void ChangeType(KnownTransformType type)
    {
        Transform? obj = CreateTransform(type);
        if (obj != null)
        {
            SetValue(Value.Value, obj);
        }
    }

    public void AddItem(KnownTransformType type)
    {
        if (Value.Value is TransformGroup group
            && CreateTransform(type) is { } obj)
        {
            group.Children.Add(obj);
            Commit();
        }
    }

    public void SetNull()
    {
        SetValue(Value.Value, null);
    }

    public void SetTarget(Transform? target)
    {
        if (Value.Value is IPresenter<Transform> presenter)
        {
            if (target != null)
            {
                var expression = Expression.CreateReference<Transform>(target.Id);
                presenter.Target.Expression = expression;
            }
            else
            {
                presenter.Target.Expression = null;
                presenter.Target.CurrentValue = null;
            }
            Commit();
        }
    }

    public IReadOnlyList<TargetObjectInfo> GetAvailableTargets()
    {
        var scene = this.GetService<EditViewModel>()?.Scene;
        if (scene == null) return [];

        var searcher = new ObjectSearcher(scene, obj =>
            obj is Transform && obj is not IPresenter<Transform>);

        return searcher.SearchAll()
            .Cast<Transform>()
            .Select(t => new TargetObjectInfo(CoreObjectHelper.GetDisplayName(t), t, CoreObjectHelper.GetOwnerElement(t)))
            .ToList();
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

            if (Group.Value != null
                && json.TryGetPropertyValue(nameof(Group), out var groupNode)
                && groupNode is JsonObject group)
            {
                Group.Value.ReadFromJson(group);
            }
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
            if (Group.Value != null)
            {
                var group = new JsonObject();
                Group.Value.WriteToJson(group);
                json[nameof(Group)] = group;
            }
        }
        catch
        {
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Properties.Value?.Dispose();
        Group.Value?.Dispose();
    }

    private sealed record Visitor(TransformEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
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
