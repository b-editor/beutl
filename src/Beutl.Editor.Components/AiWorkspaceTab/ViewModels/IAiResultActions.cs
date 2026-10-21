using Reactive.Bindings;

namespace Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

/// <summary>
/// A screen whose result can be added to the scene or saved to a file.
/// </summary>
internal interface IAiResultActions
{
    ReadOnlyReactivePropertySlim<bool> CanAddToScene { get; }

    AsyncReactiveCommand AddToScene { get; }

    AsyncReactiveCommand SaveToFile { get; }
}
