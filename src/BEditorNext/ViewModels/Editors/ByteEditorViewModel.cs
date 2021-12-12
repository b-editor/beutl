using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class ByteEditorViewModel : BaseNumberEditorViewModel<byte>
{
    public ByteEditorViewModel(Setter<byte> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<byte> Value { get; }

    public override byte Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, byte.MaxValue);

    public override byte Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, byte.MinValue);

    public override byte Clamp(byte value, byte min, byte max)
    {
        return Math.Clamp(value, min, max);
    }

    public override byte Decrement(byte value, int increment)
    {
        return (byte)(value - increment);
    }

    public override byte Increment(byte value, int increment)
    {
        return (byte)(value + increment);
    }

    public override bool TryParse(string? s, out byte result)
    {
        return byte.TryParse(s, out result);
    }
}
