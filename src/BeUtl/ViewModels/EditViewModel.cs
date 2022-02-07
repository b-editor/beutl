using BeUtl.ProjectSystem;

namespace BeUtl.ViewModels;

public class EditViewModel
{
    public EditViewModel(Scene scene)
    {
        Scene = scene;
        Player = new PlayerViewModel(scene);
        Timeline = new TimelineViewModel(scene, Player);
        Easings = new EasingsViewModel();
    }

    public Scene Scene { get; set; }

    public TimelineViewModel Timeline { get; }

    public PlayerViewModel Player { get; }

    public EasingsViewModel Easings { get; }
}
