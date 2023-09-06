using System.Windows.Input;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;

using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Services.StartupTasks;
using Beutl.ViewModels.ExtensionsPages;

using NuGet.Packaging.Core;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels;

public sealed class MainViewModel : BasePageViewModel
{
    private readonly BeutlApiApplication _beutlClients;
    private readonly HttpClient _authorizedHttpClient;

    public sealed class NavItemViewModel
    {
        public NavItemViewModel(PageExtension extension)
        {
            Extension = extension;
            Context = extension.CreateContext() ?? throw new Exception("Could not create context.");
        }

        public NavItemViewModel(PageExtension extension, IPageContext context)
        {
            Extension = extension;
            Context = context;
        }

        public PageExtension Extension { get; }

        public IPageContext Context { get; }
    }

    public MainViewModel()
    {
        _authorizedHttpClient = new HttpClient();
        _beutlClients = new BeutlApiApplication(_authorizedHttpClient);
        ExtensionProvider.Current = _beutlClients.GetResource<ExtensionProvider>();
        MenuBar = new MenuBarViewModel();

        IsProjectOpened = ProjectService.Current.IsOpened;
        NameOfOpenProject = ProjectService.Current.CurrentProject.Select(v => Path.GetFileName(v?.FileName))
            .ToReadOnlyReactivePropertySlim();
        WindowTitle = NameOfOpenProject.Select(v => string.IsNullOrWhiteSpace(v) ? "Beutl" : $"Beutl - {v}")
            .ToReadOnlyReactivePropertySlim("Beutl");

        IObservable<bool> isProjectOpenedAndTabOpened = ProjectService.Current.IsOpened
            .CombineLatest(EditorService.Current.SelectedTabItem)
            .Select(i => i.First && i.Second != null);

        IObservable<bool> isSceneOpened = EditorService.Current.SelectedTabItem
            .SelectMany(i => i?.Context ?? Observable.Empty<IEditorContext?>())
            .Select(v => v is EditViewModel);

        Pages = new()
        {
            new(EditPageExtension.Instance),
            new(ExtensionsPageExtension.Instance, new ExtensionsPageViewModel(_beutlClients)),
            new(OutputPageExtension.Instance),
        };
        SettingsPage = new(SettingsPageExtension.Instance, new SettingsPageViewModel(_beutlClients));
        SelectedPage.Value = Pages[0];

        KeyBindings = CreateKeyBindings();
    }

    public bool IsDebuggerAttached { get; } = Debugger.IsAttached;

    public ReadOnlyReactivePropertySlim<string?> NameOfOpenProject { get; }
    
    public ReadOnlyReactivePropertySlim<string> WindowTitle { get; }

    public MenuBarViewModel MenuBar { get; }

    public NavItemViewModel SettingsPage { get; }

    public CoreList<NavItemViewModel> Pages { get; }

    public List<KeyBinding> KeyBindings { get; }

    public ReactiveProperty<NavItemViewModel?> SelectedPage { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }

    public Task RunStartupTask()
    {
        return new Startup(_beutlClients, this).Run();
    }

    public void RegisterServices()
    {
        if (Application.Current is { ApplicationLifetime: IControlledApplicationLifetime lifetime })
        {
            lifetime.Exit += OnExit;
        }
    }

    public override void Dispose()
    {
        ProjectService.Current.CloseProject();
        BeutlApplication.Current.Items.Clear();
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        PackageChangesQueue queue = _beutlClients.GetResource<PackageChangesQueue>();
        PackageIdentity[] installs = queue.GetInstalls().ToArray();
        PackageIdentity[] uninstalls = queue.GetUninstalls().ToArray();

        if (installs.Length > 0 || uninstalls.Length > 0)
        {
            const string ExeName = "Beutl.PackageTools";
            var startInfo = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? $"{ExeName}.exe" : ExeName))
            {
                UseShellExecute = true,
            };

            if (installs.Length > 0)
            {
                startInfo.ArgumentList.Add("--installs");
                foreach (PackageIdentity? item in installs)
                {
                    startInfo.ArgumentList.Add(item.HasVersion ? $"{item.Id}/{item.Version}" : item.Id);
                }
            }

            if (uninstalls.Length > 0)
            {
                startInfo.ArgumentList.Add("--uninstalls");
                foreach (PackageIdentity? item in uninstalls)
                {
                    startInfo.ArgumentList.Add(item.HasVersion ? $"{item.Id}/{item.Version}" : item.Id);
                }
            }

            startInfo.ArgumentList.Add("--verbose");
            startInfo.ArgumentList.Add("--stay-open");

            if (Debugger.IsAttached)
                startInfo.ArgumentList.Add("--launch-debugger");

            Process.Start(startInfo);
        }
    }

    // Todo: 設定からショートカットを変更できるようにする。
    private List<KeyBinding> CreateKeyBindings()
    {
        static KeyBinding KeyBinding(Key key, KeyModifiers modifiers, ICommand command)
        {
            return new KeyBinding
            {
                Gesture = new KeyGesture(key, modifiers),
                Command = command
            };
        }

        PlatformHotkeyConfiguration? config = Application.Current?.PlatformSettings?.HotkeyConfiguration;
        KeyModifiers modifier = config?.CommandModifiers ?? KeyModifiers.Control;
        var list = new List<KeyBinding>
        {
            // CreateNewProject: Ctrl+Shift+N
            KeyBinding(Key.N, modifier | KeyModifiers.Shift, MenuBar.CreateNewProject),
            // CreateNew: Ctrl+N
            KeyBinding(Key.N, modifier, MenuBar.CreateNew),
            // OpenProject: Ctrl+Shift+O
            KeyBinding(Key.O, modifier | KeyModifiers.Shift, MenuBar.OpenProject),
            // OpenFile: Ctrl+O
            KeyBinding(Key.O, modifier, MenuBar.OpenFile),
            // Save: Ctrl+S
            KeyBinding(Key.S, modifier, MenuBar.Save),
            // SaveAll: Ctrl+Shift+S
            KeyBinding(Key.S, modifier | KeyModifiers.Shift, MenuBar.SaveAll),
            // Exit: Alt+F4
            KeyBinding(Key.F4, KeyModifiers.Alt, MenuBar.Exit),
        };

        if (config != null)
        {
            list.AddRange(config.Undo.Select(x => KeyBinding(x.Key, x.KeyModifiers, MenuBar.Undo)));
            list.AddRange(config.Redo.Select(x => KeyBinding(x.Key, x.KeyModifiers, MenuBar.Redo)));
        }
        else
        {
            list.Add(KeyBinding(Key.Z, modifier, MenuBar.Undo));
            list.Add(KeyBinding(Key.R, modifier, MenuBar.Redo));
        }

        return list;
    }
}
