using Avalonia.Input;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.PropertyAdapters;
using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class FilterEffectEditorViewModel : GroupableEditorViewModel<FilterEffect, FilterEffectGroup, FallbackFilterEffect>, IPresenterEditorViewModel
{
    public FilterEffectEditorViewModel(IPropertyAdapter<FilterEffect?> property)
        : base(property)
    {
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

    void IPresenterEditorViewModel.SetTarget(CoreObject? target) => SetTarget(target as FilterEffect);

    public void ChangeFilter(FilterEffect instance) => ChangeValue(instance);

    public void ChangeFilterType(Type type)
    {
        if (Activator.CreateInstance(type) is FilterEffect instance)
            ChangeFilter(instance);
    }

    public void AddItem(Type type)
    {
        if (Activator.CreateInstance(type) is FilterEffect instance)
            AddItem(instance);
    }

    public void AddTarget(Type presenterType, FilterEffect target)
    {
        if (!IsElementEditable) return;
        if (Value.Value is FilterEffectGroup group
            && Activator.CreateInstance(presenterType) is IPresenter<FilterEffect> presenter)
        {
            IsExpanded.Value = true;
            PresenterEditorHelper.AssignTarget(presenter, target);
            group.Children.Add((FilterEffect)presenter);
            Commit();
        }
    }

    protected override IListProperty<FilterEffect> ChildrenOf(FilterEffectGroup group) => group.Children;
}
