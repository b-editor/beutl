using System.Reactive.Linq;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Media;
using Avalonia.Threading;

using BeUtl.Collections;
using BeUtl.Configuration;
using BeUtl.Controls;
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

    public sealed class NavItemViewModel
    {
        public NavItemViewModel(PageExtension extension)
        {
            Extension = extension;
            Context = Activator.CreateInstance(extension.Context) ?? throw new Exception("コンテキストを作成できませんでした。");
            Header = extension.Header.GetResourceObservable()
                .Select(o => o ?? string.Empty)
                .ToReadOnlyReactivePropertySlim(string.Empty);
        }
        
        public NavItemViewModel(PageExtension extension, object context)
        {
            Extension = extension;
            Context = context;
            Header = extension.Header.GetResourceObservable()
                .Select(o => o ?? string.Empty)
                .ToReadOnlyReactivePropertySlim(string.Empty);
        }

        public ReadOnlyReactivePropertySlim<string> Header { get; }

        public PageExtension Extension { get; }

        public object Context { get; }
    }

    public MainViewModel()
    {
        _projectService = ServiceLocator.Current.GetRequiredService<IProjectService>();

        IsProjectOpened = _projectService.IsOpened;

        IObservable<bool> isProjectOpenedAndTabOpened = _projectService.IsOpened
            .CombineLatest(EditPageContext.SelectedTabItem)
            .Select(i => i.First && i.Second != null);
        IObservable<bool> isSceneOpened = EditPageContext.SelectedTabItem.Select(i => i?.Context is EditViewModel);

        AddToProject = new(isProjectOpenedAndTabOpened);
        RemoveFromProject = new(isProjectOpenedAndTabOpened);
        AddLayer = new(isSceneOpened);
        DeleteLayer = new(isSceneOpened);
        ExcludeLayer = new(isSceneOpened);
        CutLayer = new(isSceneOpened);
        CopyLayer = new(isSceneOpened);
        PasteLayer = new(isSceneOpened);
        CloseFile = new(EditPageContext.SelectedTabItem.Select(i => i != null));
        CloseProject = new(_projectService.IsOpened);

        CloseFile.Subscribe(() =>
        {
            if (EditPageContext.SelectedTabItem.Value != null)
            {
                EditPageContext.CloseTabItem(
                    EditPageContext.SelectedTabItem.Value.FilePath,
                    EditPageContext.SelectedTabItem.Value.TabOpenMode);
            }
        });
        CloseProject.Subscribe(() => _projectService.CloseProject());

        _packageLoadTask = Task.Run(async () =>
        {
            PackageManager manager = PackageManager.Instance;
            int id1 = Package.s_nextId++;
            int id2 = Package.s_nextId++;

            manager.LoadPackages(manager.GetPackageInfos());

            manager.ExtensionProvider._allExtensions.Add(id1, new Extension[]
            {
                EditPageExtension.Instance,
                ExtensionPageExtension.Instance,
                OutputPageExtension.Instance,
                SettingsPageExtension.Instance,
            });

            // Todo: ここでSceneEditorExtensionを登録しているので、
            //       パッケージとして分離する場合ここを削除
            manager.ExtensionProvider._allExtensions.Add(id2, new Extension[]
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
            new(EditPageExtension.Instance, EditPageContext),
            new(ExtensionPageExtension.Instance),
            new(OutputPageExtension.Instance),
        };
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

        OpenRecentFile.Subscribe(file => EditPageContext.SelectOrAddTabItem(file, EditPageViewModel.TabOpenMode.YourSelf));

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

        ServiceLocator.Current.BindToSelf(EditPageContext);
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

    // Todo: EditPageViewModelに依存しないサービスを作る
    //[Obsolete("Use EditorService")]
    public EditPageViewModel EditPageContext { get; } = new();

    public NavItemViewModel SettingsPage { get; } = new(SettingsPageExtension.Instance);

    public CoreList<NavItemViewModel> Pages { get; }

    public ReactiveProperty<NavItemViewModel?> SelectedPage { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }
}
