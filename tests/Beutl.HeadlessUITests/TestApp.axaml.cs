using Avalonia;
using Avalonia.Markup.Xaml;
using Beutl.Configuration;
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
    internal Beutl.Media.FontManager Fonts { get; }
    internal NotificationCapture Notifications { get; } = NotificationCapture.CreateForwardingRoot();
    internal GlobalConfiguration Configuration { get; } = new();
    internal Beutl.Services.LibraryService Library { get; } = new();
    internal Beutl.Editor.Services.ObjectTemplateService Templates { get; }
    internal Beutl.Services.Tutorials.ITutorialService Tutorials { get; }
    internal Beutl.Controls.PropertyEditors.ColorPaletteService Palettes { get; } = Beutl.Controls.PropertyEditors.ColorPaletteService.CreateDefault();
    internal IServiceProvider Services { get; }

    public TestApp()
    {
        Tutorials = new Beutl.Services.Tutorials.TutorialServiceHandler(Configuration.TutorialConfig);
        Beutl.Media.Decoding.DecoderRegistry.Initialize(Configuration.ExtensionConfig);
        Fonts = new Beutl.Media.FontManager(Configuration.FontConfig);
        Templates = new(Fonts, Configuration.EditorConfig, Configuration.ProxyStoreConfig);
        Services = new Beutl.Editor.Services.EditorServiceRegistry()
            .Add(() => Configuration)
            .Add(() => Fonts)
            .Add<Beutl.Services.INotificationService>(() => Notifications)
            .AddOwned(() => new Beutl.Services.AppUpdateController(Configuration.UpdateConfig, Notifications))
            .Add(() => Library)
            .Add(() => Templates)
            .Add(() => Tutorials)
            .Add(() => Palettes);
    }

    public override void Initialize()
    {
        Beutl.Testing.Headless.ControlHost.ConfigureWindow = window =>
        {
            if (Beutl.Controls.ControlNotifications.GetNotifications(window) is null)
                Beutl.Controls.ControlNotifications.SetNotifications(window, Notifications);
            if (Beutl.Controls.ControlFonts.GetFonts(window) is null)
                Beutl.Controls.ControlFonts.SetFonts(window, Fonts);
            if (Beutl.Controls.ControlConfiguration.GetConfiguration(window) is null)
                Beutl.Controls.ControlConfiguration.SetConfiguration(window, Configuration);
        };
        AvaloniaXamlLoader.Load(this);
        App.RegisterBundledEngineFonts(Fonts);
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
