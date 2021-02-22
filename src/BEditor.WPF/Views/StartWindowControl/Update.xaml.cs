using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using BEditor.ViewModels;

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
            using var client = new HttpClient();
            using var memory = new MemoryStream();
            await memory.WriteAsync(Encoding.UTF8.GetBytes(await client.GetStringAsync("https://raw.githubusercontent.com/b-editor/BEditor/main/docs/releases.json")));

            if (Serialize.LoadFromStream<IEnumerable<Release>>(memory, SerializeMode.Json) is var releases && releases is not null)
            {
                var first = releases.First();
                var asmName = typeof(StartWindowViewModel).Assembly.GetName();

                if (asmName.Version?.ToString(3) != first.Version)
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
