using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Input;

using BEditor.Core.Data;
using BEditor.Core.Service;
using BEditor.Models;
using BEditor.Views;

using Reactive.Bindings;

namespace BEditor.ViewModels.CreatePage
{
    public class ProjectCreatePageViewModel
    {
        private const int width = 1920;
        private const int height = 1080;
        private const int framerate = 30;
        private const int samlingrate = 44100;

        public ProjectCreatePageViewModel()
        {
            OpenFolerDialog.Subscribe(OpenFolder);
            CreateCommand.Subscribe(Create);
            var selectcommand = new ReactiveCommand<TemplateItem>();
            selectcommand.Subscribe(i=>
            {
                Width.Value = i.Width;
                Height.Value = i.Height;
                Framerate.Value = i.Framerate;
                Samplingrate.Value = i.Samplingrate;
            }) ;

            TemplateItems = new()
            {
                new(1920, 1080, 30, 44100, selectcommand),
                new(1920, 1080, 60, 44100, selectcommand),
                new(1080, 1920, 30, 44100, selectcommand),
                new(1080, 1920, 60, 44100, selectcommand),
                new(1000, 1000, 60, 44100, selectcommand),
                new(1000, 1000, 30, 44100, selectcommand),
                new(2000, 2000, 60, 44100, selectcommand),
                new(2000, 2000, 30, 44100, selectcommand),
            };
        }

        public ReactiveProperty<uint> Width { get; } = new(width);
        public ReactiveProperty<uint> Height { get; } = new(height);
        public ReactiveProperty<uint> Framerate { get; } = new(framerate);
        public ReactiveProperty<uint> Samplingrate { get; } = new(samlingrate);
        public ReactiveProperty<string> Name { get; } = new(GenFilename());
        public ReactiveProperty<string> Folder { get; } = new(Settings.Default.LastTimeFolder);
        public ReactiveProperty<bool> SaveToFile { get; } = new(true);

        public ReactiveCommand OpenFolerDialog { get; } = new();
        public ReactiveCommand CreateCommand { get; } = new();
        public ReactiveCollection<TemplateItem> TemplateItems { get; }

        private void OpenFolder()
        {
            // ダイアログのインスタンスを生成
            var dialog = new OpenFolderDialog();

            // ダイアログを表示する
            if (dialog.ShowDialog())
            {
                Folder.Value = dialog.FileName;

                Settings.Default.LastTimeFolder = dialog.FileName;

                Settings.Default.Save();
            }
        }

        private void Create()
        {
            var project = new Project((int)Width.Value, (int)Height.Value, (int)Framerate.Value, (int)Samplingrate.Value);
            project.Load();
            AppData.Current.Project = project;

            if (SaveToFile.Value)
            {
                project.Save(FormattedFilename(Folder.Value + "\\" + Name.Value));
            }

            AppData.Current.AppStatus = Status.Edit;

            Settings.Default.MostRecentlyUsedList.Remove(project.Filename!);

            if (project.Filename is not null)
            {
                Settings.Default.MostRecentlyUsedList.Add(project.Filename);
            }


            Settings.Default.Save();
        }

        private static string FormattedFilename(string original)
        {
            string dir = Path.GetDirectoryName(original)!;
            string name = Path.GetFileNameWithoutExtension(original);
            string ex = ".bedit";

            int count = 0;
            while (File.Exists(dir + "\\" + name + ((count is 0) ? "" : count.ToString()) + ex))
            {
                count++;
            }
            if (count is not 0) name += count.ToString();

            name += ex;

            return dir + "\\" + name;
        }
        private static string GenFilename()
        {
            var file = Settings.Default.MostRecentlyUsedList.LastOrDefault();
            if (file is not null && Path.GetExtension(file) is ".bedit")
            {
                return Path.GetFileName(FormattedFilename(file));
            }


            return Path.GetFileName(FormattedFilename(Settings.Default.LastTimeFolder + "\\" + "Project"));
        }

        public record TemplateItem(uint Width, uint Height, uint Framerate, uint Samplingrate, ICommand Command);
    }
}
