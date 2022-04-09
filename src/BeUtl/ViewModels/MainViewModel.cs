using Avalonia;
using Avalonia.Threading;

using BeUtl.Framework;
using BeUtl.Framework.Service;
using BeUtl.Framework.Services;
using BeUtl.ProjectSystem;
using BeUtl.Services;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BeUtl.ViewModels;

public class MainViewModel
{
    private readonly IProjectService _projectService;

    public MainViewModel()
    {
        _projectService = ServiceLocator.Current.GetRequiredService<IProjectService>();

        IsProjectOpened = _projectService.IsOpened;

        // プロジェクトが開いている時だけ実行できるコマンド
        Save = new(_projectService.IsOpened);
        SaveAll = new(_projectService.IsOpened);
        OpenScene = new(_projectService.IsOpened);
        AddScene = new(_projectService.IsOpened);
        RemoveScene = new(_projectService.IsOpened);
        CloseFile = new(_projectService.IsOpened);
        CloseProject = new(_projectService.IsOpened);
        Undo = new(_projectService.IsOpened);
        Redo = new(_projectService.IsOpened);

        CloseProject.Subscribe(() => _projectService.CloseProject());

        Undo.Subscribe(async () =>
        {
            bool handled = false;

            if (KnownCommands != null)
                handled = await KnownCommands.OnUndo();

            if (!handled)
                CommandRecorder.Default.Undo();
        });
        Redo.Subscribe(async () =>
        {
            bool handled = false;

            if (KnownCommands != null)
                handled = await KnownCommands.OnRedo();

            if (!handled)
                CommandRecorder.Default.Redo();
        });

        Task.Run(() =>
        {
            PackageManager manager = PackageManager.Instance;
            manager.LoadPackages(manager.GetPackageInfos());

            manager.ExtensionProvider._allExtensions.Add(Package.s_nextId++, new Extension[]
            {
                SceneEditorExtension.Instance
            });

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (Application.Current != null)
                {
                    PackageManager.Instance.AttachToApplication(Application.Current);
                }
            });
        });
    }

    public ReactiveCommand CreateNewProject { get; } = new();

    public ReactiveCommand CreateNew { get; } = new();

    public ReactiveCommand OpenProject { get; } = new();

    public ReactiveCommand OpenFile { get; } = new();

    public ReactiveCommand OpenScene { get; }

    public ReactiveCommand AddScene { get; }
    
    public ReactiveCommand RemoveScene { get; }

    public ReactiveCommand CloseFile { get; }

    public ReactiveCommand CloseProject { get; }

    public ReactiveCommand Save { get; }

    public ReactiveCommand SaveAll { get; }

    public ReactiveCommand Exit { get; } = new();

    public ReactiveCommand Undo { get; }

    public ReactiveCommand Redo { get; }

    public IKnownEditorCommands? KnownCommands { get; set; }

    public IReadOnlyReactiveProperty<bool> IsProjectOpened { get; }
}
