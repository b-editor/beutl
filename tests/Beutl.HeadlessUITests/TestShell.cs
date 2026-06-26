using Avalonia;
using Beutl.Api.Services;
using Beutl.Services;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

// There are no ProjectService/EditorService/ExtensionProvider singletons; MainViewModel owns those
// instances and exposes them as internal properties (visible here via InternalsVisibleTo).
internal static class TestShell
{
    public static MainViewModel MainViewModel => ((TestApp)Application.Current!).GetMainViewModel();

    public static ProjectService Project => MainViewModel.ProjectService;

    public static EditorService Editor => MainViewModel.EditorService;

    public static ExtensionProvider Extensions => MainViewModel.ExtensionProvider;
}
