using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class Int64EditorViewModel : BaseNumberEditorViewModel<long>
{
    public Int64EditorViewModel(Setter<long> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<long> Value { get; }

    public override long Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, long.MaxValue);

    public override long Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, long.MinValue);

    public override long Clamp(long value, long min, long max)
    {
        return Math.Clamp(value, min, max);
    }

    public override long Decrement(long value, int increment)
    {
        return value - increment;
    }

    public override long Increment(long value, int increment)
    {
        return value + increment;
    }

    public override bool TryParse(string? s, out long result)
    {
        return long.TryParse(s, out result);
    }
}
