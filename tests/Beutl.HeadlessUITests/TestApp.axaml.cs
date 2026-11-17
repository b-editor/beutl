using Avalonia;
using Avalonia.Markup.Xaml;
using Beutl.Extensibility;
using Beutl.Helpers;
using Beutl.Services.StartupTasks;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

public sealed class TestApp : Application
{
    // Mirrors LoadPrimitiveExtensionTask's built-in package slot (LocalPackage.Reserved0, which is internal).
    private const int BuiltInExtensionPackageId = 0;

    private MainViewModel? _mainViewModel;
    internal Beutl.Services.LibraryService Library { get; } = new();
    internal Beutl.Editor.Services.ObjectTemplateService Templates { get; } = new();
    internal Beutl.Services.Tutorials.ITutorialService Tutorials { get; } = new Beutl.Services.Tutorials.TutorialServiceHandler();
    internal Beutl.Controls.PropertyEditors.ColorPaletteService Palettes { get; } = Beutl.Controls.PropertyEditors.ColorPaletteService.CreateDefault();
    internal IServiceProvider Services { get; }

    public TestApp()
    {
        Services = new Beutl.Editor.Services.EditorServiceRegistry()
            .Add(() => Library)
            .Add(() => Templates)
            .Add(() => Tutorials)
            .Add(() => Palettes);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        App.RegisterBundledEngineFonts();
    }

    public override void RegisterServices()
    {
        base.RegisterServices();

        App.RegisterProcessServices(Library, () => GetMainViewModel().ContextCommandManager);
        _ = GetMainViewModel();
        foreach (Extension item in LoadPrimitiveExtensionTask.PrimitiveExtensions)
        {
            item.Load();
        }
    }

    public MainViewModel GetMainViewModel()
    {
        if (_mainViewModel is null)
        {
            _mainViewModel = new MainViewModel(Services, "headless-test-session");

            // ContextCommandManager.Attach is a no-op unless each ViewExtension was registered first;
            // AddExtensions alone wires up resolution but not keyboard/command gestures.
            foreach (Extension item in LoadPrimitiveExtensionTask.PrimitiveExtensions)
            {
                item.Services = _mainViewModel.AppServices;
                if (item is ViewExtension viewExtension)
                {
                    _mainViewModel.ContextCommandManager?.Register(viewExtension);
                }
            }

            // Register the loaded built-in extensions in this MainViewModel's provider; the shell resolves
            // editors and tool tabs through it.
            _mainViewModel.ExtensionProvider.AddExtensions(
                BuiltInExtensionPackageId, LoadPrimitiveExtensionTask.PrimitiveExtensions);
        }

        return _mainViewModel;
    }
}
