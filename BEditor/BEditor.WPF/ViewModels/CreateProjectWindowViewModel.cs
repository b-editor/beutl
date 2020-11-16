using BEditor.ViewModels.Helper;
using BEditor.ObjectModel.ProjectData;
using Microsoft.WindowsAPICodePack.Dialogs;
using BEditor.ObjectModel;
using BEditor.Models;

namespace BEditor.ViewModels
{
    public class CreateProjectWindowViewModel : BasePropertyChanged
    {
        private int width = 1920;
        private int height = 1080;
        private int franerate = 30;
        private int samlingrate = 0;
        private string name = Settings.Default.LastTimeNum.ToString();
        private string path = Settings.Default.LastTimeFolder;

        public CreateProjectWindowViewModel()
        {
            OpenFolerDialog.Subscribe(OpenFolder);
            CreateCommand.Subscribe(Create);
        }

        public int Width { get => width; set => SetValue(value, ref width, nameof(Width)); }
        public int Height { get => height; set => SetValue(value, ref height, nameof(Height)); }
        public int Framerate { get => franerate; set => SetValue(value, ref franerate, nameof(Framerate)); }
        public int Samplingrate { get => samlingrate; set => SetValue(value, ref samlingrate, nameof(Samplingrate)); }
        public string Name { get => name; set => SetValue(value, ref name, nameof(Name)); }
        public string Path { get => path; set => SetValue(value, ref path, nameof(Path)); }

        public DelegateCommand OpenFolerDialog { get; } = new();
        public DelegateCommand CreateCommand { get; } = new();

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
            var project = new Project(AppData.Current, Width, Height, Framerate)
            {
                Filename = path
            };

            AppData.Current.Project = project;

            project.Save(Path + "\\" + Name + ".bedit");
            AppData.Current.AppStatus = Status.Edit;

            Settings.Default.LastTimeNum++;
            Settings.Default.Save();
        }
    }
}
