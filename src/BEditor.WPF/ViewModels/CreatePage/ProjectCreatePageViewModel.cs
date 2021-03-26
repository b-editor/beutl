using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Xml.Linq;

using BEditor.Data;
using BEditor.Models;
using BEditor.Properties;
using BEditor.Views;
using BEditor.Views.MessageContent;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels.CreatePage
{
    public sealed class ProjectCreatePageViewModel : IDisposable
    {
        private const int WIDTH = 1920;
        private const int HEIGHT = 1080;
        private const int FRAMERATE = 30;
        private const int SAMLINGRATE = 44100;
        private readonly ReactiveCommand<TemplateItem> _select;
        private static readonly string _file = Path.Combine(AppContext.BaseDirectory, "user", "project_template.xml");
        private readonly CompositeDisposable _disposable = new();

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
            }).AddTo(_disposable);
            Name = new ReactiveProperty<string>(GetNewName())
                .SetValidateNotifyError(name =>
                {
                    if (Directory.Exists(Path.Combine(Folder.Value, name)))
                    {
                        return MessageResources.ThisNameAlreadyExists;
                    }

                    return null;
                });

            LoadTemplate();
        }
        ~ProjectCreatePageViewModel()
        {
            Dispose();
        }

        #region Properties
        public ReactivePropertySlim<uint> Width { get; } = new(WIDTH);
        public ReactivePropertySlim<uint> Height { get; } = new(HEIGHT);
        public ReactivePropertySlim<uint> Framerate { get; } = new(FRAMERATE);
        public ReactivePropertySlim<uint> Samplingrate { get; } = new(SAMLINGRATE);
        public ReactiveProperty<string> Name { get; }
        public ReactivePropertySlim<string> Folder { get; } = new(Settings.Default.LastTimeFolder);

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
            var project = new Project(
                (int)Width.Value,
                (int)Height.Value,
                (int)Framerate.Value,
                (int)Samplingrate.Value,
                AppData.Current,
                Path.Combine(Folder.Value, Name.Value, Name.Value) + ".bedit");


            var loading = new Loading()
            {
                IsIndeterminate = { Value = true }
            };
            var dialog = new NoneDialog(loading)
            {
                Owner = App.Current.MainWindow
            };
            dialog.Show();

            AppData.Current.Project = project;

            Task.Run(() =>
            {
                project.Load();

                {
                    project.Save();

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
            static void CreateDefaultItems()
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

                    var XDoc = new XDocument(
                        new XDeclaration("1.0", "utf-8", "true"),
                        new XElement("Items")
                    );

                    foreach (var element in elements) XDoc.Elements().First().Add(element);

                    XDoc.Save(_file);
                }
            }
            Task.Run(async () =>
            {
                CreateDefaultItems();

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

        private string GetNewName()
        {
            var dirs = Directory.GetDirectories(Folder.Value, "Project*");

            var reg = new Regex(@"^Project([\d]+)\z");

            var values = dirs.Select(i => new DirectoryInfo(i))
                .Select(i => i.Name)
                .Where(i => reg.IsMatch(i))
                .Select(i => reg.Match(i))
                .Select(i => int.Parse(i.Groups[1].Value))
                .ToArray();
            if (values.Length is 0) return "Project1";

            return "Project" + (values.Max() + 1);
        }

        public void Dispose()
        {
            Width.Dispose();
            Height.ToString();
            Framerate.Dispose();
            Samplingrate.Dispose();
            Name.Dispose();
            Folder.Dispose();
            OpenFolerDialog.Dispose();
            CreateCommand.Dispose();
            TemplateItems.Dispose();
            _disposable.Dispose();

            GC.SuppressFinalize(this);
        }

        public record TemplateItem(uint Width, uint Height, uint Framerate, uint Samplingrate, ICommand Command, string Name);
    }
}
