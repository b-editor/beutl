using System.Text.Json.Nodes;
using Avalonia.Input;
using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Effects;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class FilterEffectEditorViewModel : ValueEditorViewModel<FilterEffect?>, IFallbackObjectViewModel
{
    public FilterEffectEditorViewModel(IPropertyAdapter<FilterEffect?> property)
        : base(property)
    {
        CanCopy = Value.Select(v => v is FilterEffect and not FallbackFilterEffect)
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

        FilterName = Value.Select(v =>
            {
                if (v != null)
                {
                    Type type = v.GetType();
                    return TypeDisplayHelpers.GetLocalizedName(type);
                }
                else
                {
                    return "Null";
                }
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsGroup = Value.Select(v => v is FilterEffectGroup)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsGroupOrNull = Value.Select(v => v is FilterEffectGroup || v == null)
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

                        if (v is FilterEffectGroup group)
                        {
                            var prop = new EnginePropertyAdapter<ICoreList<FilterEffect>>(group.Children, group);
                            Group.Value = new ListEditorViewModel<FilterEffect>(prop) { IsExpanded = { Value = true } };
                        }
                        else if (v != null)
                        {
                            Properties.Value = new PropertiesEditorViewModel(v);
                        }

                        AcceptChild();
                    })
                    .DisposeWith(Disposables))
            .DisposeWith(Disposables);

        IsEnabled = Value.Select(x =>
                x?.GetObservable(FilterEffect.IsEnabledProperty) ?? Observable.ReturnThenNever(x?.IsEnabled ?? false))
            .Switch()
            .ToReactiveProperty()
            .DisposeWith(Disposables);

        IsEnabled.Skip(1)
            .Subscribe(v =>
            {
                if (Value.Value is { } filter && filter.IsEnabled != v)
                {
                    filter.IsEnabled = v;
                    Commit();
                }
            })
            .DisposeWith(Disposables);

        var expressionObservable = Value
            .Select(v => v switch
            {
                IPresenter<FilterEffect> presenter => presenter.Target.SubscribeExpressionChange()
                    .Select(exp => (presenter, exp))!,
                _ => Observable.ReturnThenNever(
                    ((IPresenter<FilterEffect>?)null, (IExpression<FilterEffect?>?)null))
            })
            .Switch();
        IsPresenter = expressionObservable
            .Select(t => t is { Item1: not null, Item2: ReferenceExpression<FilterEffect> or null })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        CurrentTargetName = expressionObservable
            .Select(t => t.Item2 is ReferenceExpression<FilterEffect>
                ? t.Item1?.Target.GetValue(CompositionContext.Default)
                : null)
            .Select(fe => fe != null ? CoreObjectHelper.GetDisplayName(fe) : MessageStrings.PropertyUnset)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public override IReadOnlyReactiveProperty<bool> CanCopy { get; }

    public override IReadOnlyReactiveProperty<bool> CanSaveAsTemplate => CanCopy;

    protected override Type? TemplateBaseType => typeof(FilterEffect);

    protected override DataFormat<string>? PasteFormat => BeutlDataFormats.FilterEffect;

    public ReadOnlyReactivePropertySlim<string?> FilterName { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroup { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroupOrNull { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactiveProperty<bool> IsEnabled { get; }

    public IReadOnlyReactiveProperty<bool> IsFallback { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    public IReadOnlyReactiveProperty<string> FallbackMessage { get; }

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public ReactivePropertySlim<ListEditorViewModel<FilterEffect>?> Group { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }

    public ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        AcceptChild();
    }

    private void AcceptChild()
    {
        NestedEditorContextHelper.AcceptChildren(new ChildVisitor(this), Group.Value, Properties.Value);
    }

    public void ChangeFilter(FilterEffect instance)
    {
        IsExpanded.Value = true;
        SetValue(Value.Value, instance);
    }

    public void ChangeFilterType(Type type)
    {
        if (Activator.CreateInstance(type) is FilterEffect instance)
            ChangeFilter(instance);
    }

    protected override ICoreSerializable? GetCopyTarget()
        => Value.Value is FilterEffect fe and not FallbackFilterEffect ? fe : null;

    protected override ICoreSerializable? GetTemplateTarget() => GetCopyTarget();

    public override bool IsTemplateGroup => Value.Value is FilterEffectGroup;

    public override bool ApplyTemplate(ObjectTemplateItem template)
    {
        return GroupedEditorHelper.ApplyTemplate(
            template, this, IsExpanded,
            Value.Value is FilterEffectGroup,
            AddItem, ChangeFilter);
    }

    public override bool TryPasteJson(string json)
    {
        return GroupedEditorHelper.TryPasteJson(
            json, this, IsExpanded,
            (Value.Value as FilterEffectGroup)?.Children);
    }

    public void AddItem(FilterEffect instance)
    {
        if (Value.Value is FilterEffectGroup group)
        {
            IsExpanded.Value = true;
            group.Children.Add(instance);
            Commit();

            if (Group.Value is { } listEditor)
            {
                var addedItem = listEditor.Items.LastOrDefault();
                if (addedItem?.Context is FilterEffectEditorViewModel vm)
                {
                    vm.IsExpanded.Value = true;
                }
            }
        }
    }

    public void AddItem(Type type)
    {
        if (Activator.CreateInstance(type) is FilterEffect instance)
            AddItem(instance);
    }

    public void AddTarget(Type presenterType, FilterEffect target)
    {
        if (Value.Value is FilterEffectGroup group
            && Activator.CreateInstance(presenterType) is IPresenter<FilterEffect> presenter)
        {
            IsExpanded.Value = true;
            presenter.Target.Expression = Expression.CreateReference<FilterEffect>(target.Id);
            group.Children.Add((FilterEffect)presenter);
            Commit();
        }
    }

    public void SetNull()
    {
        SetValue(Value.Value, null);
    }

    public void SetTarget(FilterEffect? target)
    {
        if (Value.Value is IPresenter<FilterEffect> presenter)
        {
            if (target != null)
            {
                var expression = Expression.CreateReference<FilterEffect>(target.Id);
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
        => TargetObjectSearchHelper.GetAvailableTargets<FilterEffect>(this);



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

    public IObservable<string?> GetJsonString() => FallbackHelper.GetFallbackJson(Value);

    public void SetJsonString(string? str)
    {
        SetValue(Value.Value, FallbackHelper.DeserializeInstance<FilterEffect>(str));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Properties.Value?.Dispose();
        Group.Value?.Dispose();
    }
}
