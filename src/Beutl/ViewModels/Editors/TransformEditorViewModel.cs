using System.Text.Json.Nodes;
using Avalonia.Input;
using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Transformation;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
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

public sealed class TransformEditorViewModel : ValueEditorViewModel<Transform?>, IFallbackObjectViewModel
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
        CanCopy = Value.Select(v => v is Transform and not FallbackTransform)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsFallback = Value.Select(v => v is IFallback)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        ActualTypeName = Value.Select(FallbackHelper.GetTypeName)
            .ToReadOnlyReactivePropertySlim(Strings.Unknown)
            .DisposeWith(Disposables);

        FallbackMessage = Value.Select(FallbackHelper.GetFallbackMessage)
            .ToReadOnlyReactivePropertySlim(MessageStrings.RestoreFailedTypeNotFound)
            .DisposeWith(Disposables);

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
                ? t.Item1?.Target.GetValue(CompositionContext.Default)
                : null)
            .Select(fe => fe != null ? CoreObjectHelper.GetDisplayName(fe) : MessageStrings.PropertyUnset)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> TransformName { get; }

    public override IReadOnlyReactiveProperty<bool> CanCopy { get; }

    public override IReadOnlyReactiveProperty<bool> CanSaveAsTemplate => CanCopy;

    protected override Type? TemplateBaseType => typeof(Transform);

    protected override DataFormat<string>? PasteFormat => BeutlDataFormats.Transform;

    public ReadOnlyReactivePropertySlim<KnownTransformType> TransformType { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroup { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroupOrNull { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new(false);

    public ReactiveProperty<bool> IsEnabled { get; }

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public ReactivePropertySlim<ListEditorViewModel<Transform?>?> Group { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }

    public ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    public IReadOnlyReactiveProperty<bool> IsFallback { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    public IReadOnlyReactiveProperty<string> FallbackMessage { get; }

    public IObservable<string?> GetJsonString() => FallbackHelper.GetFallbackJson(Value);

    public void SetJsonString(string? str)
    {
        SetValue(Value.Value, FallbackHelper.DeserializeInstance<Transform>(str));
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptChild();
    }

    private void AcceptChild()
    {
        NestedEditorContextHelper.AcceptChildren(new ChildVisitor(this), Group.Value, Properties.Value);
    }

    public void ChangeTransform(Transform instance)
    {
        IsExpanded.Value = true;
        SetValue(Value.Value, instance);
    }

    public void ChangeType(KnownTransformType type)
    {
        if (CreateTransform(type) is { } obj)
            ChangeTransform(obj);
    }

    public void AddItem(Transform instance)
    {
        if (Value.Value is TransformGroup group)
        {
            IsExpanded.Value = true;
            group.Children.Add(instance);
            Commit();

            if (Group.Value is { } listEditor)
            {
                var addedItem = listEditor.Items.LastOrDefault();
                if (addedItem?.Context is TransformEditorViewModel vm)
                {
                    vm.IsExpanded.Value = true;
                }
            }
        }
    }

    public void AddItem(KnownTransformType type)
    {
        if (CreateTransform(type) is { } obj)
            AddItem(obj);
    }

    protected override ICoreSerializable? GetCopyTarget()
        => Value.Value is { } tf and not FallbackTransform ? tf : null;

    protected override ICoreSerializable? GetTemplateTarget() => GetCopyTarget();

    public override bool IsTemplateGroup => Value.Value is TransformGroup;

    public override bool ApplyTemplate(ObjectTemplateItem template)
    {
        if (template.CreateInstance() is not Transform instance) return false;
        IsExpanded.Value = true;
        if (Value.Value is TransformGroup)
            AddItem(instance);
        else
            ChangeTransform(instance);
        Commit(CommandNames.ApplyTemplate);
        return true;
    }

    public override bool TryPasteJson(string json)
    {
        if (!CoreObjectClipboard.TryDeserializeJson<Transform>(json, out var pasted)) return false;

        IsExpanded.Value = true;
        if (Value.Value is TransformGroup group)
        {
            group.Children.Add(pasted);
        }
        else if (EditingKeyFrame.Value is { } kf)
        {
            kf.Value = pasted;
        }
        else if (PropertyAdapter is ListItemAccessorImpl<Transform> listItemAccessor)
        {
            listItemAccessor.List.Insert(listItemAccessor.Index, pasted);
        }
        else
        {
            PropertyAdapter.SetValue(pasted);
        }

        Commit(CommandNames.PasteObject);
        return true;
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
        NestedEditorContextHelper.ReadNestedJson(json, IsExpanded, Properties.Value, Group.Value);
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        NestedEditorContextHelper.WriteNestedJson(json, IsExpanded.Value, Properties.Value, Group.Value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Properties.Value?.Dispose();
        Group.Value?.Dispose();
    }

}
