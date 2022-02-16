using BeUtl.Framework.Service;
using BeUtl.ProjectSystem;
using BeUtl.Services;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BeUtl.ViewModels;

public class MainViewModel
{
    private readonly ProjectService _projectService;

    public MainViewModel()
    {
        _projectService = ServiceLocator.Current.GetRequiredService<ProjectService>();

        IsProjectOpened = _projectService.IsOpened;

        // プロジェクトが開いている時だけ実行できるコマンド
        Save = new(_projectService.IsOpened);
        SaveAll = new(_projectService.IsOpened);
        CloseFile = new(_projectService.IsOpened);
        CloseProject = new(_projectService.IsOpened);
        Undo = new(_projectService.IsOpened);
        Redo = new(_projectService.IsOpened);

        SaveAll.Subscribe(() =>
        {
            Project? project = _projectService.CurrentProject.Value;
            if (project != null)
            {
                INotificationService nservice = ServiceLocator.Current.GetRequiredService<INotificationService>();
                int itemsCount = 0;

                try
                {
                    project.Save(project.FileName);
                    itemsCount++;

                    foreach (Scene scene in project.Children)
                    {
                        scene.Save(scene.FileName);
                        foreach (Layer layer in scene.Children)
                        {
                            layer.Save(layer.FileName);
                            itemsCount++;
                        }
                        itemsCount++;
                    }

                    string message = new ResourceReference<string>("S.Message.ItemsSaved").FindOrDefault(string.Empty);
                    nservice.Show(new Notification(
                        string.Empty,
                        string.Format(message, itemsCount.ToString()),
                        NotificationType.Success));
                }
                catch
                {
                    string message = new ResourceReference<string>("S.Message.OperationCouldNotBeExecuted").FindOrDefault(string.Empty);
                    nservice.Show(new Notification(
                        string.Empty,
                        string.Format(message, itemsCount.ToString()),
                        NotificationType.Error));
                }
            }
        });
        CloseProject.Subscribe(() => _projectService.CloseProject());

        Undo.Subscribe(() => CommandRecorder.Default.Undo());
        Redo.Subscribe(() => CommandRecorder.Default.Redo());
    }

    public ReactiveCommand CreateNewProject { get; } = new();

    public ReactiveCommand CreateNew { get; } = new();

    public ReactiveCommand OpenProject { get; } = new();

    public ReactiveCommand OpenFile { get; } = new();

    public ReactiveCommand CloseFile { get; }

    public ReactiveCommand CloseProject { get; }

    public ReactiveCommand Save { get; }

    public ReactiveCommand SaveAll { get; }

    public ReactiveCommand Exit { get; } = new();

    public ReactiveCommand Undo { get; }

    public ReactiveCommand Redo { get; }

    public ReadOnlyReactivePropertySlim<bool> IsProjectOpened { get; }
}
