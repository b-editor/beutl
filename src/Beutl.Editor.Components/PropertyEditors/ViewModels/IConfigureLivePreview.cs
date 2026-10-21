using Reactive.Bindings;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public interface IConfigureLivePreview
{
    ReactivePropertySlim<bool> IsLivePreviewEnabled { get; }
}
