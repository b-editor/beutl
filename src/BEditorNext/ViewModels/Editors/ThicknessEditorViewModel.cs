using BEditorNext.Graphics;
using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class ThicknessEditorViewModel : BaseEditorViewModel<Thickness>
{
    public ThicknessEditorViewModel(Setter<Thickness> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Thickness> Value { get; }

    public Thickness Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Thickness(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public Thickness Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Thickness(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}
