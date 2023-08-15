using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public interface IConfigureLivePreview
{
    ReactivePropertySlim<bool> IsLivePreviewEnabled { get; }
}
