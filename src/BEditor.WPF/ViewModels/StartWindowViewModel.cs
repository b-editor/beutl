using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using BEditor.Properties;
using BEditor.Views.StartWindowControl;

using MaterialDesignThemes.Wpf;

using Reactive.Bindings;

namespace BEditor.ViewModels
{
    public class StartWindowViewModel
    {
        public StartWindowViewModel()
        {
            MenuItems.Add(new(Resources.Project, PackIconKind.Layers, () => new ProjectsControl())
            {
                IsChecked = { Value = true }
            });
            MenuItems.Add(new(Resources.LearnHowToUse, PackIconKind.School, () => new Learn()));
            MenuItems.Add(new(Resources.Update, PackIconKind.Update, () => new Update()));

            foreach (var item in MenuItems)
            {
                item.IsChecked.Where(v => v).Subscribe(v =>
                {
                    Selected.Value = item;
                });
            }

            _ = CheckVersion();
        }

        public ReactiveCollection<MenuItem> MenuItems { get; } = new();

        public ReactiveProperty<MenuItem> Selected { get; } = new();


        private async Task<Release?> CheckVersion()
        {
            using var client = new HttpClient();
            using var memory = new MemoryStream();
            await memory.WriteAsync(Encoding.UTF8.GetBytes(await client.GetStringAsync("https://raw.githubusercontent.com/b-editor/BEditor/main/docs/releases.json")));

            if (Serialize.LoadFromStream<IEnumerable<Release>>(memory, SerializeMode.Json) is var releases && releases is not null)
            {
                var first = releases.First();
                var asmName = typeof(StartWindowViewModel).Assembly.GetName();

                if (asmName.Version?.ToString(3) != first.Version)
                {
                    var stack = new VirtualizingStackPanel()
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new TextBlock() { Text = Resources.Update },
                            new Ellipse()
                            {
                                Fill = Brushes.Orange,
                                Width = 8,
                                Height = 8,
                                HorizontalAlignment = HorizontalAlignment.Center,
                                Margin = new(8, 0, 0, 0)
                            }
                        }
                    };

                    MenuItems[2].Text.Value = stack;

                    return first;
                }
            }

            return null;
        }

#pragma warning disable CS8618
        [DataContract]
        public class Release
        {
            [DataMember(Name = "version")]
            public string Version { get; set; }
            [DataMember(Name = "url")]
            public string URL { get; set; }
        }
#pragma warning restore CS8618
        public class MenuItem
        {
            private object? _control;

            public MenuItem(object Text, PackIconKind PackIconKind, Func<object?> CreateControl)
            {
                this.Text.Value = Text;
                this.PackIconKind = PackIconKind;
                this.CreateControl = CreateControl;
            }


            public ReactiveProperty<object> Text { get; } = new();

            public PackIconKind PackIconKind { get; set; }

            public Func<object?> CreateControl { get; set; }

            public ReactiveProperty<bool> IsChecked { get; } = new();

            public object? Control => _control ??= CreateControl();
        }
    }
}
