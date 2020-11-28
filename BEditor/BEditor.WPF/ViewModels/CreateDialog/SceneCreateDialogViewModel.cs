using BEditor.Core.Data;
using BEditor.Models;
using System.ComponentModel;
using Reactive.Bindings;

namespace BEditor.ViewModels.CreateDialog
{
    public class SceneCreateDialogViewModel : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs widthArgs = new(nameof(Width));
        private static readonly PropertyChangedEventArgs heightArgs = new(nameof(Height));
        private static readonly PropertyChangedEventArgs nameArgs = new(nameof(Name));
        private int width = AppData.Current.Project.SceneList[0].Width;
        private int height = AppData.Current.Project.SceneList[0].Height;
        private string name = $"Scene{AppData.Current.Project.SceneList.Count}";

        public SceneCreateDialogViewModel()
        {
            ResetCommand.Subscribe(() =>
            {
                Width = AppData.Current.Project.SceneList[0].Width;
                Height = AppData.Current.Project.SceneList[0].Height;
                Name = $"Scene{AppData.Current.Project.SceneList.Count}";
            });

            CreateCommand.Subscribe(() =>
            {
                var scene = new Scene(Width, Height) { SceneName = Name, Parent = AppData.Current.Project };
                AppData.Current.Project.SceneList.Add(scene);
                AppData.Current.Project.PreviewScene = scene;
            });
        }

        public int Width
        {
            get => width;
            set => SetValue(value, ref width, widthArgs);
        }
        public int Height
        {
            get => height;
            set => SetValue(value, ref height, heightArgs);
        }
        public string Name
        {
            get => name;
            set => SetValue(value, ref name, nameArgs);
        }

        public ReactiveCommand CreateCommand { get; } = new();
        public ReactiveCommand ResetCommand { get; } = new();
    }
}
