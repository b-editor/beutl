using System.Text.Json.Nodes;
using Avalonia.Input;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public interface ICoreObjectEditorViewModel : IServiceProvider, IPresenterEditorViewModel
{
    string Header { get; }

    ReactivePropertySlim<string?> Description { get; }

    ReactivePropertySlim<string?> HoverInfo { get; }

    IReadOnlyReactiveProperty<CoreObject?> Value { get; }

    ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; }

    ReactivePropertySlim<bool> IsExpanded { get; }

    ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    ReadOnlyReactivePropertySlim<bool> IsNull { get; }

    ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    IReadOnlyReactiveProperty<bool> CanCopy { get; }

    IReadOnlyReactiveProperty<bool> CanPaste { get; }

    IPropertyAdapter PropertyAdapter { get; }

    void SetNewInstance(Type type);

    void SetNull();
}

public sealed class CoreObjectEditorViewModel<T> : BaseEditorViewModel<T>, ICoreObjectEditorViewModel, IFallbackObjectViewModel
    where T : CoreObject
{
    public CoreObjectEditorViewModel(IPropertyAdapter<T> property)
        : base(property)
    {
        IsNull = property.GetObservable()
            .Select(x => x == null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Value = property.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        Properties = Value
            .CombineLatest(ObserveExtensionProvider())
            .Select(t => t.First != null ? new PropertiesEditorViewModel(t.First, t.Second) : null)
            .DisposePreviousValue()
            .Do(AcceptProperties)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        (IsPresenter, CurrentTargetName) = PresenterEditorHelper.ObservePresenter<T>(Value, Disposables);

        IsFallback = Value.Select(v => v is IFallback)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        CanCopy = Value.Select(v => v is T and not IFallback)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        ActualTypeName = Value.Select(FallbackHelper.GetTypeName)
            .ToReadOnlyReactivePropertySlim(Strings.Unknown)
            .DisposeWith(Disposables);

        FallbackMessage = Value.Select(FallbackHelper.GetFallbackMessage)
            .ToReadOnlyReactivePropertySlim(MessageStrings.RestoreFailedTypeNotFound)
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<T?> Value { get; }

    public override IReadOnlyReactiveProperty<bool> CanCopy { get; }

    protected override DataFormat<string>? PasteFormat => BeutlDataFormats.EngineObject;

    public ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsNull { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }

    public ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    IReadOnlyReactiveProperty<CoreObject?> ICoreObjectEditorViewModel.Value => Value;

    public IReadOnlyReactiveProperty<bool> IsFallback { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    public IReadOnlyReactiveProperty<string> FallbackMessage { get; }

    public IObservable<string?> GetJsonString() => FallbackHelper.GetFallbackJson(Value);

    public void SetJsonString(string? str)
    {
        T? previous = Value.Value;
        SetValue(previous, FallbackHelper.DeserializeInstance<T>(str));
    }

    public void SetNull()
    {
        SetValue(Value.Value, null);
    }

    public void SetNewInstance(Type type)
    {
        if (Activator.CreateInstance(type) is T typed)
        {
            SetValue(Value.Value, typed);
        }
    }

    protected override ICoreSerializable? GetCopyTarget()
        => Value.Value is T obj and not IFallback ? obj : null;

    public override IReadOnlyReactiveProperty<bool> CanSaveAsTemplate => CanCopy;

    protected override Type? TemplateBaseType => typeof(T);

    protected override ICoreSerializable? GetTemplateTarget() => GetCopyTarget();

    public override bool ApplyTemplate(ObjectTemplateItem template)
    {
        // SetValue checks the value it replaces.
        if (!CanReplaceContent) return false;
        if (template.CreateInstance() is not T instance) return false;
        IsExpanded.Value = true;
        SetValue(GetEditingValue(), instance, CommandNames.ApplyTemplate);
        return true;
    }

    public override bool TryPasteJson(string json)
        => GroupedEditorHelper.TryPasteJson(json, this!, IsExpanded, null);

    public void SetTarget(CoreObject? target)
    {
        if (IsDisposed) return;
        // A kept presenter has only its target replaced, so check that rather than the whole value.
        if (!CanReplace(Value.Value is IPresenter<T> kept ? kept.Target.CurrentValue : Value.Value)) return;

        if (Value.Value is not IPresenter<T> presenter)
        {
            Type? presenterType = PresenterTypeAttribute.GetPresenterType(PropertyAdapter.PropertyType);
            if (presenterType == null) return;
            if (Activator.CreateInstance(presenterType) is not IPresenter<T> p) return;

            presenter = p;
            PropertyAdapter.SetValue(presenter);
        }

        PresenterEditorHelper.AssignTarget(presenter, target as T);

        CompleteElementRepair();
        Commit();
    }

    public IReadOnlyList<TargetObjectInfo> GetAvailableTargets()
        => TargetObjectSearchHelper.GetAvailableTargets<T>(this);

    private void AcceptProperties(PropertiesEditorViewModel? obj)
    {
        NestedEditorContextHelper.AcceptChildren(new ChildVisitor(this), null, obj);
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
        NestedEditorContextHelper.ReadNestedJson(json, IsExpanded, Properties.Value);
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        NestedEditorContextHelper.WriteNestedJson(json, IsExpanded.Value, Properties.Value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Properties.Value?.Dispose();
    }
}
