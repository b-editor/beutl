using System.CodeDom.Compiler;
using System.Reflection;

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

using Beutl.Api;
using Beutl.Api.Services;

using Beutl.Configuration;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.ExtensionsPages;

using DynamicData;

using Microsoft.Extensions.DependencyInjection;

using NuGet.Packaging.Core;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels;

// Todo: StartupやMenuBarViewModelなど複数のクラスに分ける
public sealed class MainViewModel : BasePageViewModel
{
    private readonly ILogger _logger;
    private readonly ProjectService _projectService;
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
        _logger = Log.ForContext<MainViewModel>();
        _authorizedHttpClient = new HttpClient();
        _beutlClients = new BeutlApiApplication(_authorizedHttpClient);

        _projectService = ServiceLocator.Current.GetRequiredService<ProjectService>();
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
                        Type type = item.Extension.Value.GetType();
                        _logger.Error("{Extension} failed to save file", type.FullName ?? type.Name);
                        Notification.Show(new Notification(
                            string.Empty,
                            Message.OperationCouldNotBeExecuted,
                            NotificationType.Information));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to save file");
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
                            Notification.Show(new Notification(
                                "ファイルを保存できません",
                                item.FileName.Value,
                                NotificationType.Error));
                        }
                    }
                }

                Notification.Show(new Notification(
                    string.Empty,
                    string.Format(Message.ItemsSaved, itemsCount.ToString()),
                    NotificationType.Success));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save files");
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
            ProjectService service = ServiceLocator.Current.GetRequiredService<ProjectService>();
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

    private void LoadPrimitiveExtensions(ExtensionProvider provider, IList<(LocalPackage, Exception)> failures)
    {
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
            SourceOperatorsTabExtension.Instance,
            PropertyEditorExtension.Instance,
            NodeTreeTabExtension.Instance,
            NodeTreeInputTabExtension.Instance,
            GraphEditorTabExtension.Instance,
        });

#if FFMPEG_BUILD_IN
        // Beutl.Extensions.FFmpeg.csproj
        var pkg = new LocalPackage
        {
            ShortDescription = "FFmpeg for beutl",
            Name = "Beutl.Embedding.FFmpeg",
            DisplayName = "Beutl.Embedding.FFmpeg",
            InstalledPath = AppContext.BaseDirectory,
            Tags = { "ffmpeg", "decoder", "decoding", "encoder", "encoding", "video", "audio" },
            Version = "1.0.0",
            WebSite = "https://github.com/b-editor/beutl",
            Publisher = "b-editor"
        };
        try
        {
            var decoding = new Embedding.FFmpeg.Decoding.FFmpegDecodingExtension();
            var encoding = new Embedding.FFmpeg.Encoding.FFmpegEncodingExtension();
            decoding.Load();
            encoding.Load();

            provider._allExtensions.Add(pkg.LocalId, new Extension[]
            {
                decoding,
                encoding
            });
        }
        catch (Exception ex)
        {
            failures.Add((pkg, ex));
        }
