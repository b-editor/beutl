using System.Text.Json.Nodes;
using Avalonia.Input;
using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public interface ICoreObjectEditorViewModel : IServiceProvider
{
    string Header { get; }

    ReactivePropertySlim<string?> Description { get; }

    IReadOnlyReactiveProperty<CoreObject?> Value { get; }

    ReadOnlyReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; }

    ReactivePropertySlim<bool> IsExpanded { get; }

    ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    ReadOnlyReactivePropertySlim<bool> IsNull { get; }

    ReadOnlyReactivePropertySlim<bool> IsNotSetAndCanWrite { get; }

    ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }

    ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    IReadOnlyReactiveProperty<bool> CanCopy { get; }

    IReadOnlyReactiveProperty<bool> CanPaste { get; }

    IPropertyAdapter PropertyAdapter { get; }

    bool CanWrite { get; }

    bool IsDisposed { get; }

    void SetNewInstance(Type type);

    void SetTarget(CoreObject? target);

    void SetNull();

    IReadOnlyList<TargetObjectInfo> GetAvailableTargets();
}

public sealed class CoreObjectEditorViewModel<T> : BaseEditorViewModel<T>, ICoreObjectEditorViewModel, IFallbackObjectViewModel
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
            .Select(obj => obj != null ? CoreObjectHelper.GetDisplayName(obj) : MessageStrings.PropertyUnset)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

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

    public ReadOnlyReactivePropertySlim<bool> IsNotSetAndCanWrite { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }

    public ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    IReadOnlyReactiveProperty<CoreObject?> ICoreObjectEditorViewModel.Value => Value;

    public bool CanWrite { get; }

    public IReadOnlyReactiveProperty<bool> IsFallback { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    public IReadOnlyReactiveProperty<string> FallbackMessage { get; }

    public IObservable<string?> GetJsonString() => FallbackHelper.GetFallbackJson(Value);

    public void SetJsonString(string? str)
    {
        SetValue(Value.Value, FallbackHelper.DeserializeInstance<T>(str));
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
        if (template.CreateInstance() is not T instance) return false;
        IsExpanded.Value = true;
        PropertyAdapter.SetValue(instance);
        Commit(CommandNames.ApplyTemplate);
        return true;
    }

    public override bool TryPasteJson(string json)
    {
        if (!CoreObjectClipboard.TryDeserializeJson<T>(json, out var pasted)) return false;

        IsExpanded.Value = true;
        if (EditingKeyFrame.Value is { } kf)
        {
            kf.Value = pasted;
        }
        else if (PropertyAdapter is ListItemAccessorImpl<T> listItemAccessor)
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

    public void SetTarget(CoreObject? target)
    {
        if (Value.Value is not IPresenter<T> presenter)
        {
            Type? presenterType = PresenterTypeAttribute.GetPresenterType(PropertyAdapter.PropertyType);
            if (presenterType == null) return;
            if (Activator.CreateInstance(presenterType) is not IPresenter<T> p) return;
            presenter = p;
            PropertyAdapter.SetValue(presenter);
        }

        if (target is T)
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

    public IReadOnlyList<TargetObjectInfo> GetAvailableTargets()
    {
        var scene = this.GetService<EditViewModel>()?.Scene;
        if (scene == null) return [];

        var searcher = new ObjectSearcher(scene, obj =>
            obj is T && obj is not IPresenter<T>);

        return searcher.SearchAll()
            .Cast<T>()
            .Select(obj =>
                new TargetObjectInfo(CoreObjectHelper.GetDisplayName(obj), obj, CoreObjectHelper.GetOwnerElement(obj)))
            .ToList();
    }

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
