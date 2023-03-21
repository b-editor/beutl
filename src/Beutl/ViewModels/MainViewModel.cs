using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using Beutl.Api;
using Beutl.Api.Services;

using Beutl.Configuration;
using Beutl.Framework;
using Beutl.Framework.Service;
using Beutl.Framework.Services;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.ExtensionsPages;

using DynamicData;

using Microsoft.Extensions.DependencyInjection;

using NuGet.Packaging.Core;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class MainViewModel : BasePageViewModel
{
    private readonly IProjectService _projectService;
    private readonly EditorService _editorService;
    private readonly PageExtension[] _primitivePageExtensions;
    private readonly BeutlApiApplication _beutlClients;
    private readonly HttpClient _authorizedHttpClient;

    public sealed class NavItemViewModel
    {
        public NavItemViewModel(PageExtension extension)
        {
            Extension = extension;
            Context = extension.CreateContext() ?? throw new Exception("コンテキストを作成できませんでした。");
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

        _projectService = ServiceLocator.Current.GetRequiredService<IProjectService>();
        _editorService = ServiceLocator.Current.GetRequiredService<EditorService>();
        _primitivePageExtensions = new PageExtension[]
        {
            EditPageExtension.Instance,
            ExtensionsPageExtension.Instance,
            OutputPageExtension.Instance,
            SettingsPageExtension.Instance,
        };

        IsProjectOpened = _projectService.IsOpened;

        IObservable<bool> isProjectOpenedAndTabOpened = _projectService.IsOpened
            .CombineLatest(_editorService.SelectedTabItem)
            .Select(i => i.First && i.Second != null);

        IObservable<bool> isSceneOpened = _editorService.SelectedTabItem
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
        CloseFile = new(_editorService.SelectedTabItem.Select(i => i != null));
        CloseProject = new(_projectService.IsOpened);
        Save = new(_projectService.IsOpened);
        SaveAll = new(_projectService.IsOpened);
        Undo = new(_projectService.IsOpened);
        Redo = new(_projectService.IsOpened);

        Save.Subscribe(async () =>
        {
            EditorTabItem? item = _editorService.SelectedTabItem.Value;
            if (item != null)
            {
                try
                {
                    bool result = await (item.Commands.Value == null ? ValueTask.FromResult(false) : item.Commands.Value.OnSave());

                    if (result)
                    {
                        Notification.Show(new Notification(
                            string.Empty,
                            string.Format(Message.ItemSaved, item.FileName),
                            NotificationType.Success));
                    }
                    else
                    {
                        Notification.Show(new Notification(
                            string.Empty,
                            Message.OperationCouldNotBeExecuted,
                            NotificationType.Information));
                    }
                }
                catch
                {
                    Notification.Show(new Notification(
                        string.Empty,
                        Message.OperationCouldNotBeExecuted,
                        NotificationType.Error));
                }
            }
        });

        SaveAll.Subscribe(async () =>
        {
            Project? project = _projectService.CurrentProject.Value;
            int itemsCount = 0;

            try
            {
                project?.Save(project.FileName);
                itemsCount++;

                foreach (EditorTabItem? item in _editorService.TabItems)
                {
                    if (item.Commands.Value != null
                        && await item.Commands.Value.OnSave())
                    {
                        itemsCount++;
                    }
                }

                Notification.Show(new Notification(
                    string.Empty,
                    string.Format(Message.ItemsSaved, itemsCount.ToString()),
                    NotificationType.Success));
            }
            catch
            {
                Notification.Show(new Notification(
                    string.Empty,
                    Message.OperationCouldNotBeExecuted,
                    NotificationType.Error));
            }
        });

        CloseFile.Subscribe(() =>
        {
            EditorTabItem? tabItem = _editorService.SelectedTabItem.Value;
            if (tabItem != null)
            {
                _editorService.CloseTabItem(
                    tabItem.FilePath.Value,
                    tabItem.TabOpenMode);
            }
        });
        CloseProject.Subscribe(() => _projectService.CloseProject());

        Undo.Subscribe(async () =>
        {
            IKnownEditorCommands? commands = _editorService.SelectedTabItem.Value?.Commands.Value;
            if (commands != null)
                await commands.OnUndo();
        });
        Redo.Subscribe(async () =>
        {
            IKnownEditorCommands? commands = _editorService.SelectedTabItem.Value?.Commands.Value;
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

        OpenRecentFile.Subscribe(file => _editorService.ActivateTabItem(file, TabOpenMode.YourSelf));

        OpenRecentProject.Subscribe(file =>
        {
            IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
            INotificationService noticeService = ServiceLocator.Current.GetRequiredService<INotificationService>();

            if (!File.Exists(file))
            {
                noticeService.Show(new Notification(
                    Title: "",
                    Message: Message.FileDoesNotExist));
            }
            else if (service.OpenProject(file) == null)
            {
                noticeService.Show(new Notification(
                    Title: "",
                    Message: Message.CouldNotOpenProject));
            }
        });
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

    public ReactiveProperty<NavItemViewModel?> SelectedPage { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }

    public Task RunSplachScreenTask(Func<IReadOnlyList<LocalPackage>, Task<bool>> showDialog)
    {
        return Task.Run(async () =>
        {
            Task authTask = _beutlClients.RestoreUserAsync();

            PackageManager manager = _beutlClients.GetResource<PackageManager>();
            ExtensionProvider provider = _beutlClients.GetResource<ExtensionProvider>();

            provider._allExtensions.Add(LocalPackage.s_nextId++, _primitivePageExtensions);

            // NOTE: ここでSceneEditorExtensionを登録しているので、
            //       パッケージとして分離する場合ここを削除
            provider._allExtensions.Add(LocalPackage.s_nextId++, new Extension[]
            {
                SceneEditorExtension.Instance,
                SceneOutputExtension.Instance,
                SceneProjectItemExtension.Instance,
                TimelineTabExtension.Instance,
                ObjectPropertyTabExtension.Instance,
                StyleEditorTabExtension.Instance,
                SourceOperatorsTabExtension.Instance,
                PropertyEditorExtension.Instance,
                NodeTreeTabExtension.Instance,
                NodeTreeInputTabExtension.Instance,
                GraphEditorTabExtension.Instance,
            });

            foreach (LocalPackage item in await manager.GetPackages())
            {
                manager.Load(item);
            }

            if (manager.GetSideLoadPackages() is { Count: > 0 } sideloads
                && await showDialog(sideloads))
            {
                foreach (LocalPackage item in sideloads)
                {
                    manager.Load(item);
                }
            }

            IEnumerable<PageExtension> toAdd
                = provider.AllExtensions.OfType<PageExtension>().Except(_primitivePageExtensions);

            NavItemViewModel[] viewModels = toAdd.Select(item => new NavItemViewModel(item)).ToArray();
            _ = Dispatcher.UIThread.InvokeAsync(() => Pages.AddRange(viewModels.AsSpan()), DispatcherPriority.Background);

            try
            {
                await authTask;
            }
            catch (Exception e)
            {
                ErrorHandle(e);
            }
        });
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
        ServiceLocator.Current
            .Bind<ExtensionProvider>().ToLazy(_beutlClients.GetResource<ExtensionProvider>);

        if (Application.Current is { ApplicationLifetime: IControlledApplicationLifetime lifetime })
        {
            lifetime.Exit += OnExit;
        }
    }

    public override void Dispose()
    {
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        PackageChangesQueue queue = _beutlClients.GetResource<PackageChangesQueue>();
        PackageIdentity[] installs = queue.GetInstalls().ToArray();
        PackageIdentity[] uninstalls = queue.GetUninstalls().ToArray();

        if (installs.Length > 0 || uninstalls.Length > 0)
        {
            var startInfo = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "bpt"))
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

            Process.Start(startInfo);
        }
    }
}
