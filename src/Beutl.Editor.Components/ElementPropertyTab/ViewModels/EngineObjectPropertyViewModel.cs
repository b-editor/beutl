using System.Text.Json.Nodes;
using Beutl.Animation;
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
        // Removing or replacing this object stays allowed on a recovery-protected element while the
        // object holds what could not be loaded: that is how the user repairs the element.
        CanReplace = ElementEditability.ObserveRepair(
                parent.Element.Value, model, parent.GetService(typeof(Scene)) as Scene)
            .ToReadOnlyReactivePropertySlim();
        // A fallback that saves the JSON it was read from as it is would not save turning it on or off.
        CanToggleEnabled = model is IFallback fallback && !fallback.SavesItsMembers()
            ? new ReactivePropertySlim<bool>(false)
            : CanEdit;
        IsEnabled = model.GetObservable(EngineObject.IsEnabledProperty)
            .ToReactiveProperty();
        _enabledSubscription = IsEnabled.Skip(1).Subscribe(v =>
        {
            if (v == Model.IsEnabled) return;
            if (!CanToggleEnabled.Value)
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

    public IReadOnlyReactiveProperty<bool> CanToggleEnabled { get; }

    public ReadOnlyReactivePropertySlim<bool> CanReplace { get; }

    public CoreList<IPropertyEditorContext?> Properties { get; } = [];

    public IReadOnlyReactiveProperty<bool> IsFallback { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    public IReadOnlyReactiveProperty<string> FallbackMessage { get; }

    public void RestoreState(JsonNode json)
    {
        if (json is JsonObject obj)
        {
            if (obj.TryGetPropertyValueAsJsonValue("is-expanded", out bool isExpanded))
            {
                IsExpanded.Value = isExpanded;
            }

            NestedEditorContextHelper.ReadEditorArray(obj, "properties", Properties);
        }
    }

    public JsonNode SaveState()
    {
        var json = new JsonObject { ["is-expanded"] = IsExpanded.Value };
        NestedEditorContextHelper.WriteEditorArray(json, "properties", Properties);
        return json;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        foreach (IPropertyEditorContext? item in Properties.AsSpan())
        {
            item?.Dispose();
        }

        Properties.Clear();
        _enabledSubscription.Dispose();
        IsEnabled.Dispose();
        CanReplace.Dispose();
        if (!ReferenceEquals(CanToggleEnabled, CanEdit))
            (CanToggleEnabled as IDisposable)?.Dispose();

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
            else if (Holds(property.CurrentValue, target) || Holds(property.GetKeyFrameAnimation(), target)
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
        => FallbackHelper.GetFallbackJson(Observable.ReturnThenNever<EngineObject?>(Model));

    public void SetJsonString(string? str)
    {
        if (_isDisposed || !CanReplace.Value) return;
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
