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

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        App.RegisterBundledEngineFonts();
        // EditView's Timeline tool view (LayerHeader) resolves {StaticResource PaletteColors}, which the
        // real App sets in App.Initialize; without it the tool view fails to load once it actually inflates.
        Resources["PaletteColors"] = AppHelpers.GetPaletteColors();
    }

    public override void RegisterServices()
    {
        base.RegisterServices();

        App.RegisterProcessServices(() => GetMainViewModel().ContextCommandManager);
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

            // ContextCommandManager.Attach is a no-op unless each ViewExtension was registered first;
            // AddExtensions alone wires up resolution but not keyboard/command gestures.
            foreach (Extension item in LoadPrimitiveExtensionTask.PrimitiveExtensions)
            {
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
