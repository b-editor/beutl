using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;

using BEditor.Models;
using BEditor.ViewModels;

using Microsoft.Extensions.DependencyInjection;

using static BEditor.ViewModels.StartWindowViewModel;

namespace BEditor.Views.StartWindowControl
{
    /// <summary>
    /// Update.xaml の相互作用ロジック
    /// </summary>
    public partial class Update : UserControl
    {
        public Update()
        {
            InitializeComponent();

            _ = Init();
        }

        private async Task Init()
        {
            var client = AppData.Current.ServiceProvider.GetRequiredService<HttpClient>();
            await using var memory = await client.GetStreamAsync("https://raw.githubusercontent.com/b-editor/BEditor/main/docs/releases.json");

            if (await JsonSerializer.DeserializeAsync<IEnumerable<Release>>(memory) is var releases && releases is not null)
            {
                var first = releases.First();
                var asmName = typeof(StartWindowViewModel).Assembly.GetName();
                var latest = first.GetVersion();

                if (asmName.Version < latest)
                {
                    textBlock.Text = Properties.MessageResources.NewVersionIsAvailable;

                    button.Content = Properties.MessageResources.Download;

                    button.Click += (s, e) =>
                    {
                        Process.Start(new ProcessStartInfo("cmd", $"/c start {first.URL}") { CreateNoWindow = true });
                    };
                }
                else
                {
                    textBlock.Text = Properties.MessageResources.ThisSoftwareIsLatest;

                    button.Content = Properties.Resources.OpenThisRepository;

                    button.Click += (s, e) =>
                    {
                        Process.Start(new ProcessStartInfo("cmd", $"/c start https://github.com/b-editor/BEditor/") { CreateNoWindow = true });
                    };
                }
            }
        }
    }
}
