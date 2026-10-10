using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.ElementPropertyTab.ViewModels;

public sealed class EngineObjectPropertyViewModel : IDisposable, IPropertyEditorContextVisitor, IServiceProvider, IFallbackObjectViewModel
{
    private ElementPropertyTabViewModel _parent;
    private readonly IDisposable _enabledSubscription;
    private bool _isDisposed;

    public EngineObjectPropertyViewModel(EngineObject model, ElementPropertyTabViewModel parent)
    {
        Model = model;
        _parent = parent;
        CanEdit = parent.CanEdit;
        IsEnabled = model.GetObservable(EngineObject.IsEnabledProperty)
            .ToReactiveProperty();
        _enabledSubscription = IsEnabled.Skip(1).Subscribe(v =>
        {
            if (v == Model.IsEnabled) return;
            if (!CanEdit.Value)
            {
                IsEnabled.Value = Model.IsEnabled;
                return;
            }

            IElementObjectService? service = this.GetService<IElementObjectService>();
            if (service?.SetEnabled(Model, v) != true)
                IsEnabled.Value = Model.IsEnabled;
        });

        Init();

        IsFallback = Observable.ReturnThenNever(model is IFallback)
            .ToReadOnlyReactivePropertySlim();

        ActualTypeName = Observable.ReturnThenNever(FallbackHelper.GetTypeName(model))
            .ToReadOnlyReactivePropertySlim()!;

        FallbackMessage = Observable.ReturnThenNever(FallbackHelper.GetFallbackMessage(model))
            .ToReadOnlyReactivePropertySlim()!;
    }

    public EngineObject Model { get; private set; }

    public ReactiveProperty<bool> IsExpanded { get; } = new(true);

    public ReactiveProperty<bool> IsEnabled { get; }

    public IReadOnlyReactiveProperty<bool> CanEdit { get; }

    public CoreList<IPropertyEditorContext?> Properties { get; } = [];

    public IReadOnlyReactiveProperty<bool> IsFallback { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    public IReadOnlyReactiveProperty<string> FallbackMessage { get; }

    public void RestoreState(JsonNode json)
    {
        if (json is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("is-expanded", out JsonNode? isExpandedNode)
                && isExpandedNode is JsonValue isExpandedValue
                && isExpandedValue.TryGetValue(out bool isExpanded))
            {
                IsExpanded.Value = isExpanded;
            }

            if (obj.TryGetPropertyValue("properties", out JsonNode? propsNode)
                && propsNode is JsonArray propsArray)
            {
                foreach ((JsonNode? node, IPropertyEditorContext? context) in propsArray.Zip(Properties))
                {
                    if (context != null && node != null)
                    {
                        context.ReadFromJson(node.AsObject());
                    }
                }
            }
        }
    }

    public JsonNode SaveState()
    {
        var array = new JsonArray();

        foreach (IPropertyEditorContext? item in Properties.GetMarshal().Value)
        {
            if (item == null)
            {
                array.Add(null);
            }
            else
            {
                var node = new JsonObject();
                item.WriteToJson(node);
                array.Add(node);
            }
        }

        return new JsonObject { ["is-expanded"] = IsExpanded.Value, ["properties"] = array };
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        foreach (IPropertyEditorContext? item in Properties.GetMarshal().Value)
        {
            item?.Dispose();
        }

        Properties.Clear();
        _enabledSubscription.Dispose();
        IsEnabled.Dispose();

        Model = null!;
        _parent = null!;
    }

    private void Init()
    {
        var factory = this.GetRequiredService<IPropertyEditorFactory>();
        var adapters = PropertyAdapterFactory.CreateAdapters(Model);
        var contexts = factory.CreatePropertyEditorContexts(adapters, this);
        Properties.AddRange(contexts);
    }

    public void Visit(IPropertyEditorContext context)
    {
    }

    // Returns the top-level editor that shows `target` or its `propertyName`, or null when the
    // change concerns the whole object. Nested objects (a transform, an effect, an animation and
    // its keyframes) resolve to the editor of the property that holds them.
    internal IPropertyEditorContext? FindPropertyEditor(CoreObject target, string? propertyName)
    {
        if (ReferenceEquals(target, Model) && propertyName is null)
            return null;

        foreach (IPropertyEditorContext? context in Properties)
        {
            if ((context as IServiceProvider)?.GetService(typeof(IPropertyAdapter)) is not IPropertyAdapter adapter
                || adapter.GetEngineProperty() is not { } property)
            {
                continue;
            }

            if (ReferenceEquals(property.GetOwnerObject(), target))
            {
                if (property.Name == propertyName)
                    return context;
            }
            else if (Holds(property.CurrentValue, target) || Holds(property.Animation, target)
                     || (property is IListProperty list && list.OfType<object>().Any(item => Holds(item, target))))
            {
                return context;
            }
        }

        return null;
    }

    private static bool Holds(object? value, CoreObject target)
        => value is IHierarchical holder && ElementPropertyTabViewModel.IsWithin(target, holder);

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(EngineObject))
            return Model;

        return _parent.GetService(serviceType);
    }

    public IObservable<string?> GetJsonString()
    {
        if (Model is FallbackEngineObject { Json: JsonObject json })
        {
            return Observable.ReturnThenNever(json.ToJsonString(JsonHelper.SerializerOptions));
        }

        return Observable.ReturnThenNever<string?>(null);
    }

    public void SetJsonString(string? str)
    {
        if (_isDisposed || !CanEdit.Value) return;
        if (Model.HierarchicalParent is not Element element) return;

        int index = element.Objects.IndexOf(Model);
        if (index < 0) return;

        string message = MessageStrings.InvalidJson;
        if (str is null) throw new Exception(message);

        ObjectPasteOutcome outcome = this.GetRequiredService<IElementObjectService>()
            .PasteOver(element, index, str);
        if (outcome != ObjectPasteOutcome.Pasted)
        {
            throw new Exception(message);
        }
    }
}
