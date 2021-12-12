using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class FloatEditorViewModel : BaseNumberEditorViewModel<float>
{
    public FloatEditorViewModel(Setter<float> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<float> Value { get; }

    public override float Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, float.MaxValue);

    public override float Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, float.MinValue);

    public override float Clamp(float value, float min, float max)
    {
        return Math.Clamp(value, min, max);
    }

    public override float Decrement(float value, int increment)
    {
        return value - increment;
    }

    public override float Increment(float value, int increment)
    {
        return value + increment;
    }

    public override bool TryParse(string? s, out float result)
    {
        return float.TryParse(s, out result);
    }
}
