using Reactive.Bindings;

namespace Beutl.PackageTools.UI.Models;

// The state that the step header of an install task shows.
public interface ITaskStepModel
{
    ReactiveProperty<bool> ShowDetails { get; }

    ReactiveProperty<bool> IsRunning { get; }

    ReactiveProperty<bool> Succeeded { get; }

    ReactiveProperty<bool> Failed { get; }
}
