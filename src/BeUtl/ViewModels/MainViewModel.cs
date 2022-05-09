using System.Reactive.Linq;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Threading;

using BeUtl.Collections;
using BeUtl.Configuration;
using BeUtl.Framework;
using BeUtl.Framework.Service;
using BeUtl.Framework.Services;
using BeUtl.Services;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BeUtl.ViewModels;

public class MainViewModel
{
    private readonly IProjectService _projectService;
    internal readonly Task _packageLoadTask;

    public MainViewModel()
    {
        _projectService = ServiceLocator.Current.GetRequiredService<IProjectService>();

        IsProjectOpened = _projectService.IsOpened;

        IObservable<bool> isProjectOpenedAndTabOpened = _projectService.IsOpened
            .CombineLatest(EditPage.SelectedTabItem)
            .Select(i => i.First && i.Second != null);
        IObservable<bool> isSceneOpened = EditPage.SelectedTabItem.Select(i => i?.Context is EditViewModel);

        AddToProject = new(isProjectOpenedAndTabOpened);
        RemoveFromProject = new(isProjectOpenedAndTabOpened);
        AddLayer = new(isSceneOpened);
        DeleteLayer = new(isSceneOpened);
        ExcludeLayer = new(isSceneOpened);
        CutLayer = new(isSceneOpened);
        CopyLayer = new(isSceneOpened);
        PasteLayer = new(isSceneOpened);
        CloseFile = new(EditPage.SelectedTabItem.Select(i => i != null));
        CloseProject = new(_projectService.IsOpened);

        CloseFile.Subscribe(() =>
        {
            if (EditPage.SelectedTabItem.Value != null)
            {
                EditPage.CloseTabItem(
                    EditPage.SelectedTabItem.Value.FilePath,
                    EditPage.SelectedTabItem.Value.TabOpenMode);
            }
        });
        CloseProject.Subscribe(() => _projectService.CloseProject());

        _packageLoadTask = Task.Run(async () =>
        {
            PackageManager manager = PackageManager.Instance;
            manager.LoadPackages(manager.GetPackageInfos());

            // Todo: ここでSceneEditorExtensionを登録しているので、
            //       パッケージとして分離する場合ここを削除
            manager.ExtensionProvider._allExtensions.Add(Package.s_nextId++, new Extension[]
            {
                SceneEditorExtension.Instance,
                SceneWorkspaceItemExtension.Instance,
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Application.Current != null)
                {
                    PackageManager.Instance.AttachToApplication(Application.Current);
                }
            });
        });

        Pages = new()
        {
            EditPage,
            ExtensionsPage,
            OutputPage,
            SettingsPage
        };
        SelectedPage.Value = EditPage;
        ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
        viewConfig.RecentFiles.ForEachItem(
            item => RecentFileItems.Insert(0, item),
            item => RecentFileItems.Remove(item),
            () => RecentFileItems.Clear());

        viewConfig.RecentProjects.ForEachItem(
            item => RecentProjectItems.Insert(0, item),
            item => RecentProjectItems.Remove(item),
            () => RecentProjectItems.Clear());

        OpenRecentFile.Subscribe(file => EditPage.SelectOrAddTabItem(file, EditPageViewModel.TabOpenMode.YourSelf));

        OpenRecentProject.Subscribe(file =>
        {
            IProjectService service = ServiceLocator.Current.GetRequiredService<IProjectService>();
            INotificationService noticeService = ServiceLocator.Current.GetRequiredService<INotificationService>();

            if (!File.Exists(file))
            {
                // Todo: リソースに置き換え
                noticeService.Show(new Notification(
                    Title: "",
                    Message: "ファイルが存在しない"));
            }
            else if (service.OpenProject(file) == null)
            {
                // Todo: リソースに置き換え
                noticeService.Show(new Notification(
                    Title: "",
                    Message: "プロジェクトが開けなかった"));
            }
        });
    }

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

    public ReactiveCommand<string> OpenRecentFile { get; } = new();

    public ReactiveCommand<string> OpenRecentProject { get; } = new();

    public CoreList<string> RecentFileItems { get; } = new();

    public CoreList<string> RecentProjectItems { get; } = new();

    public ReactiveCommand Exit { get; } = new();

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

    public EditPageViewModel EditPage { get; } = new();

    public ExtensionsPageViewModel ExtensionsPage { get; } = new();

    public OutputPageViewModel OutputPage { get; } = new();

    public SettingsPageViewModel SettingsPage { get; } = new();

    public CoreList<object?> Pages { get; }

    public ReactiveProperty<object?> SelectedPage { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }
}
