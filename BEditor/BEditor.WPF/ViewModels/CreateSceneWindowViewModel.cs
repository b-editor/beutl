using BEditor.ViewModels.Helper;

using BEditor.ObjectModel;
using BEditor.ObjectModel.ProjectData;
using BEditor.Models;

namespace BEditor.ViewModels
{
    public class CreateSceneWindowViewModel : BasePropertyChanged
    {

        private int width = AppData.Current.Project.SceneList[0].Width;
        private int height = AppData.Current.Project.SceneList[0].Height;
        private string name = $"Scene{AppData.Current.Project.SceneList.Count}";

        public CreateSceneWindowViewModel()
        {
            ResetCommand.Subscribe(() =>
            {
                Width = AppData.Current.Project.SceneList[0].Width;
                Height = AppData.Current.Project.SceneList[0].Height;
                Name = $"Scene{AppData.Current.Project.SceneList.Count}";
            });

            CreateCommand.Subscribe(() =>
            {
                var scene = new Scene(Width, Height) { SceneName = Name };
                AppData.Current.Project.SceneList.Add(scene);
                AppData.Current.Project.PreviewScene = scene;
            });
        }

        public int Width { get => width; set => SetValue(value, ref width, nameof(Width)); }
        public int Height { get => height; set => SetValue(value, ref height, nameof(Height)); }
        public string Name { get => name; set => SetValue(value, ref name, nameof(Name)); }

        public DelegateCommand CreateCommand { get; } = new DelegateCommand();
        public DelegateCommand ResetCommand { get; } = new DelegateCommand();
    }
}
