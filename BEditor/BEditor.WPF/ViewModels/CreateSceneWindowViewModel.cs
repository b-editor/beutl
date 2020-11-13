using BEditor.ViewModels.Helper;

using BEditor.Core.Data;
using BEditor.Core.Data.ProjectData;

namespace BEditor.ViewModels
{
    public class CreateSceneWindowViewModel : BasePropertyChanged
    {

        private int width = Component.Current.Project.SceneList[0].Width;
        private int height = Component.Current.Project.SceneList[0].Height;
        private string name = $"Scene{Component.Current.Project.SceneList.Count}";

        public CreateSceneWindowViewModel()
        {
            ResetCommand.Subscribe(() =>
            {
                Width = Component.Current.Project.SceneList[0].Width;
                Height = Component.Current.Project.SceneList[0].Height;
                Name = $"Scene{Component.Current.Project.SceneList.Count}";
            });

            CreateCommand.Subscribe(() =>
            {
                var scene = new Scene(Width, Height) { SceneName = Name };
                Component.Current.Project.SceneList.Add(scene);
                Component.Current.Project.PreviewScene = scene;
            });
        }

        public int Width { get => width; set => SetValue(value, ref width, nameof(Width)); }
        public int Height { get => height; set => SetValue(value, ref height, nameof(Height)); }
        public string Name { get => name; set => SetValue(value, ref name, nameof(Name)); }

        public DelegateCommand CreateCommand { get; } = new DelegateCommand();
        public DelegateCommand ResetCommand { get; } = new DelegateCommand();
    }
}
