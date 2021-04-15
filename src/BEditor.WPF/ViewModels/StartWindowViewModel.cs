using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using BEditor.Models;
using BEditor.Properties;
using BEditor.Views.StartWindowControl;

using MaterialDesignThemes.Wpf;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BEditor.ViewModels
{
    public class StartWindowViewModel
    {
        public StartWindowViewModel()
        {
            MenuItems.Add(new(Strings.Project, PackIconKind.Layers, () => new ProjectsControl())
            {
                IsChecked = { Value = true }
            });
            MenuItems.Add(new(Strings.LearnHowToUse, PackIconKind.School, () => new Learn()));
            MenuItems.Add(new(Strings.Update, PackIconKind.Update, () => new Update()));

            foreach (var item in MenuItems)
            {
                item.IsChecked.Where(v => v).Subscribe(v =>
                {
                    Selected.Value = item;
                });
            }

            _ = GetLatestRelease();
        }

        public ReactiveCollection<MenuItem> MenuItems { get; } = new();

        public ReactivePropertySlim<MenuItem> Selected { get; } = new();


        private async Task<Release?> GetLatestRelease()
        {
            var client = AppData.Current.ServiceProvider.GetRequiredService<HttpClient>();
            await using var memory = await client.GetStreamAsync("https://raw.githubusercontent.com/b-editor/BEditor/main/docs/releases.json");

            if (await JsonSerializer.DeserializeAsync<IEnumerable<Release>>(memory) is var releases && releases is not null)
            {
                var first = releases.First();
                var asmName = typeof(StartWindowViewModel).Assembly.GetName();
                var latest = first.GetVersion();

                if (asmName.Version != latest)
                {
                    var stack = new VirtualizingStackPanel()
                    {
                        Orientation = Orientation.Horizontal,
                        Children =
                        {
                            new TextBlock() { Text = Strings.Update },
                            new Ellipse()
                            {
                                Fill = (asmName.Version < latest) ? Brushes.Orange : Brushes.Green,
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


        public class Release
        {
            [JsonPropertyName("version")]
            public string Version { get; set; } = "";
            [JsonPropertyName("url")]
            public string URL { get; set; } = "";

            public Version? GetVersion()
            {
                return new Version(Version);
            }
        }

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