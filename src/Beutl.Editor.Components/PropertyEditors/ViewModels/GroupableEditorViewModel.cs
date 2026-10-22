using System.Text.Json.Nodes;
using Avalonia.Input;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// What the audio effect, filter effect and transform editors share: a value that may be a group of
// values of its own kind, edited as a nested list, or a fallback for one that could not be read.
public abstract class GroupableEditorViewModel<T, TGroup, TFallback> : ValueEditorViewModel<T?>, IFallbackObjectViewModel, IObjectEditorCommands
    where T : EngineObject
    where TGroup : T
    where TFallback : T
{
    protected GroupableEditorViewModel(IPropertyAdapter<T?> property)
        : base(property)
    {
        CanCopy = Value.Select(v => v is T and not TFallback)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        (IsFallback, ActualTypeName, FallbackMessage) = FallbackEditorHelper.ObserveFallbackInfo(Value, Disposables);

        IsGroup = Value.Select(v => v is TGroup)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        IsGroupOrNull = Value.Select(v => v is TGroup || v == null)
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

                    if (v is TGroup group)
                    {
                        var prop = new EnginePropertyAdapter<ICoreList<T>>(ChildrenOf(group), group);
                        Group.Value = new ListEditorViewModel<T>(prop)
                        {
                            IsExpanded = { Value = true }
                        };
                    }
                    else if (v != null)
                    {
                        Properties.Value = new PropertiesEditorViewModel(v, GetExtensionProvider());
                    }

                    AcceptChild();
                })
                .DisposeWith(Disposables))
            .DisposeWith(Disposables);

        IsEnabled = Value.Select(x => x?.IsEnabledProperty.GetObservable() ?? Observable.ReturnThenNever(x?.IsEnabled ?? false))
            .Switch()
            .ToReactiveProperty()
            .DisposeWith(Disposables);

        IsEnabled.Skip(1)
            .Subscribe(v =>
            {
                if (Value.Value is { } value && value.IsEnabled != v)
                {
                    // A fallback that saves the JSON it was read from as it is would lose the change.
                    if (!IsElementEditable || value is IFallback fallback && !fallback.SavesItsMembers())
                    {
                        IsEnabled.Value = value.IsEnabled;
                        return;
                    }
                    value.IsEnabled = v;
                    Commit();
                }
            })
            .DisposeWith(Disposables);
    }

    public IReadOnlyReactiveProperty<bool> CanCopy { get; }

    public ReactivePropertySlim<bool> CanPaste { get; } = new();

    public IReadOnlyReactiveProperty<bool> CanSaveAsTemplate => CanCopy;

    Type? IObjectEditorCommands.TemplateBaseType => typeof(T);

    protected abstract DataFormat<string>? PasteFormat { get; }

    DataFormat<string>? IObjectEditorCommands.PasteFormat => PasteFormat;

    public ReadOnlyReactivePropertySlim<bool> IsGroup { get; }

    public ReadOnlyReactivePropertySlim<bool> IsGroupOrNull { get; }

    public ReactivePropertySlim<bool> IsExpanded { get; } = new();

    public ReactiveProperty<bool> IsEnabled { get; }

    public ReactivePropertySlim<PropertiesEditorViewModel?> Properties { get; } = new();

    public ReactivePropertySlim<ListEditorViewModel<T>?> Group { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsFallback { get; }

    public IReadOnlyReactiveProperty<string> ActualTypeName { get; }

    public IReadOnlyReactiveProperty<string> FallbackMessage { get; }

    public IObservable<string?> GetJsonString() => FallbackHelper.GetFallbackJson(Value);

    public void SetJsonString(string? str)
    {
        T? previous = Value.Value;
        SetValue(previous, FallbackHelper.DeserializeInstance<T>(str));
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

    protected void ChangeValue(T instance, string? commandName = null)
    {
        IsExpanded.Value = true;
        SetValue(Value.Value, instance, commandName);
    }

    public ICoreSerializable? GetCopyTarget()
        => Value.Value is T value and not TFallback ? value : null;

    public ICoreSerializable? GetTemplateTarget() => GetCopyTarget();

    public bool TryPasteJson(string json)
    {
        return GroupedEditorHelper.TryPasteJson(
            json, this, IsExpanded,
            Value.Value is TGroup group ? ChildrenOf(group) : null);
    }

    public bool IsTemplateGroup => Value.Value is TGroup;

    public bool ApplyTemplate(ObjectTemplateItem template)
    {
        return GroupedEditorHelper.ApplyTemplate(
            template, this, IsExpanded,
            Value.Value is TGroup,
            AddItem, ChangeValue);
    }

    public void AddItem(T instance, string? commandName = null)
    {
        if (!IsElementEditable) return;
        if (Value.Value is TGroup group)
        {
            IsExpanded.Value = true;
            ChildrenOf(group).Add(instance);
            Commit(commandName);

            if (Group.Value is { } listEditor)
            {
                var addedItem = listEditor.Items.LastOrDefault();
                if (addedItem?.Context is GroupableEditorViewModel<T, TGroup, TFallback> vm)
                {
                    vm.IsExpanded.Value = true;
                }
            }
        }
    }

    public void SetNull()
    {
        SetValue(Value.Value, null);
    }

    // For a value that presents another object.
    public void SetTarget(T? target)
    {
        // Only the presenter's target is replaced, so check that rather than the whole value.
        if (IsDisposed || Value.Value is not IPresenter<T> presenter) return;
        if (!CanReplace(presenter.Target.Value)) return;
        PresenterEditorHelper.AssignTarget(presenter, target);
        CompleteElementRepair();
        Commit();
    }

    public IReadOnlyList<TargetObjectInfo> GetAvailableTargets()
        => TargetObjectSearchHelper.GetAvailableTargets<T>(this);

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

    protected abstract IListProperty<T> ChildrenOf(TGroup group);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Properties.Value?.Dispose();
        Group.Value?.Dispose();
    }
}
