using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Helpers;
using Beutl.Operation;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class FilterEffectEditorViewModel : ValueEditorViewModel<FilterEffect?>, IUnknownObjectViewModel
{
    public FilterEffectEditorViewModel(IPropertyAdapter<FilterEffect?> property)
        : base(property)
    {
        IsDummy = Value.Select(v => v is IDummy)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        ActualTypeName = Value.Select(DummyHelper.GetTypeName)
            .ToReadOnlyReactivePropertySlim(Strings.Unknown)
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

        Value.CombineWithPrevious()
            .Select(v => v.OldValue)
            .Where(v => v != null)
            .Subscribe(v => this.GetService<ISupportCloseAnimation>()?.Close(v!))
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
                ? t.Item1?.Target.GetValue(RenderContext.Default)
                : null)
            .Select(fe => fe != null ? CoreObjectHelper.GetDisplayName(fe) : Message.Property_is_unset)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<string?> FilterName { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroup { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroupOrNull { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactiveProperty<bool> IsEnabled { get; }

    public IReadOnlyReactiveProperty<bool> IsDummy { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

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

    public void ChangeFilterType(Type type)
    {
        if (Activator.CreateInstance(type) is FilterEffect instance)
        {
            SetValue(Value.Value, instance);
        }
    }

    public void AddItem(Type type)
    {
        if (Value.Value is FilterEffectGroup group
            && Activator.CreateInstance(type) is FilterEffect instance)
        {
            group.Children.Add(instance);
            Commit();
        }
    }

    public void AddTarget(Type presenterType, FilterEffect target)
    {
        if (Value.Value is FilterEffectGroup group
            && Activator.CreateInstance(presenterType) is IPresenter<FilterEffect> presenter)
        {
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
    {
        var scene = this.GetService<EditViewModel>()?.Scene;
        if (scene == null) return [];

        var searcher = new ObjectSearcher(scene, obj =>
            obj is FilterEffect && obj is not IPresenter<FilterEffect>);

        return searcher.SearchAll()
            .Cast<FilterEffect>()
            .Select(fe => new TargetObjectInfo(CoreObjectHelper.GetDisplayName(fe), fe, CoreObjectHelper.GetOwnerElement(fe)))
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

    public IObservable<string?> GetJsonString()
    {
        return Value.Select(v =>
        {
            if (v is DummyFilterEffect { Json: JsonObject json })
            {
                return json.ToJsonString(JsonHelper.SerializerOptions);
            }

            return null;
        });
    }

    public void SetJsonString(string? str)
    {
        string message = Strings.InvalidJson;
        _ = str ?? throw new Exception(message);
        JsonObject json = (JsonNode.Parse(str) as JsonObject) ?? throw new Exception(message);

        Type? type = json.GetDiscriminator();
        FilterEffect? instance = null;
        if (type?.IsAssignableTo(typeof(FilterEffect)) ?? false)
        {
            instance = Activator.CreateInstance(type) as FilterEffect;
        }

        if (instance == null) throw new Exception(message);

        CoreSerializer.PopulateFromJsonObject(instance, type!, json);

        SetValue(Value.Value, instance);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Properties.Value?.Dispose();
        Group.Value?.Dispose();
    }

    private sealed record Visitor(FilterEffectEditorViewModel Obj) : IServiceProvider, IPropertyEditorContextVisitor
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
