using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Helpers;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Services.StartupTasks;
using Beutl.ViewModels.ExtensionsPages;
using DynamicData;
using DynamicData.Binding;
using NuGet.Packaging.Core;
using Reactive.Bindings;
using ReactiveUI;

namespace Beutl.ViewModels;

public sealed class MainViewModel : BasePageViewModel
{
    internal readonly BeutlApiApplication _beutlClients;
    private readonly HttpClient _authorizedHttpClient;

    public MainViewModel()
    {
        _authorizedHttpClient = new HttpClient();
        _beutlClients = new BeutlApiApplication(_authorizedHttpClient);
        SettingsPage = new SettingsPageViewModel(_beutlClients);

        MenuBar = new MenuBarViewModel();

        IsProjectOpened = ProjectService.Current.IsOpened;
        NameOfOpenProject = ProjectService.Current.CurrentProject.Select(v => Path.GetFileName(v?.FileName))
            .ToReadOnlyReactivePropertySlim();
        WindowTitle = NameOfOpenProject.Select(v => string.IsNullOrWhiteSpace(v) ? "Beutl" : $"Beutl - {v}")
            .ToReadOnlyReactivePropertySlim("Beutl");
        TitleBreadcrumbBar = new TitleBreadcrumbBarViewModel(this, EditorService.Current);

        KeyBindings = CreateKeyBindings();

        ICoreReadOnlyList<Extension> allExtension = ExtensionProvider.Current.AllExtensions;

        var comparer = SortExpressionComparer<Extension>.Ascending(i => i.Name);
        IObservable<IChangeSet<Extension>> changeSet = allExtension
            .ToObservableChangeSet<ICoreReadOnlyList<Extension>, Extension>()
            .Sort(comparer);

        changeSet.Filter(i => i is ToolTabExtension)
            .Cast(item => (ToolTabExtension)item)
            .Bind(out ReadOnlyObservableCollection<ToolTabExtension>? list1)
            .Subscribe();

        changeSet.Filter(i => i is EditorExtension)
            .Cast(item => (EditorExtension)item)
            .Bind(out ReadOnlyObservableCollection<EditorExtension>? list2)
            .Subscribe();

        changeSet.Filter(i => i is PageExtension)
            .Cast(item => (PageExtension)item)
            .Bind(out ReadOnlyObservableCollection<PageExtension>? list3)
            .Subscribe();

        ToolTabExtensions = list1;
        EditorExtensions = list2;
        PageExtensions = list3;
    }

    public bool IsDebuggerAttached { get; } = Debugger.IsAttached;

    public ReactivePropertySlim<bool> IsRunningStartupTasks { get; } = new();

    public ReadOnlyReactivePropertySlim<string?> NameOfOpenProject { get; }

    public ReadOnlyReactivePropertySlim<string> WindowTitle { get; }

    public MenuBarViewModel MenuBar { get; }

    public TitleBreadcrumbBarViewModel TitleBreadcrumbBar { get; }

    public List<KeyBinding> KeyBindings { get; }

    public EditorHostViewModel EditorHost { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }

    public ReadOnlyObservableCollection<ToolTabExtension> ToolTabExtensions { get; }

    public ReadOnlyObservableCollection<EditorExtension> EditorExtensions { get; }

    public ReadOnlyObservableCollection<PageExtension> PageExtensions { get; }

    public SettingsPageViewModel SettingsPage { get; }

    public Startup RunStartupTask()
    {
        IsRunningStartupTasks.Value = true;
        var startup = new Startup(_beutlClients);
        startup.WaitAll().ContinueWith(_ => IsRunningStartupTasks.Value = false);

        return startup;
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
            var startInfo = new ProcessStartInfo() { UseShellExecute = true, };
            DotNetProcess.Configure(startInfo, Path.Combine(AppContext.BaseDirectory, "Beutl.PackageTools.UI"));

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

            startInfo.ArgumentList.AddRange(["--session-id", Telemetry.Instance._sessionId]);

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
            return new KeyBinding { Gesture = new KeyGesture(key, modifiers), Command = command };
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
