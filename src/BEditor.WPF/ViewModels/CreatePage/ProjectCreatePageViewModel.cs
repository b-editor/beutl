using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;

using BEditor.Data;
using BEditor.Models;
using BEditor.Views;
using BEditor.Views.MessageContent;

using Reactive.Bindings;

namespace BEditor.ViewModels.CreatePage
{
    public class ProjectCreatePageViewModel
    {
        private const int width = 1920;
        private const int height = 1080;
        private const int framerate = 30;
        private const int samlingrate = 44100;
        private readonly ReactiveCommand<TemplateItem> _select;
        private static readonly string _file = Path.Combine(AppContext.BaseDirectory, "user", "project_template.xml");


        public ProjectCreatePageViewModel()
        {
            OpenFolerDialog.Subscribe(OpenFolder);
            CreateCommand.Subscribe(Create);
            _select = new ReactiveCommand<TemplateItem>();
            _select.Subscribe(i =>
            {
                Width.Value = i.Width;
                Height.Value = i.Height;
                Framerate.Value = i.Framerate;
                Samplingrate.Value = i.Samplingrate;
            });

            LoadTemplate();
        }

        #region Properties
        public ReactiveProperty<uint> Width { get; } = new(width);
        public ReactiveProperty<uint> Height { get; } = new(height);
        public ReactiveProperty<uint> Framerate { get; } = new(framerate);
        public ReactiveProperty<uint> Samplingrate { get; } = new(samlingrate);
        public ReactiveProperty<string> Name { get; } = new(GenFilename());
        public ReactiveProperty<string> Folder { get; } = new(Settings.Default.LastTimeFolder);
        public ReactiveProperty<bool> SaveToFile { get; } = new(true);

        public ReactiveCommand OpenFolerDialog { get; } = new();
        public ReactiveCommand CreateCommand { get; } = new();
        public ReactiveCollection<TemplateItem> TemplateItems { get; } = new();
        #endregion

        private void OpenFolder()
        {
            var dialog = new OpenFolderDialog();

            if (dialog.ShowDialog())
            {
                Folder.Value = dialog.FileName;

                Settings.Default.LastTimeFolder = dialog.FileName;

                Settings.Default.Save();
            }
        }

        private void Create()
        {
            var project = new Project((int)Width.Value, (int)Height.Value, (int)Framerate.Value, (int)Samplingrate.Value, AppData.Current);
            AppData.Current.Project = project;

            var loading = new Loading()
            {
                IsIndeterminate = { Value = true }
            };
            var dialog = new NoneDialog(loading)
            {
                Owner = App.Current.MainWindow
            };
            dialog.Show();

            Task.Run(() =>
            {
                project.Load();

                if (SaveToFile.Value)
                {
                    project.Save(FormattedFilename(Path.Combine(Folder.Value, Path.GetFileNameWithoutExtension(Name.Value), Name.Value)));

                    var fullpath = Path.Combine(project.DirectoryName!, project.Name + ".bedit");

                    Settings.Default.MostRecentlyUsedList.Remove(fullpath);

                    Settings.Default.MostRecentlyUsedList.Add(fullpath);
                }

                AppData.Current.AppStatus = Status.Edit;

                Settings.Default.Save();

                dialog.Dispatcher.Invoke(dialog.Close);
            });
        }

        private void LoadTemplate()
        {
            static void CreateDefaultColor()
            {
                if (!File.Exists(_file))
                {
                    var elements = new XElement[]
                    {
                        new XElement("Item",
                                new XAttribute("Name", "1920x1080 30 44100"),
                                new XAttribute("Width", 1920),
                                new XAttribute("Height", 1080),
                                new XAttribute("Framerate", 30),
                                new XAttribute("Samplingrate", 44100)),
                        new XElement("Item",
                                new XAttribute("Name", "1920x1080 60 44100"),
                                new XAttribute("Width", 1920),
                                new XAttribute("Height", 1080),
                                new XAttribute("Framerate", 60),
                                new XAttribute("Samplingrate", 44100)),
                        new XElement("Item",
                                new XAttribute("Name", "1080x1920 30 44100"),
                                new XAttribute("Width", 1080),
                                new XAttribute("Height", 1920),
                                new XAttribute("Framerate", 30),
                                new XAttribute("Samplingrate", 44100)),
                        new XElement("Item",
                                new XAttribute("Name", "1080x1920 60 44100"),
                                new XAttribute("Width", 1080),
                                new XAttribute("Height", 1920),
                                new XAttribute("Framerate", 60),
                                new XAttribute("Samplingrate", 44100)),
                        new XElement("Item",
                                new XAttribute("Name", "1000x1000 30 44100"),
                                new XAttribute("Width", 1000),
                                new XAttribute("Height", 1000),
                                new XAttribute("Framerate", 30),
                                new XAttribute("Samplingrate", 44100)),
                        new XElement("Item",
                                new XAttribute("Name", "1000x1000 60 44100"),
                                new XAttribute("Width", 1000),
                                new XAttribute("Height", 1000),
                                new XAttribute("Framerate", 60),
                                new XAttribute("Samplingrate", 44100)),
                        new XElement("Item",
                                new XAttribute("Name", "2000x2000 30 44100"),
                                new XAttribute("Width", 2000),
                                new XAttribute("Height", 2000),
                                new XAttribute("Framerate", 30),
                                new XAttribute("Samplingrate", 44100)),
                        new XElement("Item",
                                new XAttribute("Name", "2000x2000 60 44100"),
                                new XAttribute("Width", 2000),
                                new XAttribute("Height", 2000),
                                new XAttribute("Framerate", 60),
                                new XAttribute("Samplingrate", 44100)),
                    };

                    XDocument XDoc = new XDocument(
                        new XDeclaration("1.0", "utf-8", "true"),
                        new XElement("Items")
                    );

                    foreach (var element in elements) XDoc.Elements().First().Add(element);

                    XDoc.Save(_file);
                }
            }
            Task.Run(async () =>
            {
                CreateDefaultColor();

                using var stream = new FileStream(_file, FileMode.Open);
                // ファイルの読み込み
                var xml = await XDocument.LoadAsync(stream, LoadOptions.None, default);
                var xElement = xml.Root;

                if (xElement is not null)
                {
                    var items = xElement.Elements("Item");

                    foreach (XElement item in items)
                    {
                        string name = item.Attribute("Name")?.Value ?? "?";
                        var width = uint.Parse(item.Attribute("Width")?.Value ?? "1000");
                        var height = uint.Parse(item.Attribute("Height")?.Value ?? "1000");
                        var frame = uint.Parse(item.Attribute("Framerate")?.Value ?? "30");
                        var sampling = uint.Parse(item.Attribute("Samplingrate")?.Value ?? "44100");


                        TemplateItems.AddOnScheduler(new(width, height, frame, sampling, _select, name));
                    }
                }
            });
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

        public record TemplateItem(uint Width, uint Height, uint Framerate, uint Samplingrate, ICommand Command, string Name);
    }
}
