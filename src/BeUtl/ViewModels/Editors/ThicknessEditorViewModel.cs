using BeUtl.Graphics;
using BeUtl.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BeUtl.ViewModels.Editors;

public sealed class ThicknessEditorViewModel : BaseEditorViewModel<Thickness>
{
    public ThicknessEditorViewModel(PropertyInstance<Thickness> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<Thickness> Value { get; }

    public Thickness Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new Thickness(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public Thickness Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new Thickness(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}
