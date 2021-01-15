using Microsoft.WindowsAPICodePack.Dialogs;
using BEditor.Core.Data;
using BEditor.Models;
using System.ComponentModel;
using BEditor.Core.Service;
using Reactive.Bindings;

namespace BEditor.ViewModels.CreateDialog
{
    public class ProjectCreateDialogViewModel : BasePropertyChanged
    {
        private static readonly PropertyChangedEventArgs widthArgs = new(nameof(Width));
        private static readonly PropertyChangedEventArgs heightArgs = new(nameof(Height));
        private static readonly PropertyChangedEventArgs framerateArgs = new(nameof(Framerate));
        private static readonly PropertyChangedEventArgs samplingrateArgs = new(nameof(Samplingrate));
        private static readonly PropertyChangedEventArgs nameArgs = new(nameof(Name));
        private static readonly PropertyChangedEventArgs pathArgs = new(nameof(Path));
        private int width = 1920;
        private int height = 1080;
        private int franerate = 30;
        private int samlingrate = 44100;
        private string name = Settings.Default.LastTimeNum.ToString();
        private string path = Settings.Default.LastTimeFolder;

        public ProjectCreateDialogViewModel()
        {
            OpenFolerDialog.Subscribe(OpenFolder);
            CreateCommand.Subscribe(Create);
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
        public int Framerate
        {
            get => franerate;
            set => SetValue(value, ref franerate, framerateArgs);
        }
        public int Samplingrate
        {
            get => samlingrate;
            set => SetValue(value, ref samlingrate, samplingrateArgs);
        }
        public string Name
        {
            get => name;
            set => SetValue(value, ref name, nameArgs);
        }
        public string Path
        {
            get => path;
            set => SetValue(value, ref path, pathArgs);
        }

        public ReactiveCommand OpenFolerDialog { get; } = new();
        public ReactiveCommand CreateCommand { get; } = new();

        private void OpenFolder()
        {
            // ダイアログのインスタンスを生成
            var dialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true
            };

            // ダイアログを表示する
            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                Path = dialog.FileName;

                Settings.Default.LastTimeFolder = dialog.FileName;

                Settings.Default.Save();
            }
        }

        private void Create()
        {
            var project = new Project(Width, Height, Framerate)
            {
                Filename = path
            };
            project.Loaded();
            AppData.Current.Project = project;

            project.Save(Path + "\\" + Name + ".bedit");
            AppData.Current.AppStatus = Status.Edit;

            Settings.Default.LastTimeNum++;
            Settings.Default.MostRecentlyUsedList.Remove(project.Filename);

            Settings.Default.MostRecentlyUsedList.Add(project.Filename);


            Settings.Default.Save();
        }
    }
}
