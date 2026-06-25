using Avalonia;
using Avalonia.Markup.Xaml;
using Beutl.Api.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Extensibility;
using Beutl.NodeGraph.Nodes;
using Beutl.Services;
using Beutl.Services.StartupTasks;
using Beutl.Services.Tutorials;
using Beutl.ViewModels;
using Reactive.Bindings;
using ReactiveUI.Avalonia;

namespace Beutl.HeadlessUITests;

public sealed class TestApp : Application
{
    // Mirrors LoadPrimitiveExtensionTask's built-in package slot (LocalPackage.Reserved0, which is internal).
    private const int BuiltInExtensionPackageId = 0;

    // The headless session re-runs RegisterServices per test, but the registries it populates are
    // process-global singletons; guard the one-time population so re-entry does not double-register.
    private static bool s_globalServicesInitialized;

    private MainViewModel? _mainViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void RegisterServices()
    {
        base.RegisterServices();

        PropertyEditorExtension.DefaultHandler = new PropertyEditorService.PropertyEditorExtensionImpl();
        NotificationService.Handler = new NotificationServiceHandler();
        TutorialService.Current = new TutorialServiceHandler();
        AppHelper.GetContextCommandManager = () => GetMainViewModel().ContextCommandManager;
        ReactivePropertyScheduler.SetDefault(AvaloniaScheduler.Instance);

        if (s_globalServicesInitialized)
        {
            return;
        }

        s_globalServicesInitialized = true;
        LibraryRegistrar.RegisterAll();
        NodesRegistrar.RegisterAll();
        foreach (Extension item in LoadPrimitiveExtensionTask.PrimitiveExtensions)
        {
            item.Load();
        }
    }

    public MainViewModel GetMainViewModel()
    {
        if (_mainViewModel is null)
        {
            _mainViewModel = new MainViewModel();
            // Extensions are owned per composition root now, so register the Loaded singletons into this
            // MainViewModel's provider; without it the shell cannot resolve editors or tool tabs.
            _mainViewModel.ExtensionProvider.AddExtensions(
                BuiltInExtensionPackageId, LoadPrimitiveExtensionTask.PrimitiveExtensions);
        }

        return _mainViewModel;
    }
}
