#pragma warning disable CS0436

using System.Windows.Input;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;

using Beutl.Api;
using Beutl.Api.Services;

using Beutl.Configuration;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Services.StartupTasks;
using Beutl.ViewModels.ExtensionsPages;

using NuGet.Packaging.Core;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels;

// Todo: StartupやMenuBarViewModelなど複数のクラスに分ける
public sealed class MainViewModel : BasePageViewModel
{
    private readonly ILogger _logger;
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
        _logger = Log.ForContext<MainViewModel>();
        _authorizedHttpClient = new HttpClient();
        _beutlClients = new BeutlApiApplication(_authorizedHttpClient);
        ExtensionProvider.Current = _beutlClients.GetResource<ExtensionProvider>();

        IsProjectOpened = ProjectService.Current.IsOpened;

        IObservable<bool> isProjectOpenedAndTabOpened = ProjectService.Current.IsOpened
            .CombineLatest(EditorService.Current.SelectedTabItem)
            .Select(i => i.First && i.Second != null);

        IObservable<bool> isSceneOpened = EditorService.Current.SelectedTabItem
            .SelectMany(i => i?.Context ?? Observable.Empty<IEditorContext?>())
            .Select(v => v is EditViewModel);

        AddToProject = new(isProjectOpenedAndTabOpened);
        RemoveFromProject = new(isProjectOpenedAndTabOpened);
        AddLayer = new(isSceneOpened);
        DeleteLayer = new(isSceneOpened);
        ExcludeLayer = new(isSceneOpened);
        CutLayer = new(isSceneOpened);
        CopyLayer = new(isSceneOpened);
        PasteLayer = new(isSceneOpened);
        CloseFile = new(EditorService.Current.SelectedTabItem.Select(i => i != null));
        CloseProject = new(IsProjectOpened);
        Save = new(IsProjectOpened);
        SaveAll = new(IsProjectOpened);
        Undo = new(IsProjectOpened);
        Redo = new(IsProjectOpened);

        Save.Subscribe(async () =>
        {
            EditorTabItem? item = EditorService.Current.SelectedTabItem.Value;
            if (item != null)
            {
                try
                {
                    bool result = await (item.Commands.Value == null ? ValueTask.FromResult(false) : item.Commands.Value.OnSave());

                    if (result)
                    {
                        NotificationService.ShowSuccess(string.Empty, string.Format(Message.ItemSaved, item.FileName));
                    }
                    else
                    {
                        Type type = item.Extension.Value.GetType();
                        _logger.Error("{Extension} failed to save file", type.FullName ?? type.Name);
                        NotificationService.ShowInformation(string.Empty, Message.OperationCouldNotBeExecuted);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to save file");
                    NotificationService.ShowError(string.Empty, Message.OperationCouldNotBeExecuted);
                }
            }
        });

        SaveAll.Subscribe(async () =>
        {
            Project? project = ProjectService.Current.CurrentProject.Value;
            int itemsCount = 0;

            try
            {
                project?.Save(project.FileName);
                itemsCount++;

                foreach (EditorTabItem? item in EditorService.Current.TabItems)
                {
                    if (item.Commands.Value != null)
                    {
                        if (await item.Commands.Value.OnSave())
                        {
                            itemsCount++;
                        }
                        else
                        {
                            Type type = item.Extension.Value.GetType();
                            _logger.Error("{Extension} failed to save file", type.FullName ?? type.Name);
                            NotificationService.ShowError(Message.Unable_to_save_file, item.FileName.Value);
                        }
                    }
                }

                NotificationService.ShowSuccess(string.Empty, string.Format(Message.ItemsSaved, itemsCount.ToString()));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save files");
                NotificationService.ShowError(string.Empty, Message.OperationCouldNotBeExecuted);
            }
        });

        CloseFile.Subscribe(() =>
        {
            EditorTabItem? tabItem = EditorService.Current.SelectedTabItem.Value;
            if (tabItem != null)
            {
                EditorService.Current.CloseTabItem(
                    tabItem.FilePath.Value,
                    tabItem.TabOpenMode);
            }
        });
        CloseProject.Subscribe(() => ProjectService.Current.CloseProject());

        Undo.Subscribe(async () =>
        {
            IKnownEditorCommands? commands = EditorService.Current.SelectedTabItem.Value?.Commands.Value;
            if (commands != null)
                await commands.OnUndo();
        });
        Redo.Subscribe(async () =>
        {
            IKnownEditorCommands? commands = EditorService.Current.SelectedTabItem.Value?.Commands.Value;
            if (commands != null)
                await commands.OnRedo();
        });

        Pages = new()
        {
            new(EditPageExtension.Instance),
            new(ExtensionsPageExtension.Instance, new ExtensionsPageViewModel(_beutlClients)),
            new(OutputPageExtension.Instance),
        };
        SettingsPage = new(SettingsPageExtension.Instance, new SettingsPageViewModel(_beutlClients));
        SelectedPage.Value = Pages[0];
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.RecentFiles.ForEachItem(
            item => RecentFileItems.Insert(0, item),
            item => RecentFileItems.Remove(item),
            () => RecentFileItems.Clear());

        viewConfig.RecentProjects.ForEachItem(
            item => RecentProjectItems.Insert(0, item),
            item => RecentProjectItems.Remove(item),
            () => RecentProjectItems.Clear());

        OpenRecentFile.Subscribe(file => EditorService.Current.ActivateTabItem(file, TabOpenMode.YourSelf));

        OpenRecentProject.Subscribe(file =>
        {
            if (!File.Exists(file))
            {
                NotificationService.ShowInformation("", Message.FileDoesNotExist);
            }
            else if (ProjectService.Current.OpenProject(file) == null)
            {
                NotificationService.ShowInformation("", Message.CouldNotOpenProject);
            }
        });

        KeyBindings = CreateKeyBindings();
    }

