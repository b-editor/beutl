using BEditorNext.Media;
using BEditorNext.ProjectSystem;

using Reactive.Bindings;

namespace BEditorNext.ViewModels.Editors;

public sealed class PixelSizeEditorViewModel : BaseEditorViewModel<PixelSize>
{
    public PixelSizeEditorViewModel(Setter<PixelSize> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReadOnlyReactivePropertySlim<PixelSize> Value { get; }

    public PixelSize Maximum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new PixelSize(int.MaxValue, int.MaxValue));

    public PixelSize Minimum => Setter.Property.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new PixelSize(int.MinValue, int.MinValue));
}
