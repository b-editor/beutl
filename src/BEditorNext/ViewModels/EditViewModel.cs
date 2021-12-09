using BEditorNext.ProjectSystem;

namespace BEditorNext.ViewModels;

public class EditViewModel
{
    public EditViewModel(Scene scene)
    {
        Scene = scene;
        Timeline = new TimelineViewModel(scene);
        Player = new PlayerViewModel(scene);
    }

    public Scene Scene { get; set; }

    public TimelineViewModel Timeline { get; }

    public PlayerViewModel Player { get; }
}
