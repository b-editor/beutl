using Avalonia.Input;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.PropertyAdapters;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class FilterEffectEditorViewModel : GroupableEditorViewModel<FilterEffect, FilterEffectGroup, FallbackFilterEffect>, IPresenterEditorViewModel
{
    private readonly ReactivePropertySlim<IEditorSelection?> _selection = new();
    private readonly ReactivePropertySlim<IEffectComparisonSelection?> _comparisonSelection = new();

    public FilterEffectEditorViewModel(IPropertyAdapter<FilterEffect?> property)
        : base(property)
    {
        _selection.DisposeWith(Disposables);
        _comparisonSelection.DisposeWith(Disposables);
        CanSelectEffect = Value.CombineLatest(_selection, (value, selection) => value is IEditingHandleProvider && selection != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);
        CanCompareEffect = _comparisonSelection
            .Select(selection => selection?.IsSelectingEffectForComparison ?? Observable.Return(false))
            .Switch()
            .CombineLatest(Value, (selecting, value) => selecting && value is not null
                && _comparisonSelection.Value?.CanCompareEffect(value) == true)
            .ToReadOnlyReactivePropertySlim()
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

        (IsPresenter, CurrentTargetName) = PresenterEditorHelper.ObservePresenter<FilterEffect>(Value, Disposables);
    }

    protected override DataFormat<string>? PasteFormat => BeutlDataFormats.FilterEffect;

    public ReadOnlyReactivePropertySlim<string?> FilterName { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPresenter { get; }

    public ReadOnlyReactivePropertySlim<string?> CurrentTargetName { get; }

    // Whether the effect has editing handles, which the preview shows while it is selected, and can be selected here.
    public ReadOnlyReactivePropertySlim<bool> CanSelectEffect { get; }

    internal ReadOnlyReactivePropertySlim<bool> CanCompareEffect { get; }

    void ITargetPickerViewModel.SetTarget(CoreObject? target) => SetTarget(target as FilterEffect);

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        // The copy that stands for several elements is in no scene, so selecting its effect would select nothing shown.
        _selection.Value = EditsSeveralElements ? null : this.GetService<IEditorSelection>();
        _comparisonSelection.Value = EditsSeveralElements ? null : this.GetService<IEffectComparisonSelection>();
    }

    internal void ChooseComparisonEffect()
    {
        if (!IsDisposed && Value.Value is { } effect)
            _comparisonSelection.Value?.TryChooseComparisonEffect(effect);
    }

    public void SelectEffect()
    {
        if (!IsDisposed && CanSelectEffect.Value)
        {
            _selection.Value?.Select(Value.Value);
        }
    }

    public void ChangeFilterType(Type type)
    {
        if (Activator.CreateInstance(type) is FilterEffect instance)
            ChangeValue(instance);
    }

    public void AddItem(Type type)
    {
        if (Activator.CreateInstance(type) is FilterEffect instance)
            AddItem(instance);
    }

    public void AddTarget(Type presenterType, FilterEffect target)
    {
        if (!IsElementEditable) return;
        if (Activator.CreateInstance(presenterType) is IPresenter<FilterEffect> presenter)
        {
            PresenterEditorHelper.AssignTarget(presenter, target);
            AddItem((FilterEffect)presenter);
        }
    }

    protected override IListProperty<FilterEffect> ChildrenOf(FilterEffectGroup group) => group.Children;
}
