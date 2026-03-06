using System.Text.Json.Nodes;
using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public interface ICoreObjectEditorViewModel
{
    string Header { get; }

    ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; }

    ReactivePropertySlim<bool> IsExpanded { get; }

    ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    ReadOnlyReactivePropertySlim<bool> IsNull { get; }

    ReadOnlyReactivePropertySlim<bool> IsNotSetAndCanWrite { get; }

    ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }

    ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    bool CanWrite { get; }
}

public sealed class CoreObjectEditorViewModel<T> : BaseEditorViewModel<T>, ICoreObjectEditorViewModel, IUnknownObjectViewModel
    where T : CoreObject
{
    public CoreObjectEditorViewModel(IPropertyAdapter<T> property)
        : base(property)
    {
        CanWrite = !property.IsReadOnly;

        IsNull = property.GetObservable()
            .Select(x => x == null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsNotSetAndCanWrite = IsNull.Select(x => x && CanWrite)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Properties = Value
            .Select(x => x != null ? new PropertiesEditorViewModel(x) : null)
            .DisposePreviousValue()
            .Do(AcceptProperties)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        var expressionObservable = Value
            .Select(v => v switch
            {
                IPresenter<T> presenter => presenter.Target.SubscribeExpressionChange()
                    .Select(exp => (presenter, exp))!,
                _ => Observable.ReturnThenNever(
                    ((IPresenter<T>?)null, (IExpression<T?>?)null))
            })
            .Switch();
        IsPresenter = expressionObservable
            .Select(t => t is { Item1: not null, Item2: ReferenceExpression<T> or null })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        CurrentTargetName = expressionObservable
            .Select(t => t.Item2 is ReferenceExpression<T>
                ? t.Item1?.Target.GetValue(CompositionContext.Default)
                : null)
            .Select(obj => obj != null ? CoreObjectHelper.GetDisplayName(obj) : Message.Property_is_unset)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsFallback = Value.Select(v => v is IFallback)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        ActualTypeName = Value.Select(FallbackHelper.GetTypeName)
            .ToReadOnlyReactivePropertySlim(Strings.Unknown)
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<T?> Value { get; }

    public ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsNull { get; }

    public ReadOnlyReactivePropertySlim<bool> IsNotSetAndCanWrite { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }

    public ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    public bool CanWrite { get; }

    public IReadOnlyReactiveProperty<bool> IsFallback { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    public IObservable<string?> GetJsonString()
    {
        return Value.Select(v =>
        {
            if (v is IFallback { Json: JsonObject json })
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
        T? instance = null;
        if (type?.IsAssignableTo(typeof(T)) ?? false)
        {
            instance = Activator.CreateInstance(type) as T;
        }

        if (instance == null) throw new Exception(message);

        CoreSerializer.PopulateFromJsonObject(instance, type!, json);

        SetValue(Value.Value, instance);
    }

    public void SetNull()
    {
        SetValue(Value.Value, null);
    }

    public void SetTarget(T? target)
    {
        if (Value.Value is IPresenter<T> presenter)
        {
            if (target != null)
            {
                var expression = Expression.CreateReference<T>(target.Id);
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
            obj is T && obj is not IPresenter<T>);

        return searcher.SearchAll()
            .Cast<T>()
            .Select(obj => new TargetObjectInfo(CoreObjectHelper.GetDisplayName(obj), obj, CoreObjectHelper.GetOwnerElement(obj)))
            .ToList();
    }



    private void AcceptProperties(PropertiesEditorViewModel? obj)
    {
        if (obj != null)
        {
            var visitor = new Visitor(this);
            foreach (IPropertyEditorContext item in obj.Properties)
            {
                item.Accept(visitor);
            }
        }
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is IServiceProvider)
        {
            AcceptProperties(Properties.Value);
        }
    }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        try
        {
            if (json.TryGetPropertyValue(nameof(IsExpanded), out JsonNode? isExpandedNode)
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

    private sealed record Visitor(CoreObjectEditorViewModel<T> Obj) : IServiceProvider, IPropertyEditorContextVisitor
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