#endif
    }

    private void LoadLocalPackages(PackageManager manager, IReadOnlyList<LocalPackage> packages, IList<(LocalPackage, Exception)> failures)
    {
        foreach (LocalPackage item in packages)
        {
            try
            {
                manager.Load(item);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to load package");
                failures.Add((item, e));
            }
        }
    }

    private void InitializePages(ExtensionProvider provider)
    {
        IEnumerable<PageExtension> toAdd
            = provider.AllExtensions.OfType<PageExtension>().Except(_primitivePageExtensions);

        NavItemViewModel[] viewModels = toAdd.Select(item => new NavItemViewModel(item)).ToArray();
        _ = Dispatcher.UIThread.InvokeAsync(() => Pages.AddRange(viewModels.AsSpan()), DispatcherPriority.Background);
    }

    public Task RunSplachScreenTask(Func<IReadOnlyList<LocalPackage>, Task<bool>> showDialog)
    {
        return Task.Run(async () =>
        {
            Task authTask = _beutlClients.RestoreUserAsync();

            PackageManager manager = _beutlClients.GetResource<PackageManager>();
            ExtensionProvider provider = _beutlClients.GetResource<ExtensionProvider>();
            var failures = new List<(LocalPackage, Exception)>();

            LoadPrimitiveExtensions(provider, failures);
            // .beutl/packages/ 内のパッケージを読み込む
            LoadLocalPackages(manager, await manager.GetPackages(), failures);

            // .beutl/sideloads/ 内のパッケージを読み込む
            if (manager.GetSideLoadPackages() is { Count: > 0 } sideloads
                && await showDialog(sideloads))
            {
                LoadLocalPackages(manager, sideloads, failures);
            }

            if (failures.Count > 0)
            {
                Notification.Show(new Notification(
                    "パッケージの読み込みに失敗しました。",
                    $"{failures.Count}件のパッケージの読み込みに失敗しました",
                    NotificationType.Error,
                    OnActionButtonClick: () => ShowPackageLoadingError(failures),
                    ActionButtonText: "詳細"));
            }

            InitializePages(provider);

            try
            {
                await authTask;
            }
            catch (Exception e)
            {
                _logger.Error(e, "An error occurred during authentication");
                ErrorHandle(e);
            }
        });
    }

    // ユーザー向けのテキストファイルを生成して、デフォルトのテキストエディタで表示する。
    private static async void ShowPackageLoadingError(IReadOnlyList<(LocalPackage, Exception)> failures)
    {
        string file = Path.GetTempFileName();
        file = Path.ChangeExtension(file, ".txt");

        using (StreamWriter baseWriter = File.CreateText(file))
        using (var writer = new IndentedTextWriter(baseWriter, "  "))
        {
            baseWriter.AutoFlush = false;
            writer.WriteLine($"{failures.Count}件のパッケージの読み込みに失敗しました。\n");
            foreach ((LocalPackage pkg, Exception ex) in failures)
            {
                writer.WriteLine("Package:");
                writer.Indent++;
                writer.WriteLine($"Name: '{pkg.Name}'");
                writer.WriteLine($"DisplayName: '{pkg.DisplayName}'");
                writer.WriteLine($"Version: '{pkg.Version}'");
                writer.WriteLine($"Publisher: '{pkg.Publisher}'");
                writer.WriteLine($"WebSite: '{pkg.WebSite}'");
                writer.WriteLine($"Description: '{pkg.Description}");
                writer.WriteLine($"ShortDescription: '{pkg.ShortDescription}'");
                writer.WriteLine($"Tags: '{string.Join(',', pkg.Tags)}'");
                writer.WriteLine($"InstalledPath: '{pkg.InstalledPath}'");
                writer.Indent--;
                writer.WriteLine(ex.ToString());
                writer.WriteLine();
            }

            await writer.FlushAsync();
        }

        Process.Start(new ProcessStartInfo(file)
        {
            UseShellExecute = true,
            Verb = "open"
        });
    }

    public async ValueTask<CheckForUpdatesResponse?> CheckForUpdates()
    {
        try
        {
            AssemblyName asmName = typeof(MainViewModel).Assembly.GetName();
            if (asmName is { Version: Version version })
            {
                string versionStr = version.ToString();
                return await _beutlClients.App.CheckForUpdatesAsync(versionStr);
            }
            else
            {
                return null;
            }
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
        ServiceLocator.Current
            .Bind<ExtensionProvider>().ToLazy(_beutlClients.GetResource<ExtensionProvider>);

        if (Application.Current is { ApplicationLifetime: IControlledApplicationLifetime lifetime })
        {
            lifetime.Exit += OnExit;
        }
    }

    public override void Dispose()
    {
        _projectService.CloseProject();
        BeutlApplication.Current.Items.Clear();
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
