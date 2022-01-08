using BEditorNext.Media;
using BEditorNext.ProjectSystem;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditorNext.ViewModels.Editors;

public sealed class PixelSizeEditorViewModel : BaseEditorViewModel<PixelSize>
{
    public PixelSizeEditorViewModel(Setter<PixelSize> setter)
        : base(setter)
    {
        Value = setter.GetObservable()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    public ReadOnlyReactivePropertySlim<PixelSize> Value { get; }

    public PixelSize Maximum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Maximum, new PixelSize(int.MaxValue, int.MaxValue));

    public PixelSize Minimum => Setter.GetValueOrDefault(PropertyMetaTableKeys.Minimum, new PixelSize(int.MinValue, int.MinValue));
}