    public bool IsDebuggerAttached { get; } = Debugger.IsAttached;

    // File
    //    Create new
    //       Project
    //       File
    //    Open
    //       Project
    //       File
    //    Close
    //    Close project
    //    Save
    //    Save all
    //    Recent files
    //    Recent projects
    //    Exit
    public ReactiveCommand CreateNewProject { get; } = new();

    public ReactiveCommand CreateNew { get; } = new();

    public ReactiveCommand OpenProject { get; } = new();

    public ReactiveCommand OpenFile { get; } = new();

    public ReactiveCommand CloseFile { get; }

    public ReactiveCommand CloseProject { get; }

    public ReactiveCommand Save { get; }

    public ReactiveCommand SaveAll { get; }

    public ReactiveCommand<string> OpenRecentFile { get; } = new();

    public ReactiveCommand<string> OpenRecentProject { get; } = new();

    public CoreList<string> RecentFileItems { get; } = new();

    public CoreList<string> RecentProjectItems { get; } = new();

    public ReactiveCommand Exit { get; } = new();

    // Edit
    //    Undo
    //    Redo
    public ReactiveCommand Undo { get; }

    public ReactiveCommand Redo { get; }

    // Project
    //    Add
    //    Remove
    public ReactiveCommand AddToProject { get; }

    public ReactiveCommand RemoveFromProject { get; }

    // Scene
    //    New
    //    Settings
    //    Layer
    //       Add
    //       Delete
    //       Exclude
    //       Cut
    //       Copy
    //       Paste
    public ReactiveCommand NewScene { get; } = new();

    public ReactiveCommand AddLayer { get; }

    public ReactiveCommand DeleteLayer { get; }

    public ReactiveCommand ExcludeLayer { get; }

    public ReactiveCommand CutLayer { get; }

    public ReactiveCommand CopyLayer { get; }

    public ReactiveCommand PasteLayer { get; }

    public NavItemViewModel SettingsPage { get; }

    public CoreList<NavItemViewModel> Pages { get; }

    public List<KeyBinding> KeyBindings { get; }

    public ReactiveProperty<NavItemViewModel?> SelectedPage { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }

    public Task RunStartupTask()
    {
        return new Startup(_beutlClients, this).Run();
    }

    public async ValueTask<CheckForUpdatesResponse?> CheckForUpdates()
    {
        try
        {
            return await _beutlClients.App.CheckForUpdatesAsync(GitVersionInformation.NuGetVersion);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "An error occurred while checking for updates");
            ErrorHandle(ex);
            return null;
        }
    }

    public ToolTabExtension[] GetToolTabExtensions()
    {
        return _beutlClients.GetResource<ExtensionProvider>().GetExtensions<ToolTabExtension>();
    }

    public EditorExtension[] GetEditorExtensions()
    {
        return _beutlClients.GetResource<ExtensionProvider>().GetExtensions<EditorExtension>();
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
            KeyBinding(Key.N, modifier | KeyModifiers.Shift, CreateNewProject),
            // CreateNew: Ctrl+N
            KeyBinding(Key.N, modifier, CreateNew),
            // OpenProject: Ctrl+Shift+O
            KeyBinding(Key.O, modifier | KeyModifiers.Shift, OpenProject),
            // OpenFile: Ctrl+O
            KeyBinding(Key.O, modifier, OpenFile),
            // Save: Ctrl+S
            KeyBinding(Key.S, modifier, Save),
            // SaveAll: Ctrl+Shift+S
            KeyBinding(Key.S, modifier | KeyModifiers.Shift, SaveAll),
            // Exit: Alt+F4
            KeyBinding(Key.F4, KeyModifiers.Alt, Exit),
        };

        if (config != null)
        {
            list.AddRange(config.Undo.Select(x => KeyBinding(x.Key, x.KeyModifiers, Undo)));
            list.AddRange(config.Redo.Select(x => KeyBinding(x.Key, x.KeyModifiers, Redo)));
        }
        else
        {
            list.Add(KeyBinding(Key.Z, modifier, Undo));
            list.Add(KeyBinding(Key.R, modifier, Redo));
        }

        return list;
    }
}
