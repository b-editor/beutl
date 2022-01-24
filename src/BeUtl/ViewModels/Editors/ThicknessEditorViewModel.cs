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

    public Thickness Maximum => Setter.GetMaximumOrDefault(new Thickness(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue));

    public Thickness Minimum => Setter.GetMinimumOrDefault(new Thickness(float.MinValue, float.MinValue, float.MinValue, float.MinValue));
}
